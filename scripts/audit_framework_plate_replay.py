"""Bind a completed checkpoint plate search to immutable bytes and audit its replay.

No native calls. A raw-capture's recorded end offset binds the latest traced
control request, which must equal the saved candidate/raw request. Every input
exchange in that command interval must match the capture, in order. The final
advancing output (usually absent from the input-only capture) is independently
required. Ambiguous, partial, or mismatched evidence is an audit error.
"""
from __future__ import annotations

import argparse
import collections
import datetime
import hashlib
import json
from pathlib import Path
import re

import compare_framework_frames as parity
from framework_plate_search import normalize_inputs, to_raw_request


class AuditError(ValueError):
    pass


def require(condition, message):
    if not condition:
        raise AuditError(message)


def read(path):
    return parity.loads(Path(path).read_text(encoding="utf-8-sig"))


def save(path, value):
    Path(path).write_text(json.dumps(value, indent=2, allow_nan=False) + "\n", encoding="utf8")


def file_hash(path):
    with Path(path).open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def receipt(path):
    path = Path(path).resolve()
    return {"path": str(path), "bytes": path.stat().st_size, "sha256": file_hash(path)}


def copy_range(source, target, start, end):
    """Copy exact bytes, then re-read the same source range to detect mutation."""
    require(0 <= start < end <= source.stat().st_size, "Source is shorter than the recorded byte range")
    digest = hashlib.sha256()
    with source.open("rb") as src, target.open("xb") as dst:
        src.seek(start)
        remaining = end - start
        while remaining:
            data = src.read(min(remaining, 1048576))
            require(bool(data), "Source truncated during freezing")
            dst.write(data); digest.update(data); remaining -= len(data)
        src.seek(start)
        check = hashlib.sha256(); remaining = end - start
        while remaining:
            data = src.read(min(remaining, 1048576))
            require(bool(data), "Source truncated during verification")
            check.update(data); remaining -= len(data)
    require(digest.digest() == check.digest(), "Source bytes changed while freezing")
    require(file_hash(target) == digest.hexdigest(), "Frozen copy differs from source bytes")
    return {**receipt(target), "sourceStartByte": start, "sourceEndByte": end}


def complete_lines(path, capture_decoder=False):
    """The recorded cursor can end inside a later paused line. Never parse it."""
    offset = 0
    with Path(path).open("rb") as stream:
        for number, raw in enumerate(stream, 1):
            end = offset + len(raw)
            if not raw.endswith(b"\n"):
                return
            require(len(raw) <= 4 * 1024 * 1024, "Oversized trace line")
            if raw.strip():
                # AdvancingTrace used json.loads before writing rawExchanges;
                # its integer -0 becomes 0. Reproduce that producer only for
                # locating receipts, never for the actual parity comparison.
                decoder = json.loads if capture_decoder else parity.loads
                yield decoder(raw.decode("utf-8-sig")), {"line": number, "startByte": offset, "endByte": end}
            offset = end


def validate_capture(capture, case, expected_frames):
    start, end = capture.get("startExclusive"), capture.get("endInclusive")
    require(type(start) is int and type(end) is int and start == case["frame"] and end - start == expected_frames,
            "Capture interval does not match the selected completed trial")
    require(capture.get("validation") == "exact-four-pad-frame-coverage", "Capture did not finish exact input validation")
    require(type(capture.get("traceByteOffset")) is int and capture["traceByteOffset"] > 0, "Missing recorded source byte offset")
    raw = capture.get("rawExchanges")
    require(isinstance(raw, list) and len(raw) == expected_frames, "Raw capture is not exactly the selected interval")
    normalized = []
    for ordinal, row in enumerate(raw):
        require(row.get("kind") == "exchange", "Raw capture contains a non-exchange")
        value = row.get("input") or {}
        require(value.get("NextFrame") == start + ordinal + 1 and value.get("Input") is not None,
                "Missing, duplicate or unordered captured input frame")
        require(value.get("Warp") is None and not (value.get("__isset", {}).get("resetOrderSeed") and value.get("ResetOrderSeed") is not None),
                "Authoring/seed directive inside candidate input")
        normalized.append({"ordinal": ordinal, "nextFrame": start + ordinal + 1,
                           "inputs": normalize_inputs(value["Input"], case["chefs"])})
    require(parity.first_difference(normalized, capture.get("inputs")) is None,
            "Saved normalized inputs differ from the original raw capture")
    return normalized


