# Offline PhysX 3.3.3 level-like interaction graph baseline

Run from the framework root after building the pinned Win32 source mirror:

```bat
cmd /c experiments\physx333-offline\level_graph\Build-Check.cmd
```

This is a **fresh-scene control, not a rewind test**. It uses the PhysX public
API to create a synthetic Story 1-1 f444→f445-shaped scene. No Unity or game
binary is loaded, no source mirror is rebuilt, and no shared restorer is
modified. It reads source-private state only to check what the public API
actually produced.

## Graph under test

Eight static actors have one box shape each. Four dynamic chef actors have a
capsule each; the moving chef also has an auxiliary box shape. A fifth active
dynamic actor is isolated. Two long static boxes contact all four chef
capsules. Four short static boxes have broadphase/contact interactions only
with the moving chef's main capsule. The moving auxiliary box overlaps two of
those four boxes with `eSUPPRESS` filtering, yielding two inactive marker
interactions. Two trigger static boxes overlap both moving shapes. The
synthetic transforms put the last four contact boxes and the main-shape
trigger overlaps just across the z separation boundary: moving that chef
from z=0 to z=-0.2 removes them, while the auxiliary trigger and marker
overlaps survive. Actors and shapes remain allocated throughout.

The filter and geometric layout intentionally encode **interaction topology**,
not game mesh dimensions or Unity Transforms. Actor IDs 1–2 are common contact
statics, 3–6 are disappearing contact statics, 7–8 are trigger statics, 9 is
the moving chef, 10–12 are other chefs, and 13 is the isolated active body.
The moving chef's shape 0 is the contact capsule and shape 1 is the auxiliary
box. In the shipped graph, the fifth active dynamic has no recorded
interactions; its object identity and geometry are unknown.

| Settled image | Contact SIPs | Trigger interactions | Markers | Contact manifolds / island edges | Touching/report ActorPairs | TransformCache live IDs / refs | AABB deleted overlaps |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| A, two established steps | 12 | 4 | 2 | 12 / 12 | 10 / 10 | 10 / 24 | 0 |
| B, moving chef z=-0.2 | 8 | 2 | 2 | 8 / 8 | 8 / 8 | 6 / 16 | 6 |

At A, all 12 contact-manager manifolds are the 240-byte capsule/box large
kind and no sphere-manifold slots are used. The two nontouching contact pairs
have no report object. All five dynamic bodies remain in the active-body
array. The ten live TransformCache IDs are the four chef main capsules and
six contact statics; the two trigger statics, moving auxiliary shape, and
isolated shape have no contact-cache reference. The `24` references are two
endpoints per 12 contact managers.

The B AABB manager retains six ordered deleted-overlap rows. Their **semantic
set** is the four moving-capsule/extra-box contacts and two
moving-capsule/trigger-box pairs. The measured order in this synthetic scene
is `contact 5, contact 3, trigger 8, trigger 7, contact 6, contact 4` by
static actor ID. The shipped observation has a different type sequence:
`contact A10, contact A9, contact A8, trigger A12, contact A7, trigger A11`.
The test checks the six semantic keys and fresh-scene ordered equality, but
does **not** claim the synthetic order reproduces the shipped one. The B
callback stream has two lost triggers, two lost touching contacts, and eight
persistent contacts, all checked in fresh-scene order; the two nontouching
contact pairs have no lost-touch callback.

## What the test proves and does not prove

The executable captures the full source Oracle, an ordered scene/per-actor
interaction graph, SAP deleted-overlap keys, TransformCache refcounts,
manifold pool use, island edge bindings, and ordered callbacks at A and B.
It constructs a second scene with the same public API call trace, requires
the expected graph/counts, and compares all those images and callback orders
between the two scenes. This establishes a deterministic baseline for a
later **joined restore** with shared endpoints and auxiliary interactions.
No rewind or source-private writes occur here.

The most important known deviations from the f444 image are:

- The synthetic A TransformCache has current ID 10, with no historical free
  IDs. The shipped checkpoint has current ID 13, ten live IDs, 24 references,
  and free-ID order `[12,11,10]`. Thus live topology/refcounts match, but
  allocation history does not.
- The synthetic six-deletion order differs from the shipped order above.
  Exact SAP ordering depends on the scene's insertion and motion history;
  this fixture asserts its own order is repeatable, not that it is the game
  order.
- Synthetic capsule/box dimensions, poses, auxiliary geometry, and contact
  distances are invented to realize the graph. The game artifact gives
  geometry *types* and connectivity, not every world-space bound or body
  state at f444.
- The fixture has exactly 13 rigid actors. The shipped artifact enumerates
  five active dynamics and eight static actors **in the interaction graph**;
  it does not establish that those are all actors in the game scene.
- The source-built fixture does not establish parity of the game's remaining
  broadphase pair table, scene-query trees, CCD/kinematic/solver settings,
  raw SAP handle history, or Animator/gameplay state. Its 23→17 active SAP
  pairs include physical overlaps rejected by the filter and cannot be
  compared to an absent game SAP snapshot.

This baseline should be extended with the already separate trigger/marker
restore and then with full graph-aware rewind only after its A checkpoint
images agree; a passing fresh-scene comparison alone is not rewind parity.
