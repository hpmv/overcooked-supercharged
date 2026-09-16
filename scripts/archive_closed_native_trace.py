"""Archive a CLOSED native trial's exact gzip-decoded bytes, without deleting it.

Unlike the gate archive tool, failed finalized native trials are eligible. An
explicit finalized native-search summary must pin the selected trace and exit.
Windows sharing locks refuse active writers; gzip EOF validates every member CRC.
"""
from __future__ import annotations
import argparse
import contextlib
import gzip
import hashlib
import json
import lzma
import os
from pathlib import Path
import re
import sys
import time

from archive_closed_gate_logs import locked_read, sha_stream, require, CHUNK

ROOT = Path(__file__).resolve().parents[1]
FORMAT = "oc2-closed-native-trace-byte-archive"
HEX = re.compile(r"[0-9a-fA-F]{64}\Z")


def within(path, root, suffix=None):
    path = Path(path).resolve()
    require(path.is_relative_to((root / "artifacts").resolve()), "Path must resolve inside workspace artifacts.")
    if suffix:
        require(path.name.endswith(suffix), "Unsupported file extension: " + str(path))
    return path


def read_json(stream):
    raw = stream.read(32 * 1024 * 1024 + 1)
    require(len(raw) <= 32 * 1024 * 1024, "Provenance/manifest exceeds32MiB.")
    return json.loads(raw.decode("utf-8-sig")), hashlib.sha256(raw).hexdigest(), len(raw)


def identity(path):
    s = path.stat()
    return {"bytes": s.st_size, "mtimeNs": s.st_mtime_ns, "fileId": s.st_ino}


def byte_proof(path):
    with locked_read(path) as stream:
        sha, size = sha_stream(stream)
    return {"path": str(path), "sha256": sha, "bytes": size}


def terminal_provenance(data, source, trial):
    require(data.get("format") == "oc2-native-candidate-search" and data.get("version") in (1, 2), "Requires a finalized native candidate-search summary, not a watcher or inferred mtime.")
    matches = [r for r in data.get("results", []) if r.get("trial") == trial]
    require(len(matches) == 1, "Summary must contain one exact trial ordinal.")
    result = matches[0]
    require(type(result.get("exitCode")) is int, "Trial has no terminal controller exitCode.")
    require(isinstance(result.get("trace"), str) and Path(result["trace"]).resolve() == source, "Terminal trial names another trace.")
    require(isinstance(result.get("traceSha256"), str) and HEX.fullmatch(result["traceSha256"]), "Terminal trace SHA256 absent.")
    return {"trial": trial, "traceSha256": result["traceSha256"].lower(), "exitCode": result["exitCode"],
            "failure": result.get("failure"), "observationSource": result.get("observationSource"),
            "completedRequestedRun": result.get("completedRequestedRun"), "completedFullRound": result.get("completedFullRound"),
            "qualifyingNativeHighScoreRound": result.get("qualifyingNativeHighScoreRound"),
            "gameplayFrame": result.get("gameplayFrame"), "score": result.get("score"), "delivered": result.get("delivered"),
            "controllerSha256": result.get("observedControllerSha256", data.get("controllerSha256")),
            "qualification": "Archival proves closed bytes, not a passed trial. Recorded outcome fields are preserved without upgrading qualification."}


@contextlib.contextmanager
def source_transaction(source, provenance, trial, root=ROOT):
    require(os.name == "nt", "Writer exclusion currently requires Windows sharing locks; refuse an unlocked fallback.")
    source = within(source, root, ".jsonl.gz"); provenance = within(provenance, root, ".json")
    require(source != provenance and type(trial) is int and trial > 0, "Invalid trial/source/provenance.")
    with locked_read(provenance) as evidence, locked_read(source) as stream:
        data, evidence_sha, evidence_size = read_json(evidence)
        closed = terminal_provenance(data, source, trial)
        before = identity(source); original_sha, original_size = sha_stream(stream)
        require(original_sha == closed["traceSha256"] and original_size == before["bytes"], "Gzip bytes differ from terminal trial provenance.")
        stream.seek(0)
        yield source, stream, {"path": str(source), "sha256": original_sha, **before}, {
            "provenance": {"path": str(provenance), "sha256": evidence_sha, "bytes": evidence_size}, "terminalTrial": closed}
        stream.seek(0); require(sha_stream(stream) == (original_sha, original_size) and identity(source) == before, "Source changed during locked operation.")
        evidence.seek(0); require(sha_stream(evidence) == (evidence_sha, evidence_size), "Closed-trial provenance changed during operation.")


