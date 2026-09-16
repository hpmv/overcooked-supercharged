"""Bounded file-only screen for exact held FIFO plates at service departure."""
import argparse
import collections
import gzip
import json
import re
from pathlib import Path

from check_bowl_offmix import pin, ingredients


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("trace", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    if args.output.exists():
        raise SystemExit("Output already exists")
    before = pin(args.trace)
    jobs = {}; actions = {}; stages = {}; recipes = []; departures = []; events = collections.Counter()
    frame = -1; previous_line = None; calls = 0
    with gzip.open(args.trace, "rt", encoding="utf-8-sig") as stream:
        for line in stream:
            if line.startswith('{"kind":"call"'):
                match = re.search(r'"gameplayFrame":(-?\d+)', line)
                if match:
                    frame = int(match[1]); previous_line = line; calls += 1
                continue
            row = json.loads(line)
            if row.get("kind") != "event":
                continue
            name = row["name"]; value = row.get("value") or {}; player = value.get("player")
            events[name] += 1
            if name == "plannerInitialized":
                recipes = value["preview"]["recipes"]
            elif name == "actionCreated":
                actions[player] = value
            elif name == "actionStage":
                stages[player] = value
            elif name == "plannerJobStart":
                if player == 2 and value["name"] == "service-return-to-pantry":
                    response = json.loads(previous_line)["response"]; state = response["state"]
                    chefs = {c["playerId"]: c for c in state["chefs"]}; entities = {e["id"]: e for e in state["entities"]}
                    head = state["delivered"]
                    cooks = []
                    for cook in (0, 3):
                        chef = chefs[cook]; entity = entities.get(chef["heldEntityId"])
                        job = jobs.get(cook)
                        cooks.append({"player": cook, "held": chef["heldEntityId"], "position": chef["position"],
                                      "isPlate": entity is not None and "Plate" in entity.get("components", []),
                                      "ordinal": entity.get("observedOrdinal") if entity else None,
                                      "ingredients": ingredients(entity.get("composition")) if entity else [],
                                      "composition": entity.get("composition") if entity else None,
                                      "job": job, "activeAction": actions.get(cook), "stage": stages.get(cook),
                                      "pickupTargetId": chef.get("pickupTargetId"), "placementTargetId": chef.get("placementTargetId")})
                    record = {"frame": frame, "head": head, "recipe": recipes[head], "chefs": cooks,
                              "laterCompletionsWithin120": []}
                    # Save every exact held head assembly departure, without
                    # treating future/empty plates as eligible final applies.
                    if any(c["isPlate"] and c["job"] and c["job"]["name"].startswith(f"assemble-meal-{head + 1}-") for c in cooks):
                        path = args.output.parent / f"v16-service-departure-gf{frame}.json"
                        if not path.exists():
                            path.write_text(json.dumps(response, indent=2), encoding="utf-8")
                        record["snapshot"] = pin(path)
                    departures.append(record)
                jobs[player] = dict(value, completedActions=[])
            elif name == "actionComplete":
                action = value.get("action") or {}; actor = action.get("player")
                if actor in jobs:
                    jobs[actor]["completedActions"].append({"frame": frame, "action": action})
                actions.pop(actor, None); stages.pop(actor, None)
            elif name == "plannerJobComplete":
                job = jobs.get(player)
                for departure in departures:
                    if job and 0 <= frame - departure["frame"] <= 120 and job["name"].startswith(f'assemble-meal-{departure["head"] + 1}-'):
                        response = json.loads(previous_line)["response"]
                        path = args.output.parent / f"v16-service-head-complete-gf{frame}.json"
                        if not path.exists():
                            path.write_text(json.dumps(response, indent=2), encoding="utf-8")
                        departure["laterCompletionsWithin120"].append({"frame": frame, "delayFrames": frame - departure["frame"],
                                                                     "player": player, "job": value, "snapshot": pin(path)})
                jobs.pop(player, None); actions.pop(player, None); stages.pop(player, None)
    if before != pin(args.trace):
        raise SystemExit("Closed trace changed during screen")
    # Deep serialization freezes the final projection, but original job
    # dictionaries above accumulate later action completions intentionally;
    # each completion carries its own frame for reconstructing the boundary.
    result = {"qualification": "Read-only departure screen; later completion is measured evidence, not a counterfactual replay or admission proof",
              "source": before, "throughFrame": frame, "callCount": calls, "departures": departures, "eventCounts": dict(events), "checker": pin(Path(__file__))}
    args.output.write_text(json.dumps(result, indent=2), encoding="utf-8")
    print(json.dumps({"departures": len(departures), "throughFrame": frame,
                      "heldHeadCases": [d["frame"] for d in departures if "snapshot" in d],
                      "headsCompletedWithin120": [{"departure": d["frame"], "completions": d["laterCompletionsWithin120"]} for d in departures if d["laterCompletionsWithin120"]]}))


if __name__ == "__main__":
    main()
