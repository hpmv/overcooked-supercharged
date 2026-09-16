# Unity 2017 / PhysX rewind side investigation

Date: 2026-09-08

## Result

The numerical `0 -> 0.05` chef-height sequence is a PhysX contact correction.
An empty Unity project reproduces the same bit-level staircase when the chef
capsule is restored at world `Y=0` against Story 1-1's floor top at `Y=0.05`:

| Physics step | body Y | IEEE-754 bits |
|---:|---:|---:|
| 1 | 0.039999961853027344 | 1025758976 |
| 2 | 0.04799997806549072 | 1027906464 |
| 3 | 0.04960012435913086 | 1028336000 |
| 4 | 0.049919962882995605 | 1028421856 |
| 5 | 0.049984097480773926 | 1028439072 |
| 6 | 0.04999685287475586 | 1028442496 |
| 7 | 0.049999356269836426 | 1028443168 |
| 8 | 0.04999995231628418 | 1028443328 |

This rules out `ServerWorldObjectSynchronizer` as the mechanism producing
those numbers. The floor contact solver produces them without any Overcooked
code present.

It does **not** reproduce the important failure: the minimal project's
original and restored continuations are bit-exact. This remains true after
reconstructing the complete serialized Story 1-1 physics scene, adding the
four runtime plate-proxy rigidbodies, using the observed checkpoint pose
restore order, and running the same freeze/continue/restore/unfreeze
lifecycle. The full game
alone has shown one branch settling `0.05 -> 0` and the other branch correcting
`0 -> 0.05`. Therefore the current evidence is:

- physics explains the displacement and its exact convergence values;
- a generic capsule-on-flat-floor PhysX defect does not explain the branch
  asymmetry;
- the complete static collision setup and ordinary history produced from it
  are insufficient to create the branch asymmetry; and
- the missing input, if it is still physics-related, was seeded by some
  original-game initialization, activation/insertion order, or Unity native
  property call that the reconstructed scene does not execute.

The clone does build and rewind its own PhysX contact caches, sleeping/island
state, and kinematic history. Its negative result therefore answers the
important qualification: hidden PhysX state is no longer a broad explanation.
It is plausible only if Overcooked seeds that state differently before the
checkpoint.

## Same test inside the original game process

The empty capsule/floor test was also injected into the already-running
Overcooked process as an isolated authoring module. This removes the remaining
global-environment difference: the probe uses the shipped `UnityPlayer.dll`,
the game's loaded PhysX world, project physics settings, frame scheduler, and
the same `TimeManager` freeze/unfreeze and native-checkpoint lifecycle as the
real chefs.

The probe was placed at `(1000,0,1000)`, outside the kitchen. It copied the
real chef Rigidbody, CapsuleCollider, and PhysicMaterial properties, created a
private floor with top `Y=0.05`, and ignored collisions pairwise between its
capsule/floor and every game collider. It contained no Overcooked gameplay or
synchronizer components. Its source and runner are:

- [`InProcessPhysicsReproModule.cs`](../framework/modules/inprocess-physics-repro/InProcessPhysicsReproModule.cs)
- [`framework_inprocess_physics_repro.py`](../scripts/framework_inprocess_physics_repro.py)

The 60-frame paired run passed exactly. Both branches produced the same raw
Y-bit sequence, beginning:

```text
0, 1025758976, 1027906464, 1028335968, 1028421856,
1028439040, 1028439040, 1028442464, 1028443168,
1028443296, 1028443328, ...
```

The checkpoint was frame 864 and both continuations ended at frame 924 with
60 active samples. The full receipt is
[`inprocess-game-r1-paired-60.json`](../artifacts/unity-2017-physics-repro/inprocess-game-r1-paired-60.json),
with a compact evidence map in
[`inprocess-game-summary-v1.json`](../artifacts/unity-2017-physics-repro/inprocess-game-summary-v1.json).
The probe was then destroyed and its module retired.

This is a stronger negative control than the standalone clone. The global game
engine configuration and merely sharing its physics scene are not sufficient
to cause the asymmetry. The missing state follows the real chef actor,
component lifecycle, or the native history established for that actor.