def scan_gzip(stream, consume=None):
    digest = hashlib.sha256(); size = 0
    # Reading to EOF checks trailer size/CRC and every concatenated gzip member.
    with gzip.GzipFile(fileobj=stream, mode="rb") as raw:
        while block := raw.read(CHUNK):
            digest.update(block); size += len(block)
            if consume is not None:
                consume(block)
    return {"sha256": digest.hexdigest(), "bytes": size}


def plan(source, provenance, trial, root=ROOT):
    with source_transaction(source, provenance, trial, root) as (path, stream, original, closed):
        raw = scan_gzip(stream)
        result = {"ok": True, "mode": "read-only-plan", "source": original, "closedSourceEvidence": closed,
                  "uncompressed": raw, "completeGzipCRCValidated": True, "proposedArchive": str(path.with_suffix(".xz")),
                  "qualification": "Closed byte stream eligible for archival, regardless of native trial success. No archive or deletion performed."}
    return result


def verify_locked(stream, expected):
    stream.seek(0)
    require(sha_stream(stream) == (expected["archive"]["sha256"], expected["archive"]["bytes"]), "XZ container hash/size mismatch.")
    stream.seek(0); digest = hashlib.sha256(); size = 0
    with lzma.LZMAFile(stream, "rb") as raw:
        while block := raw.read(CHUNK):
            digest.update(block); size += len(block)
            require(size <= expected["uncompressed"]["bytes"], "XZ expands beyond the pinned raw byte count.")
    require((digest.hexdigest(), size) == (expected["uncompressed"]["sha256"], expected["uncompressed"]["bytes"]), "XZ restored bytes differ from pinned original raw bytes.")
    return {"verifiedUncompressedSha256": digest.hexdigest(), "verifiedUncompressedBytes": size}


def validate_manifest(data):
    require(data.get("format") == FORMAT and data.get("version") == 1, "Unknown native archive manifest.")
    for field in ("source", "archive", "uncompressed"):
        require(isinstance(data[field].get("sha256"), str) and HEX.fullmatch(data[field]["sha256"]) and type(data[field].get("bytes")) is int and data[field]["bytes"] >= 0, "Invalid manifest byte identity.")
    require(data.get("verification", {}).get("completeSourceGzipCRCValidated") is True, "Manifest lacks completed source CRC proof.")


def archive(source, provenance, trial, dictionary_mib=32, root=ROOT):
    require(dictionary_mib in (16, 32, 64), "Dictionary must be16,32or64MiB.")
    source = within(source, root, ".jsonl.gz")
    destination = within(source.with_suffix(".xz"), root, ".jsonl.xz")
    partial = Path(str(destination) + ".partial"); manifest = Path(str(destination) + ".manifest.json")
    require(not any(p.exists() for p in (destination, partial, manifest)), "Archive/manifest/partial already exists; refusing overwrite.")
    started = last_report = time.monotonic()
    filters = [{"id": lzma.FILTER_LZMA2, "preset": 1, "dict_size": dictionary_mib * 1024 * 1024}]
    with source_transaction(source, provenance, trial, root) as (source, stream, original, closed):
        with partial.open("xb") as sink:
            compressor = lzma.LZMACompressor(format=lzma.FORMAT_XZ, check=lzma.CHECK_CRC64, filters=filters)
            total = 0
            def consume(block):
                nonlocal total, last_report
                total += len(block); sink.write(compressor.compress(block))
                now = time.monotonic()
                if now - last_report >= 10:
                    print(f"compress: {total/2**20:.0f}MiB raw, {sink.tell()/2**20:.1f}MiB XZ, {100*stream.tell()/original['bytes']:.1f}% source", file=sys.stderr, flush=True)
                    last_report = now
            raw = scan_gzip(stream, consume)
            sink.write(compressor.flush()); sink.flush(); os.fsync(sink.fileno())
        with locked_read(partial) as packed:
            archive_sha, archive_size = sha_stream(packed)
            candidate = {"archive": {"sha256": archive_sha, "bytes": archive_size}, "uncompressed": raw}
            verify_locked(packed, candidate)
        # Windows rename refuses an existing target. No replacement/deletion API.
        require(not destination.exists() and not manifest.exists(), "Output appeared while compressing; partial retained.")
        partial.rename(destination)
        result = {"format": FORMAT, "version": 1, "createdUtc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
                  "source": original, "archive": {"path": str(destination), "sha256": archive_sha, "bytes": archive_size}, "uncompressed": raw,
                  "closedSourceEvidence": closed, "compression": {"format": "XZ", "filter": "LZMA2", "preset": 1, "dictionaryBytes": dictionary_mib * 1024 * 1024, "check": "CRC64"},
                  "verification": {"completeSourceGzipCRCValidated": True, "xzDecompressedBytesEqualOriginal": True, "sourceHeldAgainstWriters": True, "sourceGzipUnchanged": True},
                  "containerIdentity": "Original gzip SHA256 is pinned. Recreated gzip headers/compression can differ; exact original uncompressed JSONL bytes are preserved.",
                  "deletion": "No source or other trace files deleted.", "wallSeconds": time.monotonic() - started,
                  "toolSha256": byte_proof(Path(__file__))["sha256"], "lockImplementationSha256": byte_proof(Path(__file__).with_name("archive_closed_gate_logs.py"))["sha256"]}
        # Independent context exit rechecks source/provenance before success.
    with manifest.open("x", encoding="utf-8", newline="\n") as sink:
        json.dump(result, sink, indent=2); sink.write("\n")
    return result


