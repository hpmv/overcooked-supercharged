"""Audit saved idle-probe endpoints using the explicit Story11 physics tolerances.

No game connection or state changes. Raw exact-probe evidence remains unchanged.
This compares saved endpoints, not every frame or full gameplay coverage.
"""
import argparse
import json
from pathlib import Path

from framework_story11_compare import compare_boundary


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--probe", type=Path, required=True)
    parser.add_argument("--out", type=Path, required=True)
    args = parser.parse_args()
    if args.out.exists():
        parser.error("Choose a new audit path; original evidence is immutable.")
    summary = json.loads((args.probe / "summary.json").read_text())
    records = json.loads((args.probe / "observations.json").read_text())

    def last(label):
        rows = [row["response"] for row in records if row["label"] == label]
        if not rows:
            raise ValueError("Missing native observation: " + label)
        return rows[-1]

    result = {"passed": False, "scope": "Saved idle checkpoint/continuation endpoints only; no claim of complete rewind coverage or every-frame parity",
              "originalExactProbePassed": summary["passed"], "probe": str(args.probe.resolve()), "replays": []}
    try:
        if summary.get("error") or not summary.get("replays"):
            raise ValueError(summary.get("error") or "No completed native replay to audit.")
        for replay in summary["replays"]:
            i = replay["attempt"]
            baseline = compare_boundary(last("baseline"), last("baseline-native"),
                                        last(f"restored-{i}"), last(f"restored-{i}-settled-native"))
            endpoint = compare_boundary(last("original"), last("original-native"),
                                        last(f"replay-{i}"), last(f"replay-{i}-native"))
            ack = replay["nativeRestore"]
            checks = {"verifiedNativeRestore": ack["verified"] is True and ack["frame"] == summary["checkpointFrame"],
                      "frameCount": replay["observedAdvancingFrames"] == replay["requestedAdvancingFrames"],
                      "baseline": baseline["passed"], "endpoint": endpoint["passed"]}
            result["replays"].append({"attempt": i, "checks": checks, "passed": all(checks.values()),
                                      "baselineComparison": baseline, "endpointComparison": endpoint})
        result["passed"] = all(row["passed"] for row in result["replays"])
    except Exception as error:
        result["error"] = str(error)
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(json.dumps(result, indent=2), encoding="utf-8")
    print(json.dumps({"passed": result["passed"], "error": result.get("error"), "scope": result["scope"],
                      "completedReplayCount": len(result["replays"]), "evidence": str(args.out)}, indent=2))
    if not result["passed"]:
        raise SystemExit(1)


if __name__ == "__main__":
    main()
