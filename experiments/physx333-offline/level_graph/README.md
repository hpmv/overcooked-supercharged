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

| Settled image | Contact SIPs | Trigger interactions | Markers | Contact manifolds / island edges | ActorPair / report-data pool used | TransformCache live IDs / refs | AABB deleted overlaps |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| A, two established steps | 12 | 4 | 2 | 12 / 12 | 12 / 10 | 10 / 24 | 0 |
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
It also captures the complete read-only `AuxInteractionImage` at both
boundaries: physical trigger and marker pool slots and free-list order,
oriented shape endpoints, trigger flags/previous-touch/cache state, scene
active prefixes and capacities, and the mixed interaction order and reverse
indices for every actor. The test checks that these oriented rows agree with
the canonical interaction graph, including trigger-first shape orientation,
four touching triggers at A, two touching survivors at B, and two inactive
markers at both boundaries. It compares the entire auxiliary images between
independent fresh scenes, including pool allocation history and order.
Across A→B it also requires each surviving trigger and marker to retain its
physical pool slot, both lost main-shape triggers to return their slots to
the trigger free chain, and the marker pool to remain unchanged.
Capsule/box and box/box trigger overlap callbacks in this source version
ignore the uninitialized cache direction/GJK fields, so those bytes are
deliberately excluded from the auxiliary image.

The fixture also captures a read-only `ActorPairGraphImage` at A and B. Every
contact SIP is checked against its canonical chef/static actor pair and
physical ActorPair pool slot. The twelve A contacts have twelve distinct
ActorPairs; ten touch and own report data. B retains eight contacts,
eight ActorPairs, and eight report-data objects. The settled report set is
empty at both boundaries, so every live ActorPair has exactly one SIP owner
and reference. The image includes native actor A/B orientation, report fields,
physical used slots, and exact free-list order for both pools. The fixture
checks that all surviving ActorPairs and report objects keep their slots,
the four removed ActorPair slots and two removed report slots return to their
free chains, and the full A/B images equal those of a second fresh scene.

At every stopped boundary, the fixture now also retains the **complete
existing component images** for SAP, TransformCache, ShapeCacheBindings,
Island, Body, SceneClock, Context, Query, and the memory-block pool. It takes
an immediate second capture of each image and requires its full same-scene
equality method to pass. The memory-block capture uses one identity registry
per scene across all steps, and both settled A/B images must report no
unsupported memory-block ownership. These checks establish that the images
can be captured coherently for this mixed, multi-actor scene; they do not
restore any of the captured state.

Independent fresh scenes are compared at three levels:

| Image | Fresh-scene comparison |
| --- | --- |
| Oracle, auxiliary trigger/marker, ActorPair | Their complete portable equality methods, including source-defined ordering and physical pool slots. |
| TransformCache | Current ID, array capacities, live transform and reference-count prefixes, and used free-ID prefix. Allocation addresses and unused capacity tails are excluded. |
| ShapeCacheBindings | Sorted shape-ID/transform-cache-ID pairs. ShapeSim addresses are excluded. |
| Memory-block pool | Stream selectors, counters, scratch metadata, named array sizes/capacities and block-identity order. Block payload bytes are excluded. |
| SAP, Island, Body, SceneClock, Context, Query | Full same-scene repeated-capture equality only. Their image formats retain scene pointers, allocations, or address-bearing payloads and do not define a portable full-image comparator. Existing graph, callback, and component-fact checks provide a narrower fresh-scene control for these systems. |

A trial of full memory-block equality across the two fresh scenes failed at
`blocks[0].bytes[800]`. That byte is retained and checked within each scene,
but its meaning has not been established, so the test does not ignore it in
a claimed full cross-scene comparison. A later restorer must compare the
complete target image in the **same scene** after rewinding.

It constructs a second scene with the same public API call trace, requires
the expected graph/counts, and compares the portable images and callback
orders between the two scenes. This establishes a deterministic baseline for
a later **joined restore** with shared endpoints and auxiliary interactions.
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
- The ActorPair and auxiliary images are capture-only controls. The joined
  restorer does not yet recreate contact and trigger lifecycles for this
  shared-endpoint graph or restore their pool order and report state.

The next step is to add source-private restore for this joined graph and
require A checkpoint and B successor images to match. A passing fresh-scene
comparison alone is not rewind parity.
