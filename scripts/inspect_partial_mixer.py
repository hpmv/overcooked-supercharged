"""File-only, bounded projection of native partial-mixer scheduling evidence."""
import argparse
import gzip
import json
import re
from pathlib import Path

p = argparse.ArgumentParser(description=__doc__)
p.add_argument("trace", type=Path)
p.add_argument("output", type=Path)
p.add_argument("--from-frame", type=int, default=6500)
p.add_argument("--to-frame", type=int, default=9930)
p.add_argument("--capture", type=int, nargs="*", default=[9469, 9929, 9930])
args = p.parse_args()
events, samples, frames, last, captures = [], [], 0, -1, []
gf_pattern = re.compile(r'"gameplayFrame"\s*:\s*(-?\d+)')

def ingredients(node):
    if not node:
        return []
    return sorted(([node["id"]] if node.get("type") == "IngredientAssembledNode" else []) +
                  sum((ingredients(c) for c in node.get("children", [])), []))

with gzip.open(args.trace, "rt", encoding="utf-8-sig") as stream:
    for line in stream:
        head = line[:100].replace(" ", "")
        if '"kind":"event"' in head:
            row = json.loads(line)
            name = row.get("name", "")
            if not name.startswith(("navigation", "actionFrame", "dash")):
                value = {k: v for k, v in (row.get("value") or {}).items() if k not in ("state", "snapshot")}
                if name == "plannerInitialized":
                    value["preview"] = {"recipes": value.get("preview", {}).get("recipes", [])[:50]}
                events.append({"gf": last, "name": name, "value": value})
        elif '"kind":"call"' in head:
            match = gf_pattern.search(line)
            if not match:
                continue
            last = int(match.group(1))
            if last > args.to_frame:
                break
            frames += 1
            capture = last in args.capture
            if not capture and (last < args.from_frame or last % 10):
                continue
            row = json.loads(line)
            state = row["response"]["state"]
            if capture:
                target = args.output.with_name(args.output.stem + "-gf" + str(last) + ".json")
                target.write_text(json.dumps(row["response"], separators=(",", ":")), encoding="utf-8")
                if str(target) not in captures:
                    captures.append(str(target))
            entities = [e for e in state.get("entities", []) if e.get("active")]
            selected = [e for e in entities if any(c in e.get("components", []) for c in ("MixableContainer", "CookableContainer", "Workstation", "WorkableItem"))]
            bowls = [{"id": e["id"], "name": e["name"], "ordinal": e.get("observedOrdinal"),
                      "ingredients": ingredients(e.get("composition")), "mixing": e.get("mixingProgress"),
                      "cooking": e.get("cookingProgress"), "position": e["position"],
                      "attached": e.get("attachedEntityId"), "work": e.get("workProgress"),
                      "parents": [h["id"] for h in entities if h.get("attachedEntityId") == e["id"]]}
                     for e in selected]
            samples.append({"gf": last, "score": state["score"], "delivered": state["delivered"], "timer": state["timer"],
                            "chefs": [{k: c.get(k) for k in ("playerId", "position", "heldEntityId", "interactingEntityId", "serverInteractionId", "controlsEnabled")}
                                      for c in state["chefs"]], "entities": bowls})
args.output.write_text(json.dumps({"source": str(args.trace.resolve()), "scope": "Read-only completed prefix; samples every 10 native frames plus exact requested captures",
                                  "throughFrame": last, "callFrames": frames, "captures": captures, "events": events, "samples": samples}, indent=2), encoding="utf-8")
print(json.dumps({"throughFrame": last, "events": len(events), "samples": len(samples), "captures": captures}))
