#!/usr/bin/env python3
"""Extract immutable protocol requests from OC2 JSON/JSONL(.gz) recordings.

Default: preserve every request and its step count in original order. This does
not reconstruct commands from observed pads, discard inspection calls, normalize
axes, append neutral input, or change seeds. --expand-step1 explicitly splits
step requests; its source-audited button semantics still need a live comparison
at the original checkpoints before treating expanded execution as equivalent.

Example: python scripts/extract_inputs.py trace.jsonl.gz --out movie.jsonl
"""

import argparse
import gzip
import hashlib
import json
import os
import sys
import tempfile
from collections import Counter
from pathlib import Path


EXPANSION_AUDIT = {
    "status": "Input-state idempotence established from current source; full runtime equivalence not yet established",
    "sources": ["plugin/Inputs.cs:Inputs.Apply/Button.Poll",
                "native LogicalButtonBase.Update(bool)", "plugin/Plugin.cs:Handle/FrameBoundary/Complete"],
    "buttonReasoning": [
        "For unchanged down-state d: pressClaimed := pressClaimed AND d and releaseClaimed := releaseClaimed AND NOT d are idempotent",
        "buttonDownTime changes only on a false-to-true transition; identical polls do not restart held duration",
        "Native readers also call Update(IsDown()); input claims remain native",
        "Each expansion repeats the original unnormalized request, so each Apply computes the same axes without iterative normalization"],
    "conditions": [
        "Emulated Inputs.Active stays enabled and the controller remains connected",
        "All intervening expanded requests repeat the identical original inputs; no release, pause, reconnect or steering is inserted",
        "The game remains gated at whole-frame boundaries with fixed capture time"],
    "remainingValidation": [
        "Extra protocol completions capture telemetry and suspend wall time; compare expanded replay against the original recording at every original checkpoint",
        "Compare native event frames, physics hashes, food/cooking state, timer and input claims, retaining separately reported clock differences",
        "Only then use the expanded movie for twenty replays, including five fresh process starts"],
}


def encode(value):
    return (json.dumps(value, ensure_ascii=False, sort_keys=True, separators=(",", ":"), allow_nan=False) + "\n").encode("utf-8")


def sha256_file(path):
    digest = hashlib.sha256()
    with open(path, "rb") as source:
        while True:
            block = source.read(1024 * 1024)
            if not block:
                break
            digest.update(block)
    return digest.hexdigest()


def entries(path):
    opener = gzip.open if str(path).lower().endswith(".gz") else open
    with opener(path, "rt", encoding="utf-8-sig") as source:
        if ".jsonl" not in str(path).lower():
            yield 1, json.load(source)
            return
        for number, line in enumerate(source, 1):
            if not line.strip() or line.lstrip().startswith("#"):
                continue
            try:
                yield number, json.loads(line)
            except json.JSONDecodeError as error:
                raise ValueError("Invalid JSON at %s:%s: %s" % (path, number, error)) from error


def request_from(entry, line):
    if not isinstance(entry, dict):
        raise ValueError("Expected an object at line %s" % line)
    if entry.get("kind") in {"header", "event"}:
        return None
    if "request" in entry:
        request = entry["request"]
        response = entry.get("response")
        if isinstance(response, dict) and response.get("ok") is False:
            raise ValueError("Recorded request at line %s failed: %s" % (line, response.get("error", "unknown error")))
    else:
        request = entry
    if not isinstance(request, dict) or not isinstance(request.get("command"), str):
        raise ValueError("No original protocol request at line %s; response inputs are not sufficient to reconstruct one" % line)
    if request.get("version") != 1:
        raise ValueError("Expected protocol version 1 at line %s" % line)
    if request["command"] == "step":
        count = request.get("steps", 1)
        if type(count) is not int or not 1 <= count <= 60000:
            raise ValueError("Invalid step count at line %s" % line)
    return request


