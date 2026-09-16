"""File-only lower-diagonal throw screen; native dynamics are not modified.

Uses the observed Flour dimensions and native fixed-step drag/gravity. The
predicted free arc is checked against the measured pre-contact A trajectory.
Collision/catcher ordering is a geometric screen, not a native catch receipt.
"""
import hashlib
import json
import math
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def read(name):
    return json.loads((ROOT / name).read_text(encoding="utf-8-sig"))


def sha(name):
    with (ROOT / name).open("rb") as f:
        return hashlib.file_digest(f, "sha256").hexdigest()


def dot(a, b):
    return sum(x * y for x, y in zip(a, b))


def xz(p):
    return (p["x"], p["z"])


def flight(origin, direction):
    p = list(origin)
    v = [direction[0] * 18, math.tan(math.radians(12)) * 18, direction[1] * 18]
    yield tuple(p)
    for _ in range(60):
        v[1] -= 9.810000419616699 * .02
        v = [x * .96 for x in v]
        p = [x + speed * .02 for x, speed in zip(p, v)]
        yield tuple(p)


def box_overlap(p, direction, near):
    # Horizontal Flour OBB is aligned with its unchanged launch heading.
    # The original near bowl is effectively world-axis aligned in this capture.
    if p[1] > near[1] + .25 or p[1] + .4 < near[1] - .25:
        return False
    delta = (p[0] - near[0], p[2] - near[2])
    side = (-direction[1], direction[0])
    for axis in [(1, 0), (0, 1), direction, side]:
        flour_extent = .475 * (abs(dot(axis, direction)) + abs(dot(axis, side)))
        bowl_extent = .35 * (abs(axis[0]) + abs(axis[1]))
        if abs(dot(delta, axis)) > flour_extent + bowl_extent:
            return False
    return True


def sphere_overlap(p, direction, far):
    # Minimum distance between far catch sphere and the complete Flour OBB.
    side = (-direction[1], direction[0])
    delta = (far[0] - p[0], far[2] - p[2])
    d = [max(0, abs(dot(delta, axis)) - .475) for axis in [direction, side]]
    d.append(max(0, abs(far[1] - (p[1] + .2)) - .2))
    return sum(x * x for x in d) <= .7 * .7


def first_event(points, condition):
    for step, (a, b) in enumerate(zip(points, points[1:])):
        # Bounded substep sweep of the exact native step chord. It locates
        # the contact interval, without claiming native solver event timing.
        for sample in range(101):
            t = sample / 100
            p = tuple(x + (y - x) * t for x, y in zip(a, b))
            if condition(p):
                return {"fixedStepFraction": step + t, "seconds": (step + t) * .02, "position": p}
    return None


def main():
    snap = read("artifacts/full-near-far-bowl-flour-a-gf477.json")
    s = snap.get("state", snap)
    entities = {e["id"]: e for e in s["entities"]}
    c = next(c for c in s["chefs"] if c["playerId"] == 1)
    a = read("artifacts/full-near-far-bowl-flour-a-proof.json")
    released = next(p for p in a["trajectory"] if p["frame"] == a["firstFlightFrame"])
    origin = tuple(released["position"][k] for k in ["x", "y", "z"])
    direction = xz(c["forward"])
    dlen = math.hypot(*direction)
    direction = tuple(v / dlen for v in direction)
    offset = math.dist(xz(c["position"]), xz(released["position"]))
    native_unique = []
    for row in a["trajectory"]:
        if a["firstFlightFrame"] <= row["frame"] <= 477:
            p = tuple(row["position"][k] for k in ["x", "y", "z"])
            if not native_unique or native_unique[-1] != p:
                native_unique.append(p)
    simulated = list(flight(origin, direction))
    error = max(math.dist(native, model) for native, model in zip(native_unique, simulated))
    assert error < .00002, error
    near = (entities[6]["position"]["x"], .85, entities[6]["position"]["z"])
    far = (entities[3]["position"]["x"], .85, entities[3]["position"]["z"])
    candidates = []
    for x, z, file_name in [(27.2, -13.6, "27.2-13.6"), (27.2, -14.0, "27.2-14"), (26.32, -14.5, "26.32-14.5")]:
        path_name = "artifacts/far-bowl-lower-path-" + file_name + ".json"
        path = read(path_name)
        direction = (far[0] - x, far[2] - z)
        distance = math.hypot(*direction)
        direction = tuple(v / distance for v in direction)
        start = (x + offset * direction[0], origin[1], z + offset * direction[1])
        points = list(flight(start, direction))
        near_hit = first_event(points, lambda p: box_overlap(p, direction, near))
        far_catch = first_event(points, lambda p: sphere_overlap(p, direction, far))
        viable = path["success"] and far_catch is not None and (near_hit is None or far_catch["seconds"] < near_hit["seconds"])
        candidates.append({"staging": [x, z], "walkable": path["success"], "pathSha256": sha(path_name),
                           "release": start, "nearSolidFirstOverlap": near_hit, "farSphereFirstOverlapOnUnimpededArc": far_catch,
                           "viableScreen": viable, "qualification": "Hypothetical free-flight geometry; a near solid intersection invalidates the later predicted catch"})
    report = {"ok": True, "viableCandidateCount": sum(c["viableScreen"] for c in candidates),
              "nativeFreeArcSamples": len(native_unique), "nativeFreeArcMaxPositionError": error,
              "nativeReleaseOffsetFromChef": offset, "nativeFreeArcPeakBottomY": max(p[1] for p in simulated),
              "nativeNearBowlSolidTopY": 1.1, "flourNativeBoxSize": [.95, .4, .95], "flourCenterOffset": [0, .2, 0],
              "gravityY": -9.810000419616699, "fixedDelta": .02, "drag": 2, "candidates": candidates,
              "limitations": "Static observed near orientation, ideal target position and aim; no active chef traffic. Near-body collision before catch rejects the screen. No game calls or claimed native C performance. This is not an exhaustive search of all throw targets.",
              "sources": {name: sha(name) for name in ["artifacts/full-near-far-bowl-flour-a-gf477.json", "artifacts/full-near-far-bowl-flour-a-proof.json", "scripts/screen_far_bowl_lower.py"]}}
    out = ROOT / "artifacts/far-bowl-lower-sweep-screen.json"
    out.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    main()
