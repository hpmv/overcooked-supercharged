"""Read-only native checkpoint counter report; never connects to a game."""
import argparse
import hashlib
import json
from pathlib import Path


def read(path, label=None):
    raw = Path(path).read_bytes()
    data = json.loads(raw)
    if label is not None:
        matches = [row["response"] for row in data if row.get("label") == label]
        if len(matches) != 1:
            raise ValueError("Expected one exact observation label")
        data = matches[0]
    bridge = data["bridge"]
    profile = bridge["nativeCheckpoints"]["kitchenCaptureStages"]
    return bridge, profile, {"path": str(Path(path).resolve()), "label": label,
                             "bytes": len(raw), "sha256": hashlib.sha256(raw).hexdigest()}


def report(path, label=None, baseline=None, baseline_label=None):
    bridge, profile, receipt = read(path, label)
    frequency = profile["tickFrequency"]
    if type(frequency) is not int or frequency <= 0:
        raise ValueError("Missing native monotonic tick frequency")
    earlier, baseline_receipt = {}, None
    if baseline:
        before_bridge, before, baseline_receipt = read(baseline, baseline_label)
        if before["tickFrequency"] != frequency or before_bridge.get("root") != bridge.get("root"):
            raise ValueError("Counter deltas require the same process and tick frequency")
        earlier = before["stages"]
    stages = {}
    for name, current in profile["stages"].items():
        previous = earlier.get(name, {"calls": 0, "ticks": 0})
        calls, ticks = current["calls"] - previous["calls"], current["ticks"] - previous["ticks"]
        if any(type(v) is not int or v < 0 for v in (calls, ticks)):
            raise ValueError("Counters regressed; separate native processes cannot be subtracted")
        stages[name] = {"calls": calls, "milliseconds": ticks * 1000 / frequency,
                        "meanMilliseconds": ticks * 1000 / frequency / calls if calls else None,
                        "lifetimeMaximumMilliseconds": current["maximumTicks"] * 1000 / frequency}
    return {"ok": True, "input": receipt, "baseline": baseline_receipt,
            "scope": "Same-process counter deltas" if baseline else "Process-lifetime counters; includes startup and paused callbacks",
            "stages": stages, "profileScope": profile["scope"],
            "membershipDiscovery": bridge["nativeCheckpoints"].get("checkpointComponentDiscovery"),
            "applicationFocused": bridge.get("applicationFocused"),
            "unfocusedVirtualInputChecks": bridge.get("unfocusedVirtualInputChecks"),
            "unfocusedLogicalInputChecks": bridge.get("unfocusedLogicalInputChecks"),
            "limitations": "The caller must select samples from the same process for deltas; a matching root path is not a unique process identity. Nested parent/child totals are not additive. Different stage call counts must retain their own denominators. Maxima are lifetime maxima, even in a delta report. This report measures observed work, not an FPS or correctness claim."}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("input"); parser.add_argument("--label")
    parser.add_argument("--baseline"); parser.add_argument("--baseline-label")
    parser.add_argument("--output", required=True)
    args = parser.parse_args()
    value = report(args.input, args.label, args.baseline, args.baseline_label)
    Path(args.output).write_text(json.dumps(value, indent=2) + "\n")
    print(json.dumps({"ok": value["ok"], "output": args.output, "stages": value["stages"]}))