@contextlib.contextmanager
def archive_transaction(archive, root=ROOT):
    require(os.name == "nt", "Archive verification/restoration requires Windows sharing locks.")
    archive = within(archive, root, ".jsonl.xz"); manifest = Path(str(archive) + ".manifest.json")
    with locked_read(manifest) as metadata, locked_read(archive) as packed:
        data, sha, size = read_json(metadata); validate_manifest(data)
        verified = verify_locked(packed, data)
        yield archive, packed, data, verified
        metadata.seek(0); require(sha_stream(metadata) == (sha, size), "Archive manifest changed during operation.")
        packed.seek(0); require(sha_stream(packed) == (data["archive"]["sha256"], data["archive"]["bytes"]), "Archive changed during operation.")


def verify(archive, root=ROOT):
    with archive_transaction(archive, root) as (_, _, data, verified):
        result = {"ok": True, **verified, "originalGzip": data["source"], "terminalTrial": data["closedSourceEvidence"]["terminalTrial"]}
    return result


def restore(archive, output, root=ROOT):
    output = within(output, root)
    require(output.name.endswith((".jsonl", ".jsonl.gz")), "Restore needs a new .jsonl or .jsonl.gz output.")
    partial = Path(str(output) + ".partial")
    require(not output.exists() and not partial.exists(), "Restore target/partial exists; refusing overwrite.")
    with archive_transaction(archive, root) as (_, packed, data, verified):
        packed.seek(0)
        with lzma.LZMAFile(packed, "rb") as raw, partial.open("xb") as sink:
            if output.name.endswith(".gz"):
                with gzip.GzipFile(filename="", mode="wb", fileobj=sink, mtime=0, compresslevel=6) as encoded:
                    while block := raw.read(CHUNK): encoded.write(block)
            else:
                while block := raw.read(CHUNK): sink.write(block)
            sink.flush(); os.fsync(sink.fileno())
        with locked_read(partial) as check:
            restored = scan_gzip(check) if output.name.endswith(".gz") else dict(zip(("sha256", "bytes"), sha_stream(check)))
        require(restored == data["uncompressed"], "Restored output differs; partial retained.")
        require(not output.exists(), "Restore output appeared; partial retained.")
        partial.rename(output)
    return {"ok": True, "restored": byte_proof(output), **verified, "gzipContainerMayDiffer": output.name.endswith(".gz"), "sourceAndArchiveUntouched": True}


def main():
    parser = argparse.ArgumentParser(description=__doc__); sub = parser.add_subparsers(dest="command", required=True)
    for name in ("plan", "archive"):
        p = sub.add_parser(name); p.add_argument("source"); p.add_argument("--provenance", required=True); p.add_argument("--trial", required=True, type=int)
        if name == "archive": p.add_argument("--dictionary-mib", type=int, default=32)
    p = sub.add_parser("verify"); p.add_argument("archive")
    p = sub.add_parser("restore"); p.add_argument("archive"); p.add_argument("output")
    args = parser.parse_args()
    if args.command == "plan": result = plan(args.source, args.provenance, args.trial)
    elif args.command == "archive": result = archive(args.source, args.provenance, args.trial, args.dictionary_mib)
    elif args.command == "verify": result = verify(args.archive)
    else: result = restore(args.archive, args.output)
    print(json.dumps(result, indent=2))


if __name__ == "__main__":
    try: main()
    except (ValueError, OSError, EOFError, lzma.LZMAError, json.JSONDecodeError) as error:
        print(json.dumps({"ok": False, "error": str(error)}), file=sys.stderr); sys.exit(1)