def bind_capture(prefix, capture, request):
    """Derive command/load/warp epoch from observed bytes, never filename guesses."""
    cutoff = capture["traceByteOffset"]
    control = load = None
    epoch = 0
    pending_warp = False
    complete_end = 0
    # At the persisted read offset, the most recent traced control must still be
    # this exact operation. This disambiguates even bit-identical prior runs.
    for row, location in complete_lines(prefix):
        if location["endByte"] > cutoff:
            break
        complete_end = location["endByte"]
        kind = row.get("kind")
        if kind == "control":
            control = {**location, "request": row.get("request")}
            continue
        if kind not in {"exchange", "paused-exchanges"}:
            continue
        if pending_warp:
            epoch += 1; pending_warp = False
        if kind == "paused-exchanges":
            continue  # Fully validated/expanded by the strict window reader.
        output = row.get("output") or {}
        if any(m.get("Type") == 1 for m in output.get("ServerMessages") or []):
            load = location; epoch = 0
        if (row.get("input") or {}).get("Warp") is not None:
            pending_warp = True
    require(control is not None and parity.first_difference(control["request"], request) is None,
            "Recorded offset is not bound to the exact saved control request")
    require(load is not None and load["startByte"] < control["startByte"], "Missing native load before selected operation")
    require(not pending_warp, "Capture ends with an unresolved authoring warp")
    raw_rows = capture["rawExchanges"]
    matched = []
    terminals = []
    for row, location in complete_lines(prefix, capture_decoder=True):
        if location["endByte"] > cutoff:
            break
        if location["startByte"] <= control["startByte"] or row.get("kind") != "exchange":
            continue
        value = row.get("input") or {}
        output = row.get("output") or {}
        require(value.get("Warp") is None, "Selected command interval contains a warp")
        if value.get("Input"):
            i = len(matched)
            require(i < len(raw_rows), "Ambiguous extra input exchange in selected command interval")
            difference = parity.first_difference(row, raw_rows[i])
            require(difference is None, "Raw capture/source mismatch at input ordinal " + str(i) + ": " + str(difference))
            matched.append(location)
        if output.get("FrameNumber") == capture["endInclusive"] and output.get("LastFramePaused") is False:
            require(output.get("NextFramePaused") is True, "Terminal advancing output has not requested a pause")
            terminals.append(location)
    require(len(matched) == len(raw_rows), "Source lacks the complete captured input sequence")
    require(len(terminals) == 1, "Missing or ambiguous terminal advancing output")
    require(terminals[0]["startByte"] > matched[-1]["startByte"], "Terminal precedes the final emitted input")
    return {"epoch": epoch, "load": load, "control": control, "firstInput": matched[0], "lastInput": matched[-1],
            "terminalOutput": terminals[0], "recordedReadOffset": cutoff, "lastCompleteByteAtOffset": complete_end,
            "inputExchangesVerified": len(matched), "binding": "Exact latest control at recorded end offset; every input exchange under its original capture JSON decoder and unique native terminal output",
            "captureRepresentationLimit": "AdvancingTrace json.loads loses integer -0. Binding reproduces that producer; strict parity reads original source bytes with signed zero preserved."}


def review(window, bindings, start, end, out):
    original, replay = bindings["original"]["epoch"], bindings["replay"]["epoch"]
    epochs = parity.selected_epochs(window, {original, replay}, start, end)
    left, right = epochs[original], epochs[replay]
    report = parity.compare(left, right, start, end)
    save(out / "strict-comparison.json", report)
    timeline, first_entities = [], {}
    for frame in range(start + 1, end + 1):
        differences = {}
        for name in report["checks"]:
            a, b = (left["inputs"][frame]["value"], right["inputs"][frame]["value"]) if name == "inputs" else (left["frames"][frame][name], right["frames"][frame][name])
            difference = parity.first_difference(a, b, "$frame/" + name)
            if difference:
                differences[name] = difference
        for entity in left["frames"][frame]["physics"].keys() | right["frames"][frame]["physics"].keys():
            difference = parity.first_difference(left["frames"][frame]["physics"].get(entity), right["frames"][frame]["physics"].get(entity))
            if difference and entity not in first_entities:
                first_entities[entity] = {"frame": frame, "offset": frame-start, "difference": difference}
        if differences:
            timeline.append({"frame": frame, "offset": frame-start, "differences": differences,
                             "originalLocation": left["frames"][frame]["location"], "replayLocation": right["frames"][frame]["location"]})
    save(out / "divergence-timeline.json", timeline)
    first = report["firstDivergence"]["frame"] if report["firstDivergence"] else end
    context = {str(f): {"original": left["frames"][f], "replay": right["frames"][f],
                        "originalInput": left["inputs"][f], "replayInput": right["inputs"][f]}
               for f in range(max(start+1, first-2), min(end, first+5)+1)}
    save(out / "first-divergence-context.json", context)
    world = {name: [{"frame": f, "offset": f-start, "messageOrder": i, **m}
                    for f, row in epochs[binding["epoch"]]["frames"].items()
                    for i, m in enumerate(row["nativeMessages"]) if m.get("nativeComponentType") == 1]
             for name, binding in bindings.items()}
    save(out / "world-object-event-timeline.json", world)
    return {"passed": report["passed"], "firstDivergence": report["firstDivergence"], "checks": report["checks"],
            "boundaryEqual": report["boundaryEqual"], "startExclusive": start, "endInclusive": end,
            "comparedFrames": end-start, "differingFrameCount": len(timeline), "firstPhysicalDifferenceByEntity": first_entities,
            "worldObjectEvents": world,
            "nativeMessageCounts": {name: dict(collections.Counter(str(m["type"]) for row in epochs[b["epoch"]]["frames"].values() for m in row["nativeMessages"]))
                                    for name, b in bindings.items()}}


