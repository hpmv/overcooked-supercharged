"""Review exact completed refill boundaries under the unchanged delay budgets."""
import hashlib
import json
from pathlib import Path


def sha(path): return hashlib.sha256(path.read_bytes()).hexdigest()
def ingredients(node):
    if not node: return []
    return ([node["id"]] if node.get("type") == "IngredientAssembledNode" else []) + [i for child in node.get("children", []) for i in ingredients(child)]
def tip(fraction): return 8 if fraction > .66 else 5 if fraction > .33 else 3 if fraction > 0 else 0


report = {"format": "native-second-preservice-refill-review-v1", "versions": [],
          "qualification": "Read-only admission rejection evidence. Native contents/attachments and exact completed first-job events are preserved. No scheduler policy or game state was changed; a second refill was not executed."}
for version in (16, 17):
    directory = Path(f"artifacts/v{version}-preservice-boundaries")
    manifest_path = directory / "manifest.json"
    manifest = json.loads(manifest_path.read_text())
    completions = [e for e in manifest["events"] if e["name"] == "preServiceStockComplete"]
    rows = []
    for event in completions:
        value = event["value"]; frame = event["frame"]
        receipt = next(s for s in manifest["snapshots"] if s["frame"] == frame)
        path = Path(receipt["path"])
        assert sha(path) == receipt["responseSha256"]
        state = json.loads(path.read_text())["state"]
        entities = {e["id"]: e for e in state["entities"] if e["active"]}
        elapsed = frame - value["readyFrame"]
        order = min(state["orders"], key=lambda o: o["id"])
        budget_remaining = value["maximumReadyDelayFrames"] - elapsed
        head = value["reservedHeadPlate"]
        head_parent = next(e["id"] for e in entities.values() if e.get("attachedEntityId") == head)
        pots = []
        for pot, home in ((2, 17), (7, 19)):
            e = entities[pot]
            pots.append({"id": pot, "observedOrdinal": e["observedOrdinal"], "originalHome": home,
                         "parent": next((p["id"] for p in entities.values() if p.get("attachedEntityId") == pot), None),
                         "originalHomeAttached": entities[home].get("attachedEntityId"),
                         "cookingProgress": e["cookingProgress"], "composition": e["composition"],
                         "ingredientIds": ingredients(e["composition"])})
        handoff = entities[45].get("attachedEntityId", 0)
        native_handoff = {"counter": 45, "attachedEntityId": handoff,
                          "composition": entities[handoff].get("composition") if handoff else None}
        if frame == 1772:
            assert elapsed == 164 and budget_remaining < 120
            rejection = "Only76 of240 ready-head frames remain; a second120-frame pot budget fails."
        elif frame == 5377:
            assert elapsed == 122 and budget_remaining < 120
            assert all(p["ingredientIds"] == [284626] for p in pots)
            rejection = "Only118 of240 ready-head frames remain; both original pots also already contain native sausage."
        elif frame == 6354:
            assert elapsed == 61 and budget_remaining >= 120
            assert pots[0]["ingredientIds"] == [] and pots[1]["ingredientIds"] == [284626]
            assert pots[1]["parent"] == 33 and handoff == 276
            assert ingredients(native_handoff["composition"]) == [284626] and value["destination"] == 2
            rejection = "Pot2 is the exact target of the just-completed addressed raw276 handoff on45; pot7 is nonempty Cooked on33. No second unaddressed empty pot exists."
        else: raise AssertionError("Unreviewed completion boundary")
        original_tip = tip(order["remaining"] / order["lifetime"])
        projected_tip = tip((order["remaining"] - 2 - 10 - 1) / order["lifetime"])
        rows.append({"frame": frame, "event": event, "receipt": receipt, "elapsedReadyHeadFrames": elapsed,
                     "remainingReadyHeadFrames": budget_remaining, "minimumSecondPotBudgetFrames": 120,
                     "secondPotBudgetFits": budget_remaining >= 120,
                     "nativeHead": {k: order[k] for k in ("id", "recipeId", "recipe", "remaining", "lifetime")},
                     "nativeHeadPlate": head, "nativeHeadPlateParent": head_parent,
                     "nativeTipBand": original_tip, "tipBandAfterSecondPotAnd11SecondAllowance": projected_tip,
                     "tipGuardPasses": projected_tip == original_tip, "pots": pots, "nativeHandoff": native_handoff,
                     "rejection": rejection})
    report["versions"].append({"version": version, "sourceContainer": manifest["source"],
                                "readContainerSha256": manifest["readContainerSha256"],
                                "originalGzipSha256": manifest["originalGzipSha256"],
                                "originalRawJsonlSha256": manifest["originalRawJsonlSha256"],
                                "manifestSha256": sha(manifest_path), "completionCount": len(rows), "completions": rows})
assert len(report["versions"][0]["completions"]) == 3
assert report["versions"][1]["completionCount"] == 0
Path("artifacts/second-preservice-refill-review.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
print("V16: all3 completions reject; V17: no completed pre-service jobs.")
