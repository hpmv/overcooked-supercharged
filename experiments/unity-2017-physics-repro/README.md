# Unity 2017 physics rewind reproduction

This is a deliberately empty Unity project for separating Overcooked 2 rewind
behavior from game code. It targets the game's installed editor version,
`2017.4.8f1`, and builds a 32-bit development player. It can use either simple
procedural geometry or a manifest reconstructed from Story 1-1's Unity scene.

The runtime creates either its procedural test geometry or the selected scene
manifest from code. It then:

1. advances to a checkpoint and freezes the body using the game's
   velocity/kinematic/gravity sequence;
2. records an original continuation;
3. freezes at the future boundary;
4. restores the checkpoint pose while the future body remains frozen;
5. optionally calls `Physics.SyncTransforms()`;
6. unfreezes and records the replay; and
7. compares raw IEEE-754 bits for body, transform, local transform, velocity,
   collider bounds, kinematic state, and sleeping state.

## Build

From PowerShell:

```powershell
& 'C:\Program Files\Unity\Hub\Editor\2017.4.8f1\Editor\Unity.exe' `
  -batchmode -nographics -quit `
  -projectPath 'M:\projects\game-test-2\experiments\unity-2017-physics-repro' `
  -executeMethod ReproBuild.BuildWindows32 `
  --buildPath 'M:\projects\game-test-2\artifacts\unity-2017-physics-repro\win32-new\PhysicsRepro.exe'
```

Use a new build directory rather than overwriting an evidence build.

## Key case

This places the actor at `Y=0` while the floor top is `Y=0.05`, matching the
five-centimetre contact correction observed in Story 1-1:

```powershell
$exe = 'M:\projects\game-test-2\artifacts\unity-2017-physics-repro\win32-new\PhysicsRepro.exe'
$args = @(
  '-batchmode', '-nographics',
  '--out', 'M:\projects\game-test-2\artifacts\unity-2017-physics-repro\runs\new-run.json',
  '--mode', 'physics-only',
  '--geometry', 'flat',
  '--restoreLifecycle', 'stay-frozen',
  '--startY', '0',
  '--floorTop', '0.05',
  '--warmupFrames', '0',
  '--frozenFrames', '6',
  '--continuationFrames', '60',
  '--restoreWrites', '2',
  '--parented', 'true',
  '--syncAfterRestore', 'false'
)
Start-Process -FilePath $exe -ArgumentList $args -Wait -WindowStyle Hidden
```

Change only `--syncAfterRestore` to `true` for the sync A/B.

## Complete Story 1-1 case

The manifest was generated from the original Unity YAML by
[`scripts/extract_unity_scene_physics.py`](../../scripts/extract_unity_scene_physics.py).
It contains the full ancestor hierarchy for all physics objects and all
serialized Rigidbody, BoxCollider, CapsuleCollider, and MeshCollider fields.

The strongest case includes all four serialized chef bodies, all solid and
trigger colliders, the static NPC capsule, four colliderless runtime plate
proxies, and the game's observed restore order:

```powershell
$exe = 'M:\projects\game-test-2\artifacts\unity-2017-physics-repro\win32-story11-scene-v5\PhysicsRepro.exe'
$manifest = 'M:\projects\game-test-2\artifacts\unity-2017-physics-repro\story11-physics-scene-v1.json'
$out = 'M:\projects\game-test-2\artifacts\unity-2017-physics-repro\runs\new-story11-full-scene.json'
$args = @(
  '-batchmode', '-nographics',
  '--out', $out,
  '--sceneManifest', $manifest,
  '--mode', 'game-neutral',
  '--sceneBodyCount', '4',
  '--primaryBodyFileId', '4972',
  '--includeTriggers', 'true',
  '--includeStaticCapsules', 'true',
  '--includePlateProxies', 'true',
  '--restoreBodyOrder', '4975,4972,4973,4974,47,48,49,50',
  '--initialMoveTargetY', '0',
  '--initialPoseMethod', 'move-position',
  '--warmupFrames', '30',
  '--frozenFrames', '6',
  '--continuationFrames', '60',
  '--restoreWrites', '2',
  '--syncAfterRestore', 'false'
)
Start-Process -FilePath $exe -ArgumentList $args -Wait -WindowStyle Hidden
```

Use a new output filename for every run. The report records the manifest hash,
component/body counts, selected options, raw float bits, and the first exact
difference. `--initialPoseMethod` accepts `move-position`, `body-position`,
`transform-position`, or `local-position`. `--precheckpointMoveTargetY` and
`--precheckpointFrames` can queue a kinematic target after warmup and before
the checkpoint.

The current full-scene run is bit-exact. See the evidence summary at
[`story11-full-scene-summary-v1.json`](../../artifacts/unity-2017-physics-repro/story11-full-scene-summary-v1.json).

## Original-game-process control

The same isolated capsule/floor setup has also been run inside the real
Overcooked process and global PhysX scene. It remained bit-exact across a
60-frame rewind, so the global engine/project configuration is not sufficient
to reproduce the real chefs' branch asymmetry. The hot module, paired runner,
and compact result are:

- [`InProcessPhysicsReproModule.cs`](../../framework/modules/inprocess-physics-repro/InProcessPhysicsReproModule.cs)
- [`framework_inprocess_physics_repro.py`](../../scripts/framework_inprocess_physics_repro.py)
- [`inprocess-game-summary-v1.json`](../../artifacts/unity-2017-physics-repro/inprocess-game-summary-v1.json)

A read-only 53-method managed trace then localized the real-chef height change
to the native physics interval. See the main findings document for the trace
and matching native PDB audit.

The full findings and evidence map are in
[`docs/UNITY-2017-PHYSICS-REPRO.md`](../../docs/UNITY-2017-PHYSICS-REPRO.md).