## Managed mutation boundary

A separate read-only tracer patched 53 relevant managed methods and sampled
all four real chefs at FixedUpdate, Update, and LateUpdate boundaries. It
covered local controls and `RigidbodyMotion`, `TimeManager.FrozenPhysicsData`,
position recorders, the actual local chef synchronizer hierarchy, and every
currently loaded restore/pose/contact/sync authoring seam.

The paired 12-frame run again diverged:

- in the original continuation, chefs 43 and 45 descended from `0.05` to `0`,
  chef 44 remained at `0.05`, and chef 46 rose from `0` to `0.05`;
- after rewind, chefs 43-45 remained at `0.05` and chef 46 remained at `0`;
- every height transition occurred between the tracer's FixedUpdate and
  Update samples; and
- during active frames, `ClientPlayerControlsImpl_Default.Update_Movement`,
  `RigidbodyMotion.Movement`, and `RigidbodyMotion.SetVelocity` ran the same
  number of times in both branches and caused zero immediate changes in the
  sampled public Rigidbody/Transform state.

The restore and freeze methods listed in the artifact under the `original`
segment are expected warp-boundary work: the segment label changes only after
the warp completes. They are not active-continuation writers. A zero-motion
`MovePosition` call can in principle change an unobservable native target
without immediately changing public pose, but the earlier suppression A/B
already showed that removing those calls does not remove the drift.

The combined evidence places the actual visible Y mutation in Unity/PhysX's
native simulation phase, rather than a hidden managed pose assignment after
rewind. The source, paired receipt, and retirement receipt are:

- [`ChefManagedMutationTracerModule.cs`](../framework/modules/chef-managed-mutation-tracer/ChefManagedMutationTracerModule.cs)
- [`chef-managed-mutation-trace-r2-paired-12.json`](../artifacts/unity-2017-physics-repro/chef-managed-mutation-trace-r2-paired-12.json)
- [`managed-mutation-summary-v1.json`](../artifacts/unity-2017-physics-repro/managed-mutation-summary-v1.json)
- [`chef-managed-mutation-tracer-r2-retired.json`](../artifacts/unity-2017-physics-repro/chef-managed-mutation-tracer-r2-retired.json)

The local-only synchronization simplification was active during this run.
`LocalSyncBypass-r1` suppresses exactly
`ServerChefSynchroniser.GetServerUpdate` for objects that also own the local
`ClientOnTheServerChefSynchroniser`; it had skipped 3,676 updates with zero
pass-throughs and no failure after this trace. The real-chef divergence
therefore persists with that outbound local-chef synchronization path removed.
This does not remove the base synchronizer from unrelated world objects.

## Native debugger feasibility

The shipped x86 player embeds CodeView GUID
`638D1878-FE24-4B65-B675-A2C146E30E24`, age 1. That exactly matches Unity
Hub's installed non-development 2017.4.8f1 PDB,
`UnityPlayer_Win32_x86.pdb` (SHA-256
`9154B25399624EB2774BC7355E14E2B1ED0A048EAB85259CA3618938ABE3E979`).
The PE timestamp, image layout, section addresses, and sizes also match.

The shipped and stock DLL hashes differ because the shipped image has a larger
certificate and one changed byte in `.text`, at section-relative offset
`0x4B03D8`. The byte changes a conditional jump to an unconditional jump
inside `InitializeMonoFromMain`; it is outside the physics target regions. All
other sections are byte-identical. At least 512 bytes around each targeted
Rigidbody/PhysX function below are identical, so those symbol locations are
usable for a focused debugger trace. The machine has both x86 CDB and Visual
Studio 2022 installed.

