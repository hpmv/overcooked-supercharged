"""Extract a reproducible physics-only manifest from a Unity YAML scene."""

import argparse
import hashlib
import json
import math
import re
from pathlib import Path


PHYSICS_CLASS_IDS = {54, 64, 65, 136}


def reference(block, name):
    match = re.search(r"^  " + re.escape(name) + r": \{fileID: (-?\d+)", block, re.M)
    return int(match.group(1)) if match else 0


def reference_with_guid(block, name):
    match = re.search(
        r"^  " + re.escape(name) +
        r": \{fileID: (-?\d+)(?:, guid: ([0-9a-f]+), type: (\d+))?\}",
        block,
        re.M,
    )
    if not match:
        return {"fileId": 0, "guid": "", "type": 0}
    return {
        "fileId": int(match.group(1)),
        "guid": match.group(2) or "",
        "type": int(match.group(3) or 0),
    }


def scalar(block, name, fallback=None, convert=str):
    match = re.search(r"^  " + re.escape(name) + r": (\S+)", block, re.M)
    return convert(match.group(1)) if match else fallback


def vector(block, name, keys="xyz"):
    match = re.search(r"^  " + re.escape(name) + r": \{([^}]+)\}", block, re.M)
    if not match:
        raise ValueError("Missing vector " + name)
    values = dict(re.findall(r"(\w+):\s*([^,}]+)", match.group(1)))
    result = [float(values[key]) for key in keys]
    if not all(math.isfinite(value) for value in result):
        raise ValueError("Non-finite vector " + name)
    return result


def parse_scene(path):
    raw = path.read_bytes()
    data = raw.decode("utf-8-sig")
    blocks = []
    for ordinal, match in enumerate(re.finditer(
        r"^--- !u!(\d+) &(-?\d+)[^\n]*\n(.*?)(?=^--- !u!|\Z)",
        data,
        re.M | re.S,
    )):
        blocks.append({
            "ordinal": ordinal,
            "classId": int(match.group(1)),
            "fileId": int(match.group(2)),
            "text": match.group(3),
        })
    return raw, blocks


