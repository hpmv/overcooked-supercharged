"""Read-only positions from an extracted Unity YAML scene; never native ID proof."""
import argparse
import hashlib
import json
import math
import re
from pathlib import Path


def vector(block, name, keys):
    match = re.search(r"^  " + name + r": \{([^}]+)\}", block, re.M)
    if not match:
        raise ValueError("Missing " + name)
    fields = dict(re.findall(r"(\w+):\s*([^,}]+)", match[1]))
    result = [float(fields[k]) for k in keys]
    if not all(math.isfinite(v) for v in result):
        raise ValueError("Nonfinite vector")
    return result


def reference(block, name):
    match = re.search(r"^  " + name + r": \{fileID: (-?\d+)", block, re.M)
    return int(match[1]) if match else None


def multiply(a, b):
    x, y, z, w = a
    u, v, t, s = b
    return [w*u+x*s+y*t-z*v, w*v-x*t+y*s+z*u,
            w*t+x*v-y*u+z*s, w*s-x*u-y*v-z*t]


def rotate(q, point):
    n = sum(v*v for v in q)
    if n == 0:
        raise ValueError("Zero quaternion")
    return multiply(multiply(q, [*point, 0]), [-q[0]/n, -q[1]/n, -q[2]/n, q[3]/n])[:3]


def extract(path, pattern):
    raw = path.read_bytes()
    data = raw.decode("utf-8-sig")
    objects, transforms, components = {}, {}, {}
    for match in re.finditer(r"^--- !u!(\d+) &(-?\d+)[^\n]*\n(.*?)(?=^--- !u!|\Z)", data, re.M | re.S):
        kind, identity, block = int(match[1]), int(match[2]), match[3]
        owner = reference(block, "m_GameObject")
        if kind == 1:
            name = re.search(r"^  m_Name: (.*)$", block, re.M)
            active = re.search(r"^  m_IsActive: (\d+)$", block, re.M)
            objects[identity] = {"name": name[1].strip() if name else "", "active": bool(int(active[1])) if active else None}
        elif kind == 4:
            transforms[identity] = {"owner": owner, "parent": reference(block, "m_Father"),
                "position": vector(block, "m_LocalPosition", "xyz"),
                "rotation": vector(block, "m_LocalRotation", "xyzw"),
                "scale": vector(block, "m_LocalScale", "xyz")}
        if owner:
            components.setdefault(owner, []).append({"fileId": identity, "classId": kind})
    cache = {}
    def world(identity, seen=()):
        if identity in cache:
            return cache[identity]
        if identity in seen:
            raise ValueError("Transform cycle")
        t = transforms[identity]
        p, q, scale = t["position"], t["rotation"], t["scale"]
        if t["parent"]:
            pp, pq, ps = world(t["parent"], (*seen, identity))
            p = [a+b for a, b in zip(pp, rotate(pq, [a*b for a, b in zip(ps, p)]))]
            q = multiply(pq, q)
            scale = [a*b for a, b in zip(ps, scale)]
        cache[identity] = p, q, scale
        return cache[identity]
    rows = []
    for identity, t in transforms.items():
        obj = objects.get(t["owner"], {})
        if re.search(pattern, obj.get("name", ""), re.I):
            p, q, scale = world(identity)
            rows.append({"fileId": t["owner"], **obj, "position": p, "rotation": q,
                         "scale": scale, "components": components.get(t["owner"], [])})
    return {"source": str(path.resolve()), "sha256": hashlib.sha256(raw).hexdigest(),
            "scope": "Extracted asset hint only; YAML file IDs are not native game entity IDs. TRS hierarchy assumes no shear.",
            "pattern": pattern, "objects": rows}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--scene", type=Path, required=True)
    parser.add_argument("--pattern", default="ChoppingBoard|DispenserCrate|Plate|Floor|NewCrate")
    parser.add_argument("--out", type=Path, required=True)
    args = parser.parse_args()
    if args.out.exists():
        parser.error("Use a new output path")
    result = extract(args.scene, args.pattern)
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(json.dumps(result, indent=2), encoding="utf-8")
    print(json.dumps({"objects": len(result["objects"]), "evidence": str(args.out)}))