| Native function | RVA in `UnityPlayer.dll` |
|---|---:|
| `PhysicsManager::Simulate` | `0x47BA70` |
| `Rigidbody::MovePosition` | `0x483550` |
| `Rigidbody_CUSTOM_INTERNAL_set_position` | `0x5A13C0` |
| `Rigidbody_CUSTOM_INTERNAL_CALL_MovePosition` | `0x5A15F0` |
| `NpScene::simulate` | `0xA0C300` |
| `Scb::Body::setBody2World` | `0xA136D0` |
| `NpRigidDynamic::setGlobalPose` | `0xA143D0` |
| `NpRigidDynamic::setKinematicTarget` | `0xA149E0` |
| `Sc::BodyCore::setBody2World` | `0xA3A9B0` |
| `PxsDynamicsContext::atomIntegrationParallel` | `0xA766A0` |
| `PxsDynamicsContext::solveParallel` | `0xA7B8C0` |
| `PxsRigidBody::updatePoseDependenciesV` | `0xA7E850` |
| `physx::copyToSolverBody` | `0xAAAFD0` |

The exact binary/PDB audit is
[`native-debugger-symbol-audit-v1.json`](../artifacts/unity-2017-physics-repro/native-debugger-symbol-audit-v1.json).
The next native session should begin before a fresh Story 1-1 load, capture the
four chefs' actor creation/insertion and first pose/kinematic calls, then carry
the identified native actor/body pointers through one original/rewound first
physics step. Attaching only at the current endpoint would miss the history
that the two negative controls now identify as the likely discriminator.

## Matched environment and geometry

- Editor/player: `2017.4.8f1 (5ab7f4878ef1)`, 32-bit Windows development
  player, 60 rendered frames per second and `fixedDeltaTime=0.02`.
- Project physics settings match the game: gravity `(0,-9.81,0)`, bounce
  threshold 2, sleep threshold 0.005, contact offset 0.01, solver iterations
  6/1, persistent contact generation, automatic simulation, automatic
  transform syncing, and sweep-and-prune broadphase.
- Rigidbody: mass 1, drag 0, angular drag 0.05, gravity disabled, rotation
  frozen, no interpolation, continuous collision detection.
- Capsule: center `(0,1,0)`, radius 0.4, height 2, Y direction, contact offset
  0.01.
- Hierarchy option: actor parent at `Y=0.75`, actor local `Y=-0.75` for world
  `Y=0`, matching the chef hierarchy form.
- Actual Story 1-1 support collider: static, non-trigger `BoxCollider` file ID
  4982 at `Design/Colliders/Collider`, world center
  `(14.45000019,-0.2,-3.9000002)`, size `(16.7,0.5,12.6)`, top `Y=0.05`.

The extracted Story 1-1 collider record is
[`artifacts/unity-2017-physics-repro/story11-box-colliders.json`](../artifacts/unity-2017-physics-repro/story11-box-colliders.json).

## Complete Story 1-1 reconstruction

The Unity-YAML extractor is
[`scripts/extract_unity_scene_physics.py`](../scripts/extract_unity_scene_physics.py).
It preserves each relevant GameObject's ancestor hierarchy, active state,
layer, local transform, component order, and all serialized physics fields.
The resulting manifest is
[`story11-physics-scene-v1.json`](../artifacts/unity-2017-physics-repro/story11-physics-scene-v1.json).

- Scene source SHA-256:
  `5CC3AC329FF2CDCAAFE98193A5D9D2EBF2B287C5C0B9BC7B6ACA1E85D4E89E0B`
- Manifest SHA-256:
  `93E199C56271E5A070B949BEE03DBDC2926DF8BFFDF2B1C771B5B2A2690C3CC5`
- 135 hierarchy objects and 133 serialized physics components
- 4 rigidbodies, 123 box colliders (84 solid and 39 trigger), 5 capsule
  colliders, and 1 convex trigger mesh collider
- all four chefs, with Player 1 as the observed body
- 4 colliderless plate-proxy rigidbodies created at runtime
- checkpoint pose restore order:
  Player 3, Player 1, Player 4, Player 2, then plate proxies 47-50

The strongest full-scene negative control is
[`story11-all-components-four-proxies-target0-neutral-realorder-v4.json`](../artifacts/unity-2017-physics-repro/runs/story11-all-components-four-proxies-target0-neutral-realorder-v4.json).
It includes every extracted physics component, all eight rigidbodies, all
triggers, the static NPC capsule, neutral game-like movement calls, and the
observed restore order. Player 1 first corrects from `Y=0` to `Y=0.05`; its
checkpoint, original continuation, restored boundary, and replay then remain
bit-identical.

