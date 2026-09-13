"""Read one fixed compressed prefix; project real plate-first job/lease timing."""
import argparse
import gzip
import hashlib
import json
import re
from pathlib import Path

p = argparse.ArgumentParser(description=__doc__)
p.add_argument("source", type=Path)
p.add_argument("output", type=Path)
a = p.parse_args()
assert not a.output.exists(), "Use a new output for each captured prefix"
before = a.source.stat()
limit = before.st_size
container = hashlib.sha256(); plain = hashlib.sha256()
consumed = 0
last_line = None; last_frame = -1; last_record = -1
events = []; samples = []; snapshots = {}; active = {}; jobs = {}
pattern = re.compile(rb'"gameplayFrame"\s*:\s*(-?\d+)')
directory = a.output.with_suffix("")
directory.mkdir(exist_ok=True)


class Reader:
    def __init__(self, stream): self.stream = stream
    def read(self, size=-1):
        global consumed
        remaining = limit - consumed
        data = self.stream.read(remaining if size < 0 else min(size, remaining))
        consumed += len(data); container.update(data)
        return data


def save_snapshot():
    if last_frame in snapshots or last_line is None: return
    row = json.loads(last_line)
    data = json.dumps(row["response"], ensure_ascii=False, separators=(",", ":")).encode("utf-8")
    path = directory / f"gf{last_frame}.json"
    path.write_bytes(data)
    snapshots[last_frame] = {"frame": last_frame, "record": last_record, "path": str(path),
                             "callLineSha256": hashlib.sha256(last_line).hexdigest(), "responseSha256": hashlib.sha256(data).hexdigest()}


def sample(row):
    state = row["response"]["state"]
    selected = {id for plan in active.values() for id in (plan["board"], plan["plate"], plan["pot"], plan["home"])}
    selected.update((2, 7, 17, 19, 23, 45, 56))
    samples.append({"frame": last_frame, "timer": state["timer"], "delivered": state["delivered"],
                    "jobs": dict(jobs), "activePlates": list(active),
                    "chefs": [{k: c.get(k) for k in ("playerId", "entityId", "position", "heldEntityId", "controlsEnabled", "directlyControlled", "placementTargetId")} for c in state["chefs"]],
                    "entities": [{k: e.get(k) for k in ("id", "active", "observedOrdinal", "position", "attachedEntityId", "cookingProgress", "composition")} for e in state["entities"] if e["id"] in selected]})


truncated = False
with a.source.open("rb") as raw:
    with gzip.GzipFile(fileobj=Reader(raw), mode="rb") as stream:
        record = 0
        try:
            for line in stream:
                record += 1
                if not line.endswith(b"\n"): break
                plain.update(line)
                prefix = line[:90].replace(b" ", b"")
                if b'"kind":"call"' in prefix:
                    m = pattern.search(line)
                    if not m: continue
                    last_frame = int(m[1]); last_line = line; last_record = record
                    if active: sample(json.loads(line))
                elif b'"kind":"event"' in prefix:
                    row = json.loads(line); name = row["name"]; value = row.get("value") or {}
                    if name in ("plannerJobStart", "plannerJobComplete", "plannerJobPaused", "plannerJobResumed", "actionComplete", "pathPlanned", "plannerStatus", "plannerResourceReleased") or name.startswith(("nativePlatedBase", "preService")):
                        events.append({"frame": last_frame, "record": record, "name": name, "value": value, "lineSha256": hashlib.sha256(line).hexdigest()})
                    if name == "plannerJobStart": jobs[value["player"]] = value["name"]
                    elif name == "plannerJobComplete" and jobs.get(value["player"]) == value["name"]: jobs.pop(value["player"])
                    elif name == "nativePlatedBaseAdmitted":
                        active[value["plate"]] = value; save_snapshot()
                        sample(json.loads(last_line))
                        print(name, last_frame, value["plate"], flush=True)
                    elif name.startswith("nativePlatedBase"):
                        save_snapshot()
                        if name == "nativePlatedBaseComplete":
                            active.pop(value["plate"], None)
                            print(name, last_frame, value["plate"], flush=True)
                    elif name == "actionComplete":
                        action = value.get("action") or {}
                        if action.get("type") in ("assemble", "combine") and any(plan["player"] == action.get("player") for plan in active.values()): save_snapshot()
        except EOFError:
            truncated = True
after = a.source.stat()
assert consumed == limit and after.st_size >= limit and before.st_ino == after.st_ino, "Fixed source prefix changed/truncated"
result = {"format": "native-plated-base-consumed-prefix-v1", "source": str(a.source.resolve()),
          "compressedPrefixBytes": consumed, "compressedPrefixSha256": container.hexdigest(),
          "completeJsonlPrefixSha256": plain.hexdigest(), "completeRecords": record,
          "lastFrame": last_frame, "truncatedAtFixedCompressedLimit": truncated, "originalSourceStillGrowing": after.st_size > limit,
          "events": events, "samples": samples, "snapshots": list(snapshots.values()), "activeAtPrefixEnd": list(active.values()),
          "qualification": "Fixed read-only prefix of an active recording, not a closed trace or completed-run proof. Only complete JSONL records are projected. Native job timing and retained exact responses do not establish counterfactual scheduling gains."}
with gzip.open(a.output, "wt", encoding="utf-8") as stream: json.dump(result, stream, separators=(",", ":"))
print("prefix", consumed, "lastFrame", last_frame, "samples", len(samples), "snapshots", len(snapshots), flush=True)
