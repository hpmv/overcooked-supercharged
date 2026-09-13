"""Pin the closed S/U plate-search input and focus observations; no native calls."""
import hashlib
import json
from pathlib import Path


def main():
    checks, evidence = [], {}

    def check(value, message):
        if not value:
            raise AssertionError(message)
        checks.append(message)

    def load(path):
        raw = path.read_bytes()
        evidence[str(path)] = {"sha256": hashlib.sha256(raw).hexdigest(), "bytes": len(raw)}
        return json.loads(raw)

    runs = {}
    for run in ("s", "u"):
        folder = Path(f"artifacts/framework-migration/native-{run}/plate-search")
        observations = load(folder / "observations.json")
        rows = {row["label"]: row["response"] for row in observations}
        inputs = load(folder / "chef-103-walk-inputs.json")
        base, end = rows["base"], rows["chef-103-walk-end"]
        base_native, end_native = rows["base-native"]["bridge"], rows["chef-103-walk-native"]["bridge"]
        actions = {row["id"]: row for row in end["typedActions"]["actions"]}
        runs[run] = dict(base=base, end=end, inputs=inputs, actions=actions,
                         focused=[base_native["applicationFocused"], end_native["applicationFocused"]],
                         virtualChecks=[base_native["unfocusedVirtualInputChecks"], end_native["unfocusedVirtualInputChecks"]])
        check(base["frame"] == 31, f"{run}: actual baseline is frame31")
        check([(actions[n]["startFrame"], actions[n]["endFrame"]) for n in ("source-approach", "source-settle")] == [(31, 47), (47, 53)],
              f"{run}: same completed native approach and6-frame settle before pickup")
    a, b = runs["s"], runs["u"]
    for key in ("registry", "actionGraph", "typedActions"):
        check(a["base"][key] == b["base"][key], f"same initial {key}")
    for entity in (10, 32, 38, 103):
        row = lambda r: next(e for e in r["base"]["entities"] if e["id"] == entity)
        check(row(a) == row(b), f"same entire reconstructed initial entity{entity}")
    overlap = []
    for x, y in zip(a["inputs"], b["inputs"]):
        if x["nextFrame"] != y["nextFrame"] or x["inputs"] != y["inputs"]:
            break
        overlap.append(x["nextFrame"])
    check(overlap == list(range(32, 57)), "all four exact emitted pads equal for frames32..56 including the first pickup edge")
    pressed = lambda r: [v["nextFrame"] for v in r["inputs"] if v["inputs"]["103"]["Pickup"]["Down"]]
    check(pressed(a) == [54, 101], "S pickup and placement edges are54 and101")
    check(pressed(b) == list(range(54, 334, 31)), "U retries exact pickup every31frames through333")
    check(a["focused"] == [True, True] and b["focused"] == [False, False], "S actual native focus true; U false at both observed boundaries")
    check(a["actions"]["pickup"]["endFrame"] == 55 and a["actions"]["pickup"]["observedTransfer"]["attachmentAccepted"], "S exact native pickup accepted and released by55")
    check(b["end"]["frame"] == 354 and b["end"]["typedActions"]["error"] == "Action timeout: pickup" and not b["actions"]["pickup"]["observedTransfer"]["attachmentAccepted"], "U correctly reports pending exact transfer until its bounded timeout354")
    entities = {e["id"]: e for e in b["end"]["entities"]}
    check(entities[10]["data"]["attachmentParent"]["path"] == [38], "U plate10 remains attached to its original source38")
    output = {"ok": True, "checks": len(checks), "names": checks, "evidence": evidence,
              "equalInputFrames": overlap, "runs": {k: {"frame": r["end"]["frame"], "focused": r["focused"], "unfocusedVirtualChecks": r["virtualChecks"], "pressFrames": pressed(r)} for k, r in runs.items()},
              "diagnosis": "Installed LogicalButtonBase.Update claims press/release while its inherited CanProcessInput returns Application.isFocused. The old patch covered only PlayerControls.CanButtonBePressed, leaving virtual devices and their native gates subject to this second focus gate. Installed IL and negative controls are separately pinned by FrameworkLogicalFocusCheck.",
              "limits": "S/U differ in process and preceding experiment history; the exact shared inputs plus native source code establish a concrete remaining gate, not isolated native A/B causality. U raw pickup11 exact replay did not achieve a pickup. Frozen V fix requires its own actual unfocused acceptance and strict replay proof."}
    path = Path("artifacts/framework-focus-check/s-u-evidence.json")
    path.write_text(json.dumps(output, indent=2) + "\n")
    print(json.dumps({"ok": True, "checks": len(checks), "path": str(path)}))


if __name__ == "__main__":
    main()