def extract(path):
    raw, blocks = parse_scene(path)
    game_objects = {}
    transforms = {}
    physics = []

    for entry in blocks:
        class_id = entry["classId"]
        file_id = entry["fileId"]
        block = entry["text"]
        if class_id == 1:
            name = re.search(r"^  m_Name: (.*)$", block, re.M)
            tag = re.search(r"^  m_TagString: (.*)$", block, re.M)
            component_match = re.search(
                r"^  m_Component:\n(.*?)(?=^  \w)", block, re.M | re.S
            )
            component_ids = []
            if component_match:
                component_ids = [
                    int(value)
                    for value in re.findall(
                        r"component: \{fileID: (-?\d+)\}", component_match.group(1)
                    )
                ]
            game_objects[file_id] = {
                "fileId": file_id,
                "ordinal": entry["ordinal"],
                "name": name.group(1).strip() if name else "",
                "layer": scalar(block, "m_Layer", 0, int),
                "tag": tag.group(1).strip() if tag else "Untagged",
                "active": scalar(block, "m_IsActive", 1, int) == 1,
                "componentFileIds": component_ids,
            }
        elif class_id == 4:
            owner = reference(block, "m_GameObject")
            transforms[file_id] = {
                "fileId": file_id,
                "owner": owner,
                "ordinal": entry["ordinal"],
                "parent": reference(block, "m_Father"),
                "localPosition": vector(block, "m_LocalPosition"),
                "localRotation": vector(block, "m_LocalRotation", "xyzw"),
                "localScale": vector(block, "m_LocalScale"),
                "rootOrder": scalar(block, "m_RootOrder", 0, int),
            }
        elif class_id in PHYSICS_CLASS_IDS:
            owner = reference(block, "m_GameObject")
            value = {
                "fileId": file_id,
                "classId": class_id,
                "owner": owner,
                "ordinal": entry["ordinal"],
            }
            if class_id == 54:
                value.update({
                    "mass": scalar(block, "m_Mass", 1.0, float),
                    "drag": scalar(block, "m_Drag", 0.0, float),
                    "angularDrag": scalar(block, "m_AngularDrag", 0.05, float),
                    "useGravity": scalar(block, "m_UseGravity", 1, int) == 1,
                    "kinematic": scalar(block, "m_IsKinematic", 0, int) == 1,
                    "interpolate": scalar(block, "m_Interpolate", 0, int),
                    "constraints": scalar(block, "m_Constraints", 0, int),
                    "collisionDetection": scalar(block, "m_CollisionDetection", 0, int),
                })
            else:
                value.update({
                    "material": reference_with_guid(block, "m_Material"),
                    "trigger": scalar(block, "m_IsTrigger", 0, int) == 1,
                    "enabled": scalar(block, "m_Enabled", 1, int) == 1,
                })
                if class_id == 64:
                    value.update({
                        "convex": scalar(block, "m_Convex", 0, int) == 1,
                        "cookingOptions": scalar(block, "m_CookingOptions", 0, int),
                        "skinWidth": scalar(block, "m_SkinWidth", 0.01, float),
                        "mesh": reference_with_guid(block, "m_Mesh"),
                    })
                elif class_id == 65:
                    value.update({
                        "center": vector(block, "m_Center"),
                        "size": vector(block, "m_Size"),
                    })
                elif class_id == 136:
                    value.update({
                        "center": vector(block, "m_Center"),
                        "radius": scalar(block, "m_Radius", 0.5, float),
                        "height": scalar(block, "m_Height", 2.0, float),
                        "direction": scalar(block, "m_Direction", 1, int),
                    })
            physics.append(value)

    transform_by_owner = {value["owner"]: key for key, value in transforms.items()}
    required_transforms = set()
    for component in physics:
        transform_id = transform_by_owner.get(component["owner"])
        if not transform_id:
            raise ValueError("Physics owner has no Transform: " + str(component["owner"]))
        while transform_id:
            if transform_id in required_transforms:
                break
            required_transforms.add(transform_id)
            transform_id = transforms[transform_id]["parent"]

    required_owners = {transforms[value]["owner"] for value in required_transforms}
    objects = []
    for value in sorted(
        (game_objects[owner] for owner in required_owners),
        key=lambda item: item["ordinal"],
    ):
        transform_id = transform_by_owner[value["fileId"]]
        transform = transforms[transform_id]
        kept_components = [
            component_id
            for component_id in value["componentFileIds"]
            if component_id == transform_id
            or any(item["fileId"] == component_id for item in physics)
        ]
        objects.append({
            **value,
            "transformFileId": transform_id,
            "parentTransformFileId": transform["parent"],
            "localPosition": transform["localPosition"],
            "localRotation": transform["localRotation"],
            "localScale": transform["localScale"],
            "rootOrder": transform["rootOrder"],
            "componentFileIds": kept_components,
        })

    def object_path(owner):
        names = []
        transform_id = transform_by_owner.get(owner, 0)
        while transform_id:
            transform = transforms[transform_id]
            names.append(game_objects[transform["owner"]]["name"])
            transform_id = transform["parent"]
        return "/".join(reversed(names))

    for component in physics:
        component["path"] = object_path(component["owner"])

    counts = {}
    for component in physics:
        key = str(component["classId"])
        counts[key] = counts.get(key, 0) + 1

    return {
        "kind": "unity-yaml-physics-scene",
        "version": 1,
        "source": str(path.resolve()),
        "sourceSha256": hashlib.sha256(raw).hexdigest(),
        "scope": (
            "Physics components and their complete Transform ancestor closure; "
            "serialized values only, with no game MonoBehaviours."
        ),
        "counts": {
            "objects": len(objects),
            "components": len(physics),
            "rigidbodies": counts.get("54", 0),
            "meshColliders": counts.get("64", 0),
            "boxColliders": counts.get("65", 0),
            "capsuleColliders": counts.get("136", 0),
        },
        "objects": objects,
        "components": sorted(physics, key=lambda item: item["ordinal"]),
    }


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
    print(json.dumps({
        "evidence": str(args.out),
        "sourceSha256": result["sourceSha256"],
        **result["counts"],
    }))
