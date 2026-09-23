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

## Same-object physics replay canary (2026-09-22)

Pass `--physicsReplayReset` to run `PhysicsSceneReplay` instead of the older
freeze/pose-restore test. The existing `PhysicsRepro` runtime initializer routes
the flag explicitly; this matters when replacing a player's managed assembly,
because a newly added `[RuntimeInitializeOnLoadMethod]` may not be present in
the already-built player's registration metadata. Without the flag, the older
test is unchanged.

The canary disables automatic simulation, constructs an inactive floor and
capsule, inserts them in a fixed order, and calls `Physics.Simulate(0.02f)` for
24 scripted steps. It then removes **all** physics participants by deactivating
their GameObjects, resets their public state, reactivates the *same* managed
GameObjects/Rigidbody/Colliders in the original order, and repeats the same
mutations and simulation steps. It compares raw Rigidbody/Transform/bounds
float bits, sleep state, raycast and overlap results including order, and
ordered collision callbacks including relative-velocity bits. Step 12 is an
explicit reported checkpoint. The optional `--lateSpawn true` variant keeps a
box Rigidbody/Collider allocated but inactive until step 7; it tests late
**activation**, not creation of a new Unity object during replay.

The Unity Editor's batch build was blocked by its legacy license check on this
machine. The evidence player instead copies the existing 32-bit 2017.4.8f1
development player and recompiles only `Assembly-CSharp.dll` with the same
editor's bundled Mono compiler. `UnityPlayer.dll` is unchanged. To reproduce,
choose a destination that does not already exist:

```powershell
$src = 'M:\projects\game-test-2\artifacts\unity-2017-physics-repro\win32-story11-scene-v5'
$dst = 'M:\projects\game-test-2\framework\artifacts\unity2017-replay-reset-new'
if (Test-Path -LiteralPath $dst) { throw 'Choose a new destination; do not overwrite evidence.' }
New-Item -ItemType Directory -Path $dst | Out-Null
Copy-Item -Path (Join-Path $src '*') -Destination $dst -Recurse -Force
$managed = Join-Path $dst 'PhysicsRepro_Data\Managed'
$mono = 'C:\Program Files\Unity\Hub\Editor\2017.4.8f1\Editor\Data\Mono\bin\mono.exe'
$gmcs = 'C:\Program Files\Unity\Hub\Editor\2017.4.8f1\Editor\Data\Mono\lib\mono\2.0\gmcs.exe'
$assets = 'M:\projects\game-test-2\framework\experiments\unity-2017-physics-repro\Assets'
$refs = @(Get-ChildItem -LiteralPath $managed -Filter 'UnityEngine*.dll' |
    ForEach-Object { '-r:' + $_.FullName })
$compilerArgs = @('-target:library', '-debug+', '-nowarn:649',
    ('-out:' + (Join-Path $managed 'Assembly-CSharp.dll'))) + $refs + @(
    (Join-Path $assets 'PhysicsRepro.cs'),
    (Join-Path $assets 'PhysicsSceneReplay.cs'),
    (Join-Path $assets 'PhysicsReplayEventRecorder.cs'))
& $mono $gmcs @compilerArgs
if ($LASTEXITCODE -ne 0) { throw 'Managed compilation failed' }
```

Run the simple and late-activation cases with unique output paths:

```powershell
$exe = Join-Path $dst 'PhysicsRepro.exe'
$out = Join-Path $dst 'late-reverse-noempty.json'
$log = Join-Path $dst 'late-reverse-noempty-player.log'
$playerArgs = @('-batchmode', '-nographics', '--physicsReplayReset',
    '--out', ('"' + $out + '"'), '--lateSpawn', 'true',
    '--resetRemovalOrder', 'reverse', '--emptyResetStep', 'false',
    '-logFile', ('"' + $log + '"'))
Start-Process -FilePath $exe -ArgumentList $playerArgs -Wait -WindowStyle Hidden
```

Set `--lateSpawn false` for the simple capsule/floor case. The reset-order
flag accepts `forward` or `reverse`; `--emptyResetStep true` adds one manual
physics step after all objects have been deactivated and before reactivation.

The simple capsule/floor case **passed**: all 24 per-step samples and 24
ordered callbacks matched, including the known `0 -> 0.05` contact-correction
staircase; the same object/component instance IDs survived reset. Its report is
[`capsule-floor-r2.json`](../../artifacts/unity2017-replay-reset-r1/capsule-floor-r2.json).

The late-activation case **failed** in all four reset variations (forward or
reverse removal, with or without an empty step). All 24 per-step numeric body
and floor bit arrays still matched, and the step-12 checkpoint sample matched.
However, the first callback at step 7 switched from late-box→capsule to
capsule→late-box. There were 28 differing ordered callback records out of 52;
the per-step callback payload *multiset* also differs because some callback
relative-velocity components changed `+0` to `-0`. `Physics.OverlapSphere`
returned the same colliders in a different order at step 13. This is a strict
observable mismatch even though the bodies' numeric motion agreed. Repeating
the default late-activation run in a fresh process produced the same mismatch.
The four receipts are under
[`artifacts/unity2017-replay-reset-r1`](../../artifacts/unity2017-replay-reset-r1/),
named `late-{reverse,forward}-{noempty,empty}.json`.

Reset took about 0.9–1.3 ms and replaying 24 steps with queries/callback
recording took about 7.6–8.5 ms in the late-activation runs. Those are small
synthetic scenes, not a Story 1-1 speed estimate. Even the passing simple case
is only a **behavioral canary**: matching public outputs does not prove private
PhysX scene, pool, or query-tree state parity. The late failure demonstrates
that preserving managed object identities and removing/reinserting their
physics participants is not, by itself, a complete scene reset.
