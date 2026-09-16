"""Offline, strict advancing-frame parity for framework exchange recordings.

An epoch starts after each emitted Warp directive; epoch zero precedes any warp.
The explicit interval is (start,end]. Paused callbacks reconstruct the boundary
but do not invent advancing frames. No sockets, native calls, or normalisation of
positions, quaternion signs, auxiliary floats, or native event bytes are used.
"""
from __future__ import annotations

import argparse
import base64
import copy
import gzip
import hashlib
import json
import math
from pathlib import Path


class TraceError(ValueError):
    pass


def loads(value):
    def integer(text):
        return -0.0 if text == "-0" else int(text)
    def invalid(text):
        raise TraceError("Nonfinite JSON number: " + text)
    def real(text):
        value = float(text)
        if not math.isfinite(value):
            return invalid(text)
        return value
    return json.loads(value, parse_int=integer, parse_float=real, parse_constant=invalid)


def digest(value):
    return hashlib.sha256(json.dumps(value, sort_keys=True, separators=(",", ":"), allow_nan=False).encode()).hexdigest()


def without_metadata(value):
    if isinstance(value, dict):
        return {k: without_metadata(v) for k, v in value.items() if k != "__isset"}
    if isinstance(value, list):
        return [without_metadata(v) for v in value]
    return value


def first_difference(a, b, path="$"):
    if isinstance(a, dict) and isinstance(b, dict):
        for key in sorted(a.keys() | b.keys()):
            child = path + "/" + str(key)
            if key not in a or key not in b:
                return {"field": child, "originalPresent": key in a, "replayPresent": key in b,
                        "original": a.get(key), "replay": b.get(key)}
            difference = first_difference(a[key], b[key], child)
            if difference:
                return difference
        return None
    if isinstance(a, list) and isinstance(b, list):
        for index, (left, right) in enumerate(zip(a, b)):
            difference = first_difference(left, right, path + f"/{index}")
            if difference:
                return difference
        if len(a) == len(b):
            return None
        return {"field": path + "/length", "original": len(a), "replay": len(b)}
    numeric = isinstance(a, (int, float)) and not isinstance(a, bool) and isinstance(b, (int, float)) and not isinstance(b, bool)
    if numeric:
        equal = type(a) is type(b) and a == b
        if equal and a == 0:
            equal = math.copysign(1, a) == math.copysign(1, b)
    else:
        equal = type(a) is type(b) and a == b
    if not equal:
        return {"field": path, "original": a, "replay": b}
    return None


def records(path):
    """Expand v1/plain or gzip and verified v6 Brotli paused blocks."""
    opener = gzip.open if str(path).lower().endswith(".gz") else open
    with opener(path, "rb") as stream:
        for line_number, raw in enumerate(stream, 1):
            if len(raw) > 4 * 1024 * 1024:
                raise TraceError("Oversized trace line")
            if not raw.endswith(b"\n"):
                raise TraceError("Trace has an incomplete final line; close/flush it before comparison")
            if not raw.strip():
                continue
            row = loads(raw.decode("utf-8-sig"))
            where = {"line": line_number}
            if row.get("kind") != "paused-exchanges":
                yield row, where
                continue
            if row.get("version") != 1 or row.get("encoding") != "brotli-jsonl":
                raise TraceError("Unsupported paused-block encoding")
            count, length = row.get("count"), row.get("uncompressedBytes")
            if not isinstance(count, int) or not 1 <= count <= 256 or not isinstance(length, int) or not 1 <= length <= 1048576:
                raise TraceError("Paused block exceeds native TraceStore bounds")
            packed = base64.b64decode(row["data"], validate=True)
            if len(packed) > 1048576 + 65536:
                raise TraceError("Oversized compressed block")
            import brotli
            expanded = brotli.decompress(packed)
            if len(expanded) != length or hashlib.sha256(expanded).hexdigest() != row.get("sha256"):
                raise TraceError("Paused block byte length/hash mismatch")
            lines = expanded.split(b"\n")
            if len(lines) != count + 1 or lines[-1] != b"":
                raise TraceError("Paused block callback count mismatch")
            for index, line in enumerate(lines[:-1], 1):
                value = loads(line.decode("utf8"))
                output, inputs = value.get("output", {}), value.get("input", {})
                if value.get("kind") != "exchange" or output.get("LastFramePaused") is not True or output.get("NextFramePaused") is not True:
                    raise TraceError("Paused block contains advancing/transition data")
                if inputs.get("Warp") is not None or inputs.get("RequestResume") or inputs.get("RequestPause"):
                    raise TraceError("Paused block contains a control transition")
                yield value, {"line": line_number, "blockRow": index}


