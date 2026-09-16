"""Hash and summarize already closed/read V19 bakery evidence; no native connection."""
from __future__ import annotations
import hashlib
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def identity(relative: str) -> dict:
    path = ROOT / relative
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return {"path": str(path), "bytes": path.stat().st_size, "sha256": digest.hexdigest()}


def main() -> None:
    extraction = json.loads((ROOT / "artifacts/v19-late-bakery-extraction.json").read_text())
    summary = json.loads((ROOT / "artifacts/native-round-v19/summary.json").read_text())
    trial = summary["results"][0]
    assert extraction["sha256"] == trial["traceSha256"]
    assert extraction["completeGzipRead"] and extraction["samples"] == 8704
    assert (trial["score"], trial["delivered"], trial["gameplayFrame"]) == (2396, 22, 16203)
    files = ["artifacts/v19-late-bakery-extraction.json", "artifacts/v19-late-bakery-native.jsonl",
             "artifacts/v19-late-bakery-native-transitions.json", "artifacts/v19-late-bakery-events.json",
             "artifacts/native-round-v19/summary.json", "artifacts/native-round-v19/native-preview-call.json",
             "artifacts/planner-candidate-v19/OvercookedTAS.Controller.dll",
             "artifacts/planner-candidate-v19/source/controller/CarnivalPlanner.cs",
             "artifacts/planner-candidate-v19/source/controller/CarnivalNearReadyDough.cs",
             "controller/CarnivalFifoEmptyBowls.cs", "controller/CarnivalFifoEmptyBowlsTests.cs",
             "artifacts/fifo-empty-bowls-offline-check.json", "scripts/FifoEmptyBowlsCheck/Program.cs",
             "docs/FIFO-EMPTY-BOWLS.md", "scripts/build_fifo_empty_bowls_evidence.py"]
    files += [f"artifacts/v19-bakery-priority-gf{f}.json" for f in (11738, 11739, 12355, 13027, 13165, 13247)]
    report = {
        "format": "oc2-v19-late-bakery-fifo-audit", "version": 1,
        "nativeSource": {"path": extraction["source"], "compressedSha256": extraction["sha256"],
                         "hashEvidence": "Complete gzip read/CRC extraction receipt agrees with closed candidate summary"},
        "completedNativeRound": {k: trial[k] for k in ("score", "delivered", "deductions", "timer", "gameplayFrame", "completedFullRound", "nativeScoreAtLeast5000")},
        "nativeControllerSha256": summary["controllerSha256"],
        "auditedFrames": {"first": 7500, "last": 16203, "samples": 8704, "fullDecodeCrossChecks": 6},
        "currentBatch": {"index": 20, "recipeId": 130976, "bowl": 3, "home": 14,
                         "nativeIngredientFrames": [13699, 13833, 14056], "basket": 5, "basketLoadedFrame": 14705,
                         "nativeCookedFrame": 15305, "deliveredFrame": 15569},
        "laterBatch": {"index": 24, "recipeId": 228996, "bowl": 6, "home": 18,
                       "nativeIngredientFrames": [13247, 13328, 13521], "basket": 8, "basketLoadedFrame": 14209,
                       "nativeCookedFrame": 14809},
        "observedLaterBatchCookedLeadFrames": 496,
        "originalAssignmentEvidence": {"far": "Explicit urgentFifoBakeryReturn at13027 records bowl3/index20",
                                       "near": "Explicit nativePreparedFlavorSupplyStarted at13330 records bowl6/index24",
                                       "limitation": "No original assignment event; exact metadata at pre-issue idle fixtures reconstructed from these events and frozen sticky-assignment source"},
        "newOption": {"property": "FifoEmptyBowls", "cli": "--fifo-empty-bowls", "default": False,
                      "nativeExecutionTested": False, "changes": "Only existing near/far assignment and flavor metadata; no native mutation"},
        "qualifications": ["Native ordering is observed, not a counterfactual savings or score proof",
                           "Offline fixture tests reconstruct planner Work and assignments explicitly; original native observations remain unchanged",
                           "GF11738 active washing rejects; GF12355 lower-left idle andGF13165 post-return idle admit; GF13247 first Flour rejects"],
        "artifacts": [identity(p) for p in files]
    }
    output = ROOT / "artifacts/v19-late-bakery-fifo-proof.json"
    output.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(identity(str(output.relative_to(ROOT))), indent=2))


if __name__ == "__main__":
    main()
