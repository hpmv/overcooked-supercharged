"""Read-only exact native body/module-reset failure projection; no sockets."""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path


def pinned(path: Path) -> dict:
    data = path.read_bytes()
    return {"path": str(path.resolve()), "sha256": hashlib.sha256(data).hexdigest(), "bytes": len(data)}


def analyze(native_path: Path, status_path: Path) -> dict:
    native = json.loads(native_path.read_text(encoding="utf-8-sig"))["bridge"]
    status = json.loads(status_path.read_text(encoding="utf-8-sig"))["result"]
    module, result = status["module"], status["result"]
    checkpoints = native["nativeCheckpoints"]
    saved = {x["entityId"]: x for x in checkpoints["requestedBodyCheckpoint"]["bodies"]}
    current = {x["entityId"]: x for x in native["nativePhysics"]["bodies"]}
    live_modules = native["authoringModules"]["active"]
    if not any(m["slot"] == module["slot"] and m["sha256"] == module["sha256"] for m in live_modules):
        raise ValueError("Fresh module status does not match the failed native module identity")
    fields = ("mass", "centerOfMass", "inertiaTensor", "inertiaTensorRotation")
    rows = []
    for reset in result["massRestores"]:
        entity = reset["entityId"]
        if entity not in saved or entity not in current:
            raise ValueError("Reset references an uncaptured native body")
        target = {key: saved[entity][key] for key in fields}
        if reset["target"] != target:
            raise ValueError("Reset target does not match the failed native checkpoint")
        rows.append({
            "entityId": entity,
            "bodyInstanceId": saved[entity]["bodyInstanceId"],
            "restoreCall": reset["restoreCall"],
            "savedColliderCount": len(saved[entity]["colliders"]),
            "savedColliderKinds": [x["kind"] for x in saved[entity]["colliders"]],
            "savedIsKinematic": saved[entity]["rawIsKinematic"],
            "before": reset["before"], "target": target, "after": reset.get("after"),
            "resetCenterOfMass": reset["resetCenterOfMass"],
            "resetInertiaTensor": reset["resetInertiaTensor"],
            "numericFieldsUnchangedByBothResets": reset["before"] == reset.get("after"),
            "numericFieldsMatchTarget": reset.get("after") == target,
            "moduleExact": reset["exact"], "error": reset.get("error"),
            "finalPausedNativeFields": {key: current[entity][key] for key in fields},
        })
    return {
        "scope": "exact-native-module-reset-receipts-no-replay-claim",
        "inputs": [pinned(native_path), pinned(status_path)],
        "coreSha256": native["authoringModules"]["coreSha256"],
        "module": module,
        "frame": checkpoints["requestedBodyCheckpointFrame"],
        "failure": checkpoints["lastRestoreFailure"],
        "bodyResets": rows,
        "rotationAssignments": result["rotationRestores"],
        "qualification": "JSON numeric comparisons use no tolerance. Original raw JSON bytes remain hash-pinned; signed-zero encoding and private native automatic modes are not inferred. Empty collider membership is the saved checkpoint observation; successful recomputation and complete rewind are separate outcomes.",
    }


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("native", type=Path)
    parser.add_argument("module_status", type=Path)
    parser.add_argument("--out", type=Path, required=True)
    args = parser.parse_args()
    report = analyze(args.native, args.module_status)
    report["analyzer"] = pinned(Path(__file__))
    with args.out.open("x", encoding="utf-8") as stream:
        json.dump(report, stream, indent=2, allow_nan=False)
        stream.write("\n")


if __name__ == "__main__":
    main()