def bits(data, offset, count):
    if offset + count > len(data) * 8:
        raise TraceError("Truncated native message header")
    return (int.from_bytes(data, "big") >> (len(data) * 8 - offset - count)) & ((1 << count) - 1)


def message_info(message, registry):
    kind = message.get("Type")
    data = base64.b64decode(message.get("Message", ""), validate=True)
    result = {"type": kind, "bytes": message.get("Message")}
    if kind in {3, 4, 6, 43, 44}:
        entity = bits(data, 0, 10)
        result["entityId"] = entity
        if kind == 4:
            component = bits(data, 10, 4)
            result["componentIndex"] = component
            types = registry.get(str(entity), {}).get("SyncEntityTypes") or []
            result["nativeComponentType"] = types[component] if component < len(types) else None
        if kind == 43:
            result["auxiliaryType"] = bits(data, 10, 8)
    return result


def selected_epochs(path, epochs, start, end, after_line=0, require_registry_membership=True):
    if not 0 <= start < end or end - start > 36000 or any(e < 0 for e in epochs):
        raise TraceError("Use nonnegative epochs and a 1..36000 frame interval")
    selected = {e: {"epoch": e, "frames": {}, "inputs": {}, "boundary": None} for e in epochs}
    epoch, physics, chefs, registry = 0, {}, {}, {}
    retained_bytes = 0
    pending_warp = False
    # Raw observations intentionally do not guess transformed/default positions.
    # The live registry is used only for identity and message-header annotations.
    for row, location in records(path):
        if location["line"] < after_line:
            continue
        if row.get("kind") != "exchange":
            continue
        output, inputs = row.get("output", {}), row.get("input") or {}
        if pending_warp:
            epoch += 1
            pending_warp = False
            physics, chefs = {}, {}
        if epoch > max(epochs):
            break
        frame = output.get("FrameNumber")
        if not isinstance(frame, int):
            raise TraceError("Native output frame is absent")
        for item in output.get("EntityRegistry") or []:
            key = str(item["EntityId"])
            registry[key] = without_metadata(item)
        infos = [message_info(m, registry) for m in output.get("ServerMessages") or []]
        for info in infos:
            if info["type"] in {6, 44}:
                key = str(info["entityId"])
                registry.pop(key, None); physics.pop(key, None); chefs.pop(key, None)
            if info["type"] == 38:
                raise TraceError("DestroyEntities bulk lifecycle requires a decoder before this comparison is supported")
        for key, item in (output.get("Items") or {}).items():
            old = physics.setdefault(key, {})
            for name, value in item.items():
                if name != "__isset" and item.get("__isset", {}).get(name[0].lower()+name[1:], value is not None):
                    old[name] = without_metadata(value)
        for key, chef in (output.get("Chefs") or {}).items():
            chefs[key] = without_metadata(chef)
        target = selected.get(epoch)
        if target is not None:
            if output.get("InvalidStateReason", ""):
                raise TraceError("Native invalid-state receipt in selected epoch: " + output["InvalidStateReason"])
            if frame == start and not target["frames"]:
                target["boundary"] = {"physics": copy.deepcopy(physics), "chefs": copy.deepcopy(chefs),
                                      "registeredIds": sorted(registry), "location": location}
            advancing = output.get("LastFramePaused") is False
            if start < frame <= end and advancing:
                if frame in target["frames"]:
                    raise TraceError(f"Duplicate advancing output frame {frame} in epoch {epoch}")
                if len(output.get("Chefs") or {}) != 4:
                    raise TraceError("Advancing frame lacks a fresh complete four-chef observation")
                retained_bytes += len(json.dumps(physics)) + len(json.dumps(chefs)) + len(json.dumps(output.get("ServerMessages") or []))
                if retained_bytes > 128 * 1024 * 1024:
                    raise TraceError("Comparison exceeds128MiB retained native observations; select smaller intervals")
                target["frames"][frame] = {
                    "location": location, "physics": copy.deepcopy(physics), "chefs": copy.deepcopy(chefs),
                    "phase": {k: output.get(k) for k in ("PhysicsFramesElapsed", "FramesSinceLastNoPhysicsFrame", "LastFramePaused", "NextFramePaused")},
                    "messages": copy.deepcopy(output.get("ServerMessages") or []),
                    "nativeMessages": [i for i in infos if i["type"] < 42],
                    "auxiliaryMessages": [i for i in infos if i["type"] >= 42],
                }
            next_frame = inputs.get("NextFrame")
            if isinstance(next_frame, int) and start < next_frame <= end and inputs.get("Input") is not None:
                if inputs.get("Warp") is not None:
                    raise TraceError("Authoring warp mixed with advancing input")
                if next_frame in target["inputs"]:
                    raise TraceError(f"Duplicate advancing input frame {next_frame} in epoch {epoch}")
                target["inputs"][next_frame] = {"location": location, "value": copy.deepcopy(inputs)}
        if inputs.get("Warp") is not None:
            pending_warp = True
    wanted = set(range(start + 1, end + 1))
    for value in selected.values():
        if value["boundary"] is None:
            raise TraceError(f"No observed frame {start} boundary for epoch {value['epoch']}")
        for key in ("frames", "inputs"):
            if set(value[key]) != wanted:
                raise TraceError(f"Epoch {value['epoch']} {key} is not contiguous; missing={sorted(wanted-set(value[key]))[:8]}")
        if len(value["boundary"]["chefs"]) != 4:
            raise TraceError("Boundary lacks all four observed native chef states")
        required = {"Pos", "Rotation", "Velocity", "AngularVelocity"}
        if any(not required.issubset(p) for p in value["boundary"]["physics"].values()) or not value["boundary"]["physics"]:
            raise TraceError("Boundary lacks complete native physical observations")
        if require_registry_membership and set(value["boundary"]["physics"]) != set(value["boundary"]["registeredIds"]):
            raise TraceError("Boundary physical observation inventory differs from the actual live registry")
    return selected