Three more runs changed how the scene's serialized chef height was replaced
with runtime `Y=0`: `Rigidbody.position`, `Transform.position`, and
`Transform.localPosition`. Each produced the same upward staircase and exact
rewind:

- [`initial-body-position-v5.json`](../artifacts/unity-2017-physics-repro/runs/story11-all-four-proxies-initial-body-position-v5.json)
- [`initial-transform-position-v5.json`](../artifacts/unity-2017-physics-repro/runs/story11-all-four-proxies-initial-transform-position-v5.json)
- [`initial-local-position-v5.json`](../artifacts/unity-2017-physics-repro/runs/story11-all-four-proxies-initial-local-position-v5.json)

Finally,
[`story11-four-proxies-kinematic-target0-realorder-v4.json`](../artifacts/unity-2017-physics-repro/runs/story11-four-proxies-kinematic-target0-realorder-v4.json)
queues `MovePosition(Y=0)` while the chefs are frozen. The clone jumps directly
from `0.05` to `0`, then both original and replay perform the same upward
contact-correction staircase. It does not produce the game's downward
`0.05 -> 0.01 -> 0.002 -> ... -> 0` sequence.

## Rewind model

The harness uses the game's relevant lifecycle rather than teleporting an
always-dynamic actor:

1. save velocity, angular velocity, `isKinematic`, and `useGravity`;
2. set both velocities to zero, set `isKinematic=true`, then disable gravity;
3. record the future while frozen;
4. restore local and world poses twice while still frozen;
5. restore saved velocities;
6. wait at the restored frozen boundary;
7. restore kinematic/gravity state and replay.

Every sample compares raw float bits for Rigidbody pose, Transform world/local
pose, velocity, capsule bounds, kinematic state, and sleep state. No epsilon is
used.

## Case matrix

| Case | Result |
|---|---|
| Flat floor at `Y=0`, settled actor | exact original/replay |
| Flat floor top `Y=0.05`, settled actor | exact original/replay |
| Checkpoint actor `Y=0`, floor top `Y=0.05` | exact staircase reproduced; exact original/replay |
| Frozen restore vs dynamic round-trip restore | both exact |
| Root actor vs parented actor | both exact |
| Physics-only vs two same-position `MovePosition` calls per Update | both exact |
| Box-collider height seam crossings | exact after checkpoint velocity was included |
| Two-height MeshCollider seam crossings | exact after checkpoint velocity was included |
| Restore-time `Physics.SyncTransforms()` off/on | both exact; same staircase |
| Full Story 1-1 physics hierarchy and all triggers | exact original/replay |
| Four chefs plus four runtime plate proxies | exact original/replay |
| Game body restore order | exact original/replay |
| Four public initial-pose write paths | exact original/replay |
| Kinematic `MovePosition(Y=0)` before checkpoint | direct jump to zero, then exact upward correction in both branches |

The final sync A/B reports are:

- [`sync-ab-off.json`](../artifacts/unity-2017-physics-repro/runs/sync-ab-off.json)
- [`sync-ab-on.json`](../artifacts/unity-2017-physics-repro/runs/sync-ab-on.json)

Both have `passed=true`, checkpoint and restored-boundary `bodyYBits=0`, and
bit-identical original/replay arrays within each run. Calling
`Physics.SyncTransforms()` after the restore did not introduce an asymmetry.

The minimal project and runner source are under
[`experiments/unity-2017-physics-repro`](../experiments/unity-2017-physics-repro/README.md).

## Corresponding PhysX source

Unity's official material says Unity 5.5 upgraded its physics engine to PhysX
3.3.3, and Unity 2018.3 later upgraded from PhysX 3.3 to 3.4. This makes 3.3.3
the strongly supported upstream version for Unity 2017.4.8f1:

- https://discussions.unity.com/t/5-5-released/647436
- https://unity.com/blog/technology/introducing-unity-2018-3

