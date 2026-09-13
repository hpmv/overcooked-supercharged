#!/usr/bin/env python3
"""Read-only structural audit of original native request traces, including failed prefixes.

This checks recorded protocol boundaries, not the absence of hidden game patches.
The separate frozen .NET native-score validator checks the observed score ledger.
"""
import argparse
from collections import Counter
import gzip
import hashlib
import json
from pathlib import Path
import re


def file_hash(path):
    digest = hashlib.sha256()
    with open(path, "rb") as source:
        for block in iter(lambda: source.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def canonical(value):
    return (json.dumps(value, sort_keys=True, separators=(",", ":"), ensure_ascii=False, allow_nan=False) + "\n").encode()


def require(condition, message):
    if not condition:
        raise ValueError(message)


def audit(path, out):
    path, out = Path(path).resolve(), Path(out).resolve()
    before = path.stat()
    decoder = json.JSONDecoder()
    commands, steps = Counter(), Counter()
    digest = hashlib.sha256()
    records = calls = stepped = 0
    prior_gf = first_gf = None
    first = last = preview = None
    failures, endings = [], {}
    gf_pattern = re.compile(rb'"gameplayFrame":(-?\d+)')
    for_line = gzip.open if path.suffix == ".gz" else open
    with for_line(path, "rb") as source:
        for line_number, raw in enumerate(source, 1):
            if not raw.strip():
                continue
            records += 1
            if not raw.startswith(b'{"kind":"call"'):
                if raw.startswith(b'{"kind":"event","name":"plannerFailure"'):
                    event = json.loads(raw)
                    failures.append({"gameplayFrame": event["value"].get("planner", {}).get("gameplayFrame"),
                                     "error": event["value"].get("error")})
                continue
            # Decode bounded envelope objects; do not deserialize every entity on every frame.
            head = raw[:16000].decode("utf-8")
            index = int(re.search(r'"index":(\d+)', head).group(1))
            require(index == calls, f"Call-index gap at line {line_number}")
            request, _ = decoder.raw_decode(head, head.index('"request":') + len('"request":'))
            require(request.get("version") == 1, "Unexpected request version")
            command = request.get("command")
            require(command in {"restart", "load", "inspect", "preview", "step"}, "Unapproved trace command: " + str(command))
            require('"ok":true' in head[head.index('"response":'):head.index('"state":')], "Failed native response")
            match = gf_pattern.search(raw[:16000])
            require(match is not None, "Missing gameplay frame")
            gf = int(match.group(1))
            count = request.get("steps", 1) if command == "step" else 0
            if command == "step":
                require(type(count) is int and count >= 1, "Invalid step count")
                require(set(request) <= {"version", "command", "steps", "inputs"}, "Unexpected step request field")
                pads = request.get("inputs")
                require(isinstance(pads, list) and sorted(p.get("player") for p in pads) == [0, 1, 2, 3], "Missing explicit four-player inputs")
                for pad in pads:
                    require(set(pad) <= {"player", "x", "y", "pickup", "use", "dash"}, "Unexpected pad field")
                    require(all(type(pad.get(k)) is bool for k in ("pickup", "use", "dash")), "Nonboolean native input")
                    require(all(type(pad.get(k)) in (int, float) and -1 <= pad[k] <= 1 for k in ("x", "y")), "Invalid native axis")
                steps[count] += 1
            elif command in {"load", "restart"}:
                require(calls == 0 and gf == 0, "In-body load/reset")
            else:
                require(set(request) <= ({"version", "command", "seed", "count"} if command == "preview" else {"version", "command"}), "Unexpected observation field")
            if prior_gf is not None:
                require(gf == prior_gf + count, f"Unrecorded/extra gameplay advance at call {calls}: {prior_gf}->{gf}, request {count}")
            if first is None:
                first = json.loads(raw)["response"]
                first_gf = gf
            if command == "preview":
                preview = json.loads(raw)
            state_head = head[head.index('"state":'):]
            for field, value in [("timer", "0"), ("serverRoundActive", "false"), ("clientRoundActive", "false")]:
                if field not in endings and re.search(r'"' + field + '":' + value + r'(?=[,}])', state_head):
                    endings[field] = gf
            if "gameState" not in endings and '"gameState":"RunLevelOutro"' in state_head:
                endings["gameState"] = gf
            prior_gf = gf
            last = raw
            calls += 1
            stepped += count
            commands[command] += 1
            digest.update(canonical(request))
    require(first is not None and last is not None, "Empty trace")
    require((before.st_size, before.st_mtime_ns) == (path.stat().st_size, path.stat().st_mtime_ns), "Trace changed during audit")
    final = json.loads(last)["response"]
    out.parent.mkdir(parents=True, exist_ok=True)
    first_path = Path(str(out) + ".first.json")
    final_path = Path(str(out) + ".last.json")
    first_path.write_text(json.dumps(first), encoding="utf-8")
    final_path.write_text(json.dumps(final), encoding="utf-8")
    if preview is not None:
        Path(str(out) + ".preview.json").write_text(json.dumps(preview), encoding="utf-8")
    fields = ["gameplayFrame", "timer", "clientTime", "levelClientTimeZero", "score", "baseScore", "tips", "deductions", "delivered", "serverRoundActive", "clientRoundActive", "gameState"]
    report = {"trace": str(path), "traceSha256": file_hash(path), "records": records, "requests": calls,
              "commands": dict(commands), "stepSizes": dict(steps), "totalSteppedFrames": stepped,
              "firstGameplayFrame": first_gf, "lastGameplayFrame": prior_gf,
              "canonicalRequestsSha256": digest.hexdigest(), "canonicalEncoding": "sorted compact UTF-8 JSON + LF",
              "requestAndFrameContinuity": True, "explicitFourPlayerStepInputs": True,
              "noProtocolStateCorrections": True, "limitations": "Protocol audit does not independently attest loaded native/plugin code; use pinned instrumentation and process receipts.",
              "plannerFailures": failures, "firstObservedEndFields": endings,
              "initial": {k: first["state"].get(k) for k in fields},
              "final": {k: final["state"].get(k) for k in fields},
              "firstResponse": str(first_path), "lastResponse": str(final_path)}
    out.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    return report


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("trace")
    parser.add_argument("--out", required=True)
    args = parser.parse_args()
    print(json.dumps(audit(args.trace, args.out), indent=2))