def extract(source_path, output_path, manifest_path=None, expand=False, force=False):
    source_path = Path(source_path).resolve()
    output_path = Path(output_path).resolve()
    manifest_path = Path(manifest_path).resolve() if manifest_path else Path(str(output_path) + ".manifest.json")
    if len({source_path, output_path, manifest_path}) != 3:
        raise ValueError("Input, movie and manifest paths must be distinct")
    if not force and (output_path.exists() or manifest_path.exists()):
        raise ValueError("Movie or manifest already exists; choose new paths or pass --force")
    before = source_path.stat()
    source_hash = sha256_file(source_path)
    output_path.parent.mkdir(parents=True, exist_ok=True)
    manifest_path.parent.mkdir(parents=True, exist_ok=True)
    original_hash, movie_hash = hashlib.sha256(), hashlib.sha256()
    commands, ignored = Counter(), Counter()
    total_frames, input_count, output_count = 0, 0, 0
    starts, expanded, headers = [], [], []
    temporary_movie = None
    temporary_manifest = None
    try:
        with tempfile.NamedTemporaryFile("wb", prefix=".oc2-movie-", suffix=".tmp", dir=output_path.parent, delete=False) as raw:
            temporary_movie = Path(raw.name)
            compressed = gzip.GzipFile(filename="", mode="wb", fileobj=raw, mtime=0) if str(output_path).lower().endswith(".gz") else None
            writer = compressed if compressed is not None else raw
            try:
                for line, entry in entries(source_path):
                    request = request_from(entry, line)
                    if request is None:
                        ignored[entry.get("kind", "unknown")] += 1
                        if entry.get("kind") == "header":
                            headers.append(entry)
                        continue
                    original_hash.update(encode(request))
                    command = request["command"]
                    commands[command] += 1
                    step_count = request.get("steps", 1) if command == "step" else 0
                    total_frames += step_count
                    if command in {"restart", "load"}:
                        starts.append({"sourceRequestIndex": input_count, "request": request})
                    if expand and step_count > 1:
                        emitted = dict(request)
                        emitted["steps"] = 1
                        copies = step_count
                        expanded.append({"sourceLine": line, "sourceRequestIndex": input_count,
                                         "originalSteps": step_count, "firstOutputRequestIndex": output_count,
                                         "lastOutputRequestIndex": output_count + copies - 1,
                                         "cumulativeSteppedFrames": total_frames})
                    else:
                        emitted, copies = request, 1
                    data = encode(emitted)
                    for _ in range(copies):
                        writer.write(data)
                        movie_hash.update(data)
                    input_count += 1
                    output_count += copies
                if input_count == 0:
                    raise ValueError("No protocol requests found")
            finally:
                if compressed is not None:
                    compressed.close()
        after = source_path.stat()
        if (before.st_size, before.st_mtime_ns) != (after.st_size, after.st_mtime_ns):
            raise ValueError("Input recording changed during extraction; finish recording before extraction")
        output_file_hash = sha256_file(temporary_movie)
        manifest = {
            "format": "oc2-input-movie-manifest", "version": 1,
            "source": str(source_path), "sourceFileSha256": source_hash,
            "sourceSizeBytes": before.st_size, "sourceHeaders": headers,
            "originalCanonicalRequestsSha256": original_hash.hexdigest(),
            "output": str(output_path), "outputFileSha256": output_file_hash,
            "outputCanonicalRequestsSha256": movie_hash.hexdigest(),
            "originalRequests": input_count, "outputRequests": output_count,
            "totalSteppedFrames": total_frames, "commands": dict(commands),
            "skippedMetadata": dict(ignored), "starts": starts,
            "requestBoundariesPreserved": not expanded,
            "allInputsAndOtherRequestFieldsPreserved": True,
            "expansionRequested": expand, "expandedBoundaries": expanded,
            "expansionAudit": EXPANSION_AUDIT if expand else None,
            "hashEncoding": "Source/outputFile are exact file bytes; canonical requests are sorted-key compact UTF-8 JSON with LF, before compression",
            "replayNote": "Input-only movie: execute record --script MOVIE --out TRACE, or replay --file MOVIE --no-compare. It contains no expected responses.",
        }
        with tempfile.NamedTemporaryFile("w", encoding="utf-8", newline="\n", prefix=".oc2-manifest-", suffix=".tmp", dir=manifest_path.parent, delete=False) as destination:
            temporary_manifest = Path(destination.name)
            json.dump(manifest, destination, indent=2, ensure_ascii=False, allow_nan=False)
            destination.write("\n")
        os.replace(temporary_movie, output_path)
        temporary_movie = None
        os.replace(temporary_manifest, manifest_path)
        temporary_manifest = None
        return {"movie": str(output_path), "manifest": str(manifest_path),
                "sourceFileSha256": source_hash, "movieFileSha256": output_file_hash,
                "movieCanonicalRequestsSha256": movie_hash.hexdigest(),
                "requests": output_count, "steppedFrames": total_frames,
                "requestBoundariesPreserved": not expanded,
                "expandedSourceRequests": len(expanded)}
    finally:
        for temporary in (temporary_movie, temporary_manifest):
            if temporary is not None:
                temporary.unlink(missing_ok=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("source", type=Path)
    parser.add_argument("--out", required=True, type=Path)
    parser.add_argument("--manifest", type=Path)
    parser.add_argument("--expand-step1", action="store_true", help="Explicitly split step N into N identical-input step 1 requests; see manifest for required validation")
    parser.add_argument("--force", action="store_true", help="Replace existing movie/manifest; never overwrite source")
    args = parser.parse_args()
    try:
        result = extract(args.source, args.out, args.manifest, args.expand_step1, args.force)
    except (OSError, ValueError, EOFError) as error:
        print("extraction failed: " + str(error), file=sys.stderr)
        return 2
    print(json.dumps(result, indent=2, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
