"""Read the named original scene components and hash bounded mechanism evidence.

No game connection or installed-file mutation. Output documents capacity and
catch prerequisites; it does not infer a collision from a stopped projectile.
"""
import hashlib
import json
from pathlib import Path

import UnityPy

ROOT = Path(__file__).resolve().parents[1]
SCENE = Path(r"K:\trash\Steam\steamapps\common\Overcooked! 2\Overcooked2_Data\StreamingAssets\Windows\s_day_3_4")
SOURCES = Path(r"M:\projects\AssetRipper\Source\0Bins\AssetRipper.Tools.SystemTester\Release\Ripped\ExportedProject\Assets\Scripts\Assembly-CSharp")


def sha(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def main():
    native = UnityPy.load(str(SCENE))
    selected = {8310, 8325, 9849, 9864, 6995, 7004}
    objects = [dict(pathId=o.path_id, type=o.type.name, data=o.read_typetree())
               for o in native.objects if o.path_id in selected]
    by_id = {o["pathId"]: o["data"] for o in objects}
    assert set(by_id) == selected
    assert by_id[8325]["m_capacity"] == by_id[9864]["m_capacity"] == 3
    assert by_id[8310]["m_requireAttached"] == by_id[9849]["m_requireAttached"] == 1
    names = ["ServerIngredientContainer.cs", "MixableContainer.cs",
             "ServerIngredientCatcher.cs", "ServerAttachmentCatchingProxy.cs",
             "ServerAttachStation.cs", "ServerMixingStation.cs",
             "ServerThrowableItem.cs", "ServerAttachmentThrower.cs"]
    artifacts = ["routes/probes/full-near-far-bowl-flour.json",
                 "routes/probes/full-near-far-bowl-flour-upper.json",
                 "artifacts/full-near-far-bowl-flour-a-proof.json",
                 "artifacts/full-near-far-bowl-flour-b-proof.json",
                 "artifacts/far-bowl-lower-sweep-screen.json",
                 "artifacts/full-near-far-bowl-flour-upper-preflight.json",
                 "artifacts/far-bowl-staging-screen.json"]
    report = {
        "ok": True,
        "classification": "Original scene capacity/attachment evidence; no far-bowl catch performance claim",
        "originalScene": str(SCENE), "originalSceneSha256": sha(SCENE),
        "unityPyVersion": UnityPy.__version__, "components": objects,
        "sourceHashes": {str(SOURCES / name): sha(SOURCES / name) for name in names},
        "artifacts": {name: sha(ROOT / name) for name in artifacts},
        "conclusions": [
            "Both original scene bowls have capacity3; the generic exported prefab's capacity4 does not override this scene fact.",
            "Native ingredient-container capacity is count based. A duplicate in a partial bowl is not excluded by ingredient identity.",
            "A full three-item near bowl cannot consume another Flour through the capacity-gated native catcher; its solid collider remains present.",
            "An empty attach station can catch a stopped throwable; observed later board attachment does not identify the collision that first ended flight.",
            "B changes only final staging to a full-clearance reachable point. Static reachability is not a flight/catch proof."
        ],
        "sourceAnchors": {
            "capacity": "ServerIngredientContainer.cs:57-65",
            "catchPrerequisites": "ServerIngredientCatcher.cs:21-49",
            "occupiedAndEmptyStationCatch": "ServerAttachStation.cs:450-471",
            "nativeCollisionEndsFlight": "ServerThrowableItem.cs:189",
            "nativeGamePlacementOnMixer": "ServerMixingStation.cs:79"
        },
        "scriptSha256": sha(Path(__file__))
    }
    output = ROOT / "artifacts/far-bowl-native-mechanics-evidence.json"
    output.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"ok": True, "output": str(output), "sha256": sha(output)}))


if __name__ == "__main__":
    main()