The installed Unity development-player PDB independently proves that this x86
player statically linked source-built PhysX 3 libraries. Its modules include
`NpPhysics.cpp`, `NpRigidDynamic.cpp`, `ScBodyCore.cpp`,
`ScBodyCoreKinematic.cpp`, `PxsRigidBody.cpp`, and solver objects under paths
such as:

```text
C:\buildslave\physx\build\artifacts\compileroutput\libPhysX3_win32_vs2015_release\...
```

PDB SHA-256:
`1B302A31A72589459D7A7A655D153FD08370B8689724292DE35E8AD7936C403F`.

There is not yet an authoritative standalone source build. NVIDIA's official
forum identifies the former `NVIDIAGameWorks/PhysX-3.3` repository and its
tagged 3.3.3 release, but also records that the repository later disappeared:

- https://forums.developer.nvidia.com/t/new-github-repo-physx-3-3-old-repo-physx-is-deprecated/37658

The installed PDB is stripped of source-server and revision information, so it
does not identify Unity's exact internal/patched commit. NVIDIA's still-public
PhysX 3.4 and modern PhysX 5 repositories are useful references but are not the
corresponding solver version. Using an unverified mirror would weaken this
experiment's provenance.

If an authoritative 3.3.3 archive becomes available, the standalone x86 VS2015
test should create a static box with top `Y=0.05`, a dynamic capsule equivalent
to the Unity shape at actor `Y=0`, use the matching 6/1 solver iterations and
0.02-second simulation step, then exercise kinematic/global-pose restoration
and compare every actor pose bit. That should reproduce the depenetration
staircase. It may still not reproduce the full rewind asymmetry, because
Transform syncing, Rigidbody property order, and scene insertion/update order
are Unity integration behavior above the upstream solver.

## Next diagnostic for rewind parity

Do not resume route search yet. Full-game lifecycle traces already show that
the branch direction is selected by the **first physics step after unfreeze**,
before that frame's two neutral `RigidbodyMotion.Movement` calls:

- one pause unfreezes at `Y=0.05`, then the first physics step produces
  approximately `0.01`, followed by `0.002`, `0.0004`, and convergence to 0;
- another unfreezes near `Y=0`, then the first physics step produces
  approximately `0.04` and converges upward to `0.05`; and
- suppressing the neutral `MovePosition` calls does not eliminate the drift.

The next diagnostic should therefore move earlier in the full-game lifecycle:

1. trace creation/deserialization, parenting, enable/disable, and activation of
   each chef Rigidbody and CapsuleCollider;
2. trace every pose, kinematic, gravity, detect-collisions, constraints, and
   sleep/wake write from serialized `Y=0.1180732` through the first runtime
   `Y=0` state;
3. reproduce those calls and their actor insertion order one at a time in the
   full-scene clone; and
4. only descend into PhysX source if one of those Unity-level histories makes
   the clone alternate.

The minimal project is now the negative control: a proposed cause must create
the asymmetry there or explain which extra full-game state it depends on.

`ServerWorldObjectSynchroniser` is not the immediate pose writer. Its update
path reads Transform state to construct outgoing messages, while the local
`ClientOnTheServerChefSynchroniser` pose/event/resume handlers are empty. It
can be removed later to simplify a local-only TAS, but this experiment gives no
reason to expect that removal to fix the `0`/`0.05` branch.

## Build audit note

The original minimal players were built normally by Unity 2017.4.8f1. For the
final sync-only A/B, the legacy editor's batch process rejected its cached
license even while the GUI editor was usable. The A/B therefore copied the
ordinary `win32-parented` player and recompiled only `Assembly-CSharp.dll` with
the same editor's bundled `gmcs`; `UnityPlayer.dll` remained byte-identical,
SHA-256
`6CBA90003A6F4A1907E6A9CA4DF93547288561F8561B98D614BD0655FD859249`.
The current v5 managed harness SHA-256 is
`FBDE9B30C72CE9D1564199BDA80D000E6CCB32AD3C3A4C493294B2D729FBC180`.
The failed editor-build logs are preserved rather than hidden.
