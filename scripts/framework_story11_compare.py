"""Explicit physical-only tolerances for Story1-1 authoring comparisons.

No state writes. Logical identities/attachments, food, recipes, progress, ledger,
orders and clocks remain exact. Every raw difference and its individual decision
is retained. Unknown fields, modes, sleep/kinematic flags and schema changes are
never accepted through a generic epsilon.
"""
from __future__ import annotations

import math
from compare_framework_frames import first_difference
from framework_native_search import gameplay_round, native_clock_state
from framework_pause_boundary import native_physics


# Units are native scene units, seconds and quaternion components. These bounds
# are deliberately much smaller than walking/interaction distances. They cover
# the measured sub-microunit plate/proxy drift; they are acceptance criteria for
# search, not evidence of identical PhysX state or corrections to native state.
LIMITS = {
    "position": 0.00002, "rotation": 0.000002,
    "velocity": 0.00002, "angularVelocity": 0.00002,
    "centerOfMass": 0.000002, "inertiaTensor": 0.000002,
}
BODY_FIELDS = {
    "position": "position", "transformPosition": "position", "transformLocalPosition": "position",
    "worldCenterOfMass": "position", "centerOfMass": "centerOfMass",
    "rotation": "rotation", "transformRotation": "rotation", "transformLocalRotation": "rotation",
    "inertiaTensorRotation": "rotation", "inertiaTensor": "inertiaTensor",
    "rawVelocity": "velocity", "resumeVelocity": "velocity",
    "rawAngularVelocity": "angularVelocity", "resumeAngularVelocity": "angularVelocity",
}


def differences(a, b, path=()):
    if first_difference(a, b) is None:
        return []
    if isinstance(a, dict) and isinstance(b, dict):
        result = []
        for key in sorted(a.keys() | b.keys()):
            if key not in a or key not in b:
                result.append({"path": path+(str(key),), "originalPresent": key in a,
                               "replayPresent": key in b, "original": a.get(key), "replay": b.get(key)})
            else:
                result.extend(differences(a[key], b[key], path+(str(key),)))
        return result
    if isinstance(a, list) and isinstance(b, list) and len(a) == len(b):
        return [d for i, (x, y) in enumerate(zip(a, b)) for d in differences(x, y, path+(str(i),))]
    return [{"path": path, "original": a, "replay": b}]


def projection(state, receipt):
    if state.get("state") != "Paused" or state.get("requestPending") or state.get("errors") or state.get("invalidStateReason"):
        raise ValueError("Comparison requires a settled valid native boundary")
    live = [e for e in state["entities"] if e.get("exists")]
    entities = {str(e["id"]): e for e in live}
    if len(entities) != len(live):
        raise ValueError("Duplicate live native identity")
    body_rows = native_physics(receipt)["bodies"]
    bodies = {str(b["entityId"]): b for b in body_rows}
    food_rows = receipt["detail"]["entities"]
    food = {str(e["id"]): e.get("composition") for e in food_rows if str(e["id"]) in entities}
    if len(food) != len([e for e in food_rows if str(e["id"]) in entities]):
        raise ValueError("Duplicate live native food identity")
    return {"frame": state["frame"], "entities": entities, "bodies": bodies, "food": food,
            "round": gameplay_round(receipt["bridge"]["nativeRound"]),
            "clocks": native_clock_state(receipt["bridge"])}


def tolerance_kind(path):
    if len(path) == 4 and path[0] == "entities" and path[2] in ("position", "rotation", "velocity", "angularVelocity"):
        return path[2] if path[3] in (("x", "y", "z", "w") if path[2] == "rotation" else ("x", "y", "z")) else None
    if len(path) == 5 and path[0] == "entities" and path[2:4] == ("chef", "lastVelocity") and path[4] in ("x", "y", "z"):
        return "velocity"
    if len(path) == 4 and path[0] == "bodies" and path[2] in BODY_FIELDS:
        kind = BODY_FIELDS[path[2]]
        return kind if path[3] in (("x", "y", "z", "w") if kind == "rotation" else ("x", "y", "z")) else None
    return None


def compare_projections(original, replay):
    rows = differences(original, replay)
    for row in rows:
        kind = tolerance_kind(row["path"])
        a, b = row["original"], row["replay"]
        numeric = type(a) in (int, float) and type(b) in (int, float) and math.isfinite(a) and math.isfinite(b)
        limit = LIMITS.get(kind)
        error = abs(a-b) if numeric else None
        row.update(field="$/"+"/".join(row.pop("path")), physicalField=kind, absoluteError=error,
                   limit=limit, accepted=bool(kind and numeric and "originalPresent" not in row and error <= limit))
    return {"passed": all(r["accepted"] for r in rows), "rawEqual": not rows, "rawDifferences": rows,
            "firstRejectedDifference": next((r for r in rows if not r["accepted"]), None),
            "toleranceProfile": dict(LIMITS),
            "classification": "Physical-tolerant search boundary, never exact raw parity; no native state changed",
            "exactGates": ["logical identities and attachments", "native food", "orders and recipes", "ledger", "native time and private clocks", "nonphysical fields"]}


def compare_boundary(original_state, original_receipt, state, receipt):
    return compare_projections(projection(original_state, original_receipt), projection(state, receipt))