def audit(run, out, trace=None):
    run, out = Path(run).resolve(), Path(out).resolve()
    out.mkdir(parents=True, exist_ok=False)
    summary = {"passed": False, "auditCompleted": False, "run": str(run),
               "classification": "Offline strict checkpoint plate replay; raw differences are never waived"}
    manifest = {"createdUtc": datetime.datetime.now(datetime.timezone.utc).isoformat(), "run": str(run),
                "auditor": receipt(__file__), "comparator": receipt(parity.__file__)}
    try:
        result = read(run / "summary.json"); case = read(run / "case.json")
        chosen = result.get("selected")
        require(isinstance(chosen, str) and re.fullmatch(r"[a-z0-9_-]+", chosen), "Missing/invalid selected candidate")
        candidates = [c for c in case["candidates"] if c["id"] == chosen]
        trials = [t for t in result["trials"] if t["id"] == chosen]
        require(len(candidates) == len(trials) == 1 and trials[0].get("eligible") is True and trials[0]["goal"].get("achieved") is True,
                "Selected trial is not one uniquely observed completed candidate")
        captures = {"original": read(run / (chosen + "-raw-capture.json")), "replay": read(run / "selected-fixed-input-raw-capture.json")}
        frames = trials[0]["goal"]["frames"]
        normalized = {label: validate_capture(capture, case, frames) for label, capture in captures.items()}
        require(parity.first_difference(normalized["original"], read(run / (chosen + "-inputs.json"))) is None, "Selected input file differs from capture")
        request = read(run / "selected-raw-request.json")
        require(parity.first_difference(to_raw_request(normalized["original"], case["chefs"]), request) is None,
                "Saved raw request is not the exact selected input conversion")
        require(captures["original"]["trace"] == captures["replay"]["trace"], "Original and replay use different trace sources")
        require(captures["original"]["traceByteOffset"] < captures["replay"]["traceByteOffset"], "Replay capture does not follow original")
        source = Path(trace or captures["original"]["trace"]).resolve()
        manifest.update(sourcePath=str(source), recordedTracePath=captures["original"]["trace"], sourceRetainedUnmodified=True,
                        sourceSizeObservedBeforeFreeze=source.stat().st_size,
                        evidence=[receipt(p) for p in sorted(run.glob("*.json*"))])
        prefix = out / "recorded-source-prefix.bin"
        manifest["recordedPrefix"] = copy_range(source, prefix, 0, captures["replay"]["traceByteOffset"])
        save(out / "prefix-manifest.json", manifest)
        bindings = {"original": bind_capture(prefix, captures["original"], candidates[0]["request"]),
                    "replay": bind_capture(prefix, captures["replay"], request)}
        a, b = bindings["original"], bindings["replay"]
        require(a["load"] == b["load"], "Selected original and replay are from different native loads")
        require(a["epoch"] < b["epoch"] and a["terminalOutput"]["endByte"] < b["control"]["startByte"], "Selected replay is not a later authoring branch")
        window = out / "same-load-through-selected-replay.jsonl"
        manifest["comparisonWindow"] = copy_range(prefix, window, a["load"]["startByte"], b["terminalOutput"]["endByte"])
        manifest["comparisonWindow"].update(originalFirstLine=a["load"]["line"], originalLastLine=b["terminalOutput"]["line"])
        manifest["bindings"] = bindings
        # Verify the compressed callbacks too and reject a hidden load reset.
        loads = [where for row, where in parity.records(window) if row.get("kind") == "exchange" and
                 any(m.get("Type") == 1 for m in row.get("output", {}).get("ServerMessages") or [])]
        require(loads == [{"line": 1}], "Comparison window contains an unbound/packed native load")
        save(out / "prefix-manifest.json", manifest)
        start, end = captures["original"]["startExclusive"], captures["original"]["endInclusive"]
        summary.update(review(window, bindings, start, end, out), auditCompleted=True, selected=chosen,
                       originalEpoch=a["epoch"], replayEpoch=b["epoch"], endpointResult=result.get("selectedFixedInputComparison"),
                       scope="All recorded physical fields, four chef states, phases, full inputs and native/auxiliary bytes are strict. Full Rigidbody mass fields are endpoint receipts, not invented per-frame observations.")
    except Exception as error:
        summary.update(error=str(error))
        save(out / "prefix-manifest.json", manifest)
    save(out / "summary.json", summary)
    return summary


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--run", type=Path, required=True)
    parser.add_argument("--out", type=Path, required=True, help="New output directory; existing evidence is never overwritten")
    parser.add_argument("--trace", type=Path, help="Relocated original plain JSONL source, still verified against exact captured exchanges")
    args = parser.parse_args()
    result = audit(args.run, args.out, args.trace)
    print(json.dumps({k: result.get(k) for k in ("auditCompleted", "passed", "error", "firstDivergence", "worldObjectEvents")}))
    return (0 if result["passed"] else 1) if result["auditCompleted"] else 2


if __name__ == "__main__":
    raise SystemExit(main())
