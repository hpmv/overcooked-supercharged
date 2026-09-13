"""Extract read-only BoxCollider world bounds from a Unity YAML scene."""
import argparse
import hashlib
import json
import math
import re
from pathlib import Path


def fields(block, name):
    match = re.search(r"^  " + name + r": \{([^}]+)\}", block, re.M)
    if not match:
        raise ValueError("Missing " + name)
    return dict(re.findall(r"(\w+):\s*([^,}]+)", match[1]))


def vector(block, name, keys="xyz"):
    value = fields(block, name)
    result = [float(value[key]) for key in keys]
    if not all(math.isfinite(item) for item in result):
        raise ValueError("Nonfinite " + name)
    return result


def reference(block, name):
    match = re.search(r"^  " + name + r": \{fileID: (-?\d+)", block, re.M)
    return int(match[1]) if match else None


def scalar(block, name):
    match = re.search(r"^  " + name + r": (\S+)", block, re.M)
    return match[1] if match else None


def multiply(a, b):
    x, y, z, w = a
    u, v, t, s = b
    return [w*u+x*s+y*t-z*v, w*v-x*t+y*s+z*u,
            w*t+x*v-y*u+z*s, w*s-x*u-y*v-z*t]


def rotate(q, point):
    norm = sum(value*value for value in q)
    if norm == 0:
        raise ValueError("Zero quaternion")
    return multiply(multiply(q, [*point, 0]),
                    [-q[0]/norm, -q[1]/norm, -q[2]/norm, q[3]/norm])[:3]


def extract(path):
    raw = path.read_bytes()
    data = raw.decode("utf-8-sig")
    objects, transforms, components, boxes = {}, {}, {}, []
    for match in re.finditer(r"^--- !u!(\d+) &(-?\d+)[^\n]*\n(.*?)(?=^--- !u!|\Z)", data, re.M | re.S):
        kind, identity, block = int(match[1]), int(match[2]), match[3]
        owner = reference(block, "m_GameObject")
        if kind == 1:
            name = re.search(r"^  m_Name: (.*)$", block, re.M)
            objects[identity] = name[1].strip() if name else ""
        elif kind == 4:
            transforms[identity] = {"owner": owner, "parent": reference(block, "m_Father"),
                "position": vector(block, "m_LocalPosition"),
                "rotation": vector(block, "m_LocalRotation", "xyzw"),
                "scale": vector(block, "m_LocalScale")}
        elif kind == 65:
            boxes.append({"fileId": identity, "owner": owner,
                "center": vector(block, "m_Center"), "size": vector(block, "m_Size"),
                "enabled": scalar(block, "m_Enabled") == "1",
                "trigger": scalar(block, "m_IsTrigger") == "1"})
        if owner:
            components.setdefault(owner, []).append(kind)

    transform_by_owner = {value["owner"]: identity for identity, value in transforms.items()}
    cache = {}
    def world(identity, seen=()):
        if identity in cache:
            return cache[identity]
        if identity in seen:
            raise ValueError("Transform cycle")
        value = transforms[identity]
        position, rotation, scale = value["position"], value["rotation"], value["scale"]
        if value["parent"]:
            pp, pq, ps = world(value["parent"], (*seen, identity))
            position = [a+b for a, b in zip(pp, rotate(pq, [a*b for a, b in zip(ps, position)]))]
            rotation = multiply(pq, rotation)
            scale = [a*b for a, b in zip(ps, scale)]
        cache[identity] = position, rotation, scale
        return cache[identity]

    def object_path(owner):
        names = []
        current = transform_by_owner.get(owner)
        while current:
            value = transforms[current]
            names.append(objects.get(value["owner"], ""))
            current = value["parent"]
        return "/".join(reversed(names))

    rows = []
    for box in boxes:
        transform_id = transform_by_owner[box["owner"]]
        position, rotation, scale = world(transform_id)
        local_center = [a*b for a, b in zip(scale, box["center"])]
        center = [a+b for a, b in zip(position, rotate(rotation, local_center))]
        half = [abs(a*b)*0.5 for a, b in zip(scale, box["size"])]
        corners = []
        for x in (-half[0], half[0]):
            for y in (-half[1], half[1]):
                for z in (-half[2], half[2]):
                    corners.append([a+b for a, b in zip(center, rotate(rotation, [x, y, z]))])
        minimum = [min(corner[index] for corner in corners) for index in range(3)]
        maximum = [max(corner[index] for corner in corners) for index in range(3)]
        rows.append({**box, "name": objects.get(box["owner"], ""),
            "path": object_path(box["owner"]), "worldCenter": center,
            "worldRotation": rotation, "worldScale": scale,
            "aabbMin": minimum, "aabbMax": maximum,
            "hasRigidbody": 54 in components.get(box["owner"], [])})
    return {"source": str(path.resolve()), "sha256": hashlib.sha256(raw).hexdigest(),
        "scope": "Read-only YAML extraction; world AABBs assume hierarchical TRS without shear.",
        "boxColliders": rows}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--scene", type=Path, required=True)
    parser.add_argument("--out", type=Path, required=True)
    args = parser.parse_args()
    if args.out.exists():
        parser.error("Use a new output path")
    result = extract(args.scene)
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(json.dumps(result, indent=2), encoding="utf-8")
    print(json.dumps({"boxColliders": len(result["boxColliders"]), "evidence": str(args.out)}))
