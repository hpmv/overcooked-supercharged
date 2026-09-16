"""Screen closed, already-hashed V16/V17 projections for UL batch-wait witnesses.

This does not predict a new policy's execution or recompute native food. It uses
completed ordinary assembly callbacks to locate useful full-native captures.
"""
import gzip
import hashlib
import json
import re
from pathlib import Path


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def screen(version):
    projection = Path(f"artifacts/v{version}-central-work-projection.json.gz")
    analysis = Path(f"artifacts/v{version}-central-work-analysis.json")
    with gzip.open(projection, "rt", encoding="utf-8") as stream:
        p = json.load(stream)
    a = json.loads(analysis.read_text(encoding="utf-8"))
    assert p["compressedSha256"] == a["sourceSha256"]
    jobs = a["jobs"]
    boundaries = {x["frame"]: x for x in p["boundaries"]}
    ready = {}
    for job in jobs:
        match = re.match(r"assemble-meal-(\d+)-", job["name"])
        match = match or re.fullmatch(r"cooperative-sauce-(\d+)-stage-complete-meal", job["name"])
        match = match or re.fullmatch(r"recover-washer-plated-meal-(\d+)", job["name"])
        if match and job["end"] is not None:
            ready[int(match[1]) - 1] = job
    visits = []
    boards = [j for j in jobs if j["player"] == 2 and j["name"] == "board-empty-for-service-wave"]
    for board in boards:
        boundary = boundaries[board["start"]]
        head = boundary["delivered"]
        returns = [j for j in jobs if j["player"] == 2 and j["name"] == "service-return-to-pantry" and j["start"] > board["start"]]
        returned = min(returns, key=lambda j: j["start"]) if returns else None
        stop = returned["start"] if returned else p["lastFrame"]
        collected = [j for j in jobs if j["player"] == 2 and j["name"].startswith("collect-fifo-") and board["start"] <= j["start"] < stop]
        second = ready.get(head + 1)
        delta = second["end"] - board["start"] if second else None
        item = {
            "boardFrame": board["start"], "boardEnd": board["end"],
            "headIndex": head, "headOrderNumber": head + 1, "nativeTimer": boundary["timer"],
            "returnStart": returned["start"] if returned else None,
            "returnEnd": returned["end"] if returned else None,
            "collectedOrderNumbers": [int(j["name"].rsplit("-", 1)[1]) + 1 for j in collected],
            "nextAssembly": {k: second[k] for k in ("name", "player", "start", "end", "resources")} if second else None,
            "nextPlateReadyMinusBoardFrames": delta,
            "qualification": "Existing ordinary callback timing; not a counterfactual readiness or delivery guarantee.",
        }
        if delta is None:
            item["screen"] = "next-plate-not-completed-in-recording"
        elif delta <= 0:
            item["screen"] = "two-consecutive-plates-already-ready"
        elif head + 2 in item["collectedOrderNumbers"]:
            item["screen"] = "next-plate-already-collected-on-this-native-visit"
        elif delta <= 480:
            item["screen"] = "candidate-for-native-identity-capacity-tip-and-dependency-review"
        else:
            item["screen"] = "recorded-next-plate-outside-eight-second-wait"
        visits.append(item)
    return {
        "version": version, "source": p["source"], "sourceCompressedSha256": p["compressedSha256"],
        "projection": {"path": str(projection), "sha256": sha(projection)},
        "analysis": {"path": str(analysis), "sha256": sha(analysis)},
        "lastFrame": p["lastFrame"], "visits": visits,
    }


if __name__ == "__main__":
    report = {
        "format": "closed-pantry-batch-wait-screen-v1",
        "qualification": "Read-only screening of prior native callbacks. No new game requests, source policy changes, modeled saved time, or score claims. Full native snapshots are required for tip windows, exact plate capacity and resource ownership.",
        "versions": [screen(v) for v in (16, 17)],
    }
    path = Path("artifacts/pantry-batch-wait-screen.json")
    path.write_text(json.dumps(report, indent=2), encoding="utf-8")
    for version in report["versions"]:
        print("V", version["version"], [(v["boardFrame"], v["headOrderNumber"], v["nextPlateReadyMinusBoardFrames"], v["screen"]) for v in version["visits"]])
    print(path)