def compare(original, replay, start, end):
    names = ("physics", "chefs", "phase", "messages", "nativeMessages", "auxiliaryMessages", "inputs")
    checks = {name: {"equal": True, "firstDivergence": None} for name in names}
    boundary = None
    for name in ("physics", "chefs"):
        difference = first_difference(original["boundary"][name], replay["boundary"][name], "$boundary/"+name)
        if difference and boundary is None:
            boundary = dict(difference, frame=start, originalLocation=original["boundary"]["location"], replayLocation=replay["boundary"]["location"])
    for frame in range(start+1, end+1):
        left, right = original["frames"][frame], replay["frames"][frame]
        for name in names:
            a, b = (original["inputs"][frame]["value"], replay["inputs"][frame]["value"]) if name == "inputs" else (left[name], right[name])
            difference = first_difference(a, b, "$frame/"+name)
            if difference and checks[name]["equal"]:
                checks[name] = {"equal": False, "firstDivergence": dict(difference, frame=frame,
                    originalLocation=original["inputs"][frame]["location"] if name=="inputs" else left["location"],
                    replayLocation=replay["inputs"][frame]["location"] if name=="inputs" else right["location"])}
        
    failures = ([boundary] if boundary else []) + [c["firstDivergence"] for c in checks.values() if not c["equal"]]
    report = {"passed": not failures, "startExclusive": start, "endInclusive": end, "advancingFrames": end-start,
        "originalEpoch": original["epoch"], "replayEpoch": replay["epoch"], "boundaryEqual": boundary is None,
        "boundaryFirstDivergence": boundary, "checks": checks,
        "firstDivergence": min(failures, key=lambda f:f["frame"]) if failures else None,
        "decodedProjection": {"nativeEventSequenceEqual": checks["nativeMessages"]["equal"],
            "auxiliarySequenceEqual": checks["auxiliaryMessages"]["equal"],
            "scope": "Header-decoded native versus instrumentation messages, preserving exact bytes/order. Not a full semantic food/order decoder and never overrides raw failure."},
        "hashes": {name: {"original": digest([original["inputs"][f]["value"] if name=="inputs" else original["frames"][f][name] for f in range(start+1,end+1)]),
                          "replay": digest([replay["inputs"][f]["value"] if name=="inputs" else replay["frames"][f][name] for f in range(start+1,end+1)])} for name in names}}
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--trace", type=Path, required=True)
    parser.add_argument("--original-epoch", type=int, default=0)
    parser.add_argument("--replay-epoch", type=int, default=1)
    parser.add_argument("--start", type=int, required=True)
    parser.add_argument("--end", type=int, required=True)
    parser.add_argument("--after-line", type=int, default=0,
                        help="Ignore physical trace rows before this one-based line; epoch numbering restarts at zero.")
    parser.add_argument("--allow-truncated-registry-prefix", action="store_true",
                        help=("Compare native body/chef/input/message frames after --after-line even when the cropped "
                              "window no longer contains the registry rows needed for a boundary membership proof. "
                              "The default strict registry proof is unchanged."))
    parser.add_argument("--out", type=Path, required=True)
    args = parser.parse_args()
    result = {"passed": False, "classification": "offline exact native advancing-frame trace comparison", "trace": str(args.trace.resolve())}
    try:
        if args.original_epoch == args.replay_epoch:
            raise TraceError("Select distinct epochs; self-comparison is not replay evidence")
        before = args.trace.stat()
        if args.after_line < 0:
            raise TraceError("Use a nonnegative trace start line")
        if args.allow_truncated_registry_prefix and args.after_line <= 0:
            raise TraceError("A truncated registry prefix requires a positive --after-line boundary")
        epochs = selected_epochs(args.trace, {args.original_epoch,args.replay_epoch}, args.start,args.end,
                                  args.after_line, not args.allow_truncated_registry_prefix)
        result.update(compare(epochs[args.original_epoch],epochs[args.replay_epoch],args.start,args.end))
        result["traceStartLine"] = args.after_line
        result["registryBoundaryMembershipChecked"] = not args.allow_truncated_registry_prefix
        with args.trace.open("rb") as stream:
            result["traceSha256"] = hashlib.file_digest(stream,"sha256").hexdigest()
        after = args.trace.stat()
        if before.st_size != after.st_size or before.st_mtime_ns != after.st_mtime_ns:
            raise TraceError("Trace changed during comparison; close it first")
    except Exception as error:
        result.update(passed=False,error=str(error))
    args.out.parent.mkdir(parents=True,exist_ok=True)
    args.out.write_text(json.dumps(result,indent=2,allow_nan=False)+"\n",encoding="utf8")
    print(json.dumps({"passed":result["passed"],"firstDivergence":result.get("firstDivergence"),"error":result.get("error"),"out":str(args.out)}))
    return 0 if result["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
