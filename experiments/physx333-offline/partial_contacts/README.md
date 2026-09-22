# Offline PhysX 3.3.3 partial-contact baseline

Run from the framework root after building the pinned Win32 source mirror:

```bat
cmd /c experiments\physx333-offline\partial_contacts\Build-Check.cmd
```

This executable creates a scene using the public PhysX API only. One dynamic
rigid actor has twelve box shapes. Each shape faces a separate static box.
Shapes 0 through 7 are deeply inside their static boxes' z ranges; shapes 8
through 11 have only 0.1 units of z overlap. Two settled steps at z=0 establish
twelve touching interactions. Moving the whole dynamic actor to z=-0.2 on the
next step removes the last four contacts while preserving the first eight.
No geometry or actor is created or destroyed after setup. The filter requests
found, persistent, and lost notifications.

The test reads the existing source Oracle and the SAP and island component
images at checkpoint A and successor B. It verifies a second capture at each
boundary is identical. It constructs a second fresh scene, follows the same
call trace, and compares the complete 39-section semantic Oracle plus the
ordered contact callbacks at A and B. It also restores only public body fields
after B and repeats the B step as a negative control.

Observed with the pinned `3.3.3-1.3.3` source build:

- A has twelve active shape pairs, low-level managers, and contact edges.
  The pairs' SIP, manager, and island-edge slots are 0 through 11.
- B preserves pairs 1 through 8. Pairs 9 through 12, corresponding to mover
  shapes 8 through 11, disappear. The ordered B callback stream reports four
  `TOUCH_LOST` rows in static-ID order 9, 10, 11, 12, followed by eight
  `TOUCH_PERSISTS` rows in order 1 through 8.
- The B shape-pair and island-edge free lists start 11, 10, 9, 8. The contact
  manager free array ends 8, 9, 10, 11, and its allocator pops from the end,
  so the next manager slot is 11. The executable prints both ends of each
  free list and SAP counts.
- At settled boundaries, SAP active pairs go from twelve to eight, and its
  `createdPairsSize` and `deletedPairsSize` buffers are both zero because the
  update's transient outputs have already been consumed. This fixture deletes
  four broadphase pairs; the shipped trace's six-deletion output is a distinct
  target for a later fixture.
- Repeating B after a public body restore yields only the eight persistent
  callbacks. It loses all four `TOUCH_LOST` notifications.

The twelve-pair restore extends the original six-contact path in four ways:

1. The source NPhase bridge now creates only the four missing pairs through
   `onOverlapCreated`, retaining the eight survivor objects.
2. Scene and mover interaction arrays are reordered to A after creation;
   SIP, contact-manager, edge, and ActorPair physical slots are verified.
3. The mixed fixture's ActorPair free-node order differs from the required
   SIP/manager creation order. A preflighted stage permutes only four free
   links before source lifecycle allocation, retaining survivor objects and
   the untouched free-list tail.
4. All twelve work units, reports where owned, touch metadata, contact
   memory, islands, broadphase, bodies, caches, clock, context, and queries
   are restored and verified before simulation.

The default run is a deterministic public-only red test and component-state
inventory. The shipped Story 1-1 trace has a distinct broadphase pattern
(zero created/six deleted), so this fixture is not an exact scene
reconstruction.

For full joined rewind/replay from a cold checkpoint, run:

```bat
cmd /c experiments\physx333-offline\partial_contacts\Build-Check.cmd --subset-probe
```

This mode captures A's NPhase pair keys and physical SIP/CM
slots, checks that corrupt shape and physical-slot requests are rejected
without a scene change, then preflights and creates only the four missing B
pairs through the original
`NPhaseCore::onOverlapCreated` path. It verifies the ordered twelve-pair
topology and exact SIP/CM slot mapping. The report stage preserves all eight
survivor report objects and creates four missing report objects via the SDK's
lazy path; it restores all twelve ordered persistent events, SIP/ActorPair
touch metadata, and contact-manager bitmaps. The contact stage restores all
twelve work units and single box/box manifolds, with the saved contact-memory
blocks installed between binding and payload stages.

All `contact.*` and `nphase.*` source-Oracle sections then match A. The
island restore closes the next difference, and the complete source Oracle
matches A. The cold successor allocates new SAP deleted-overlap storage
(A `size/capacity=0/0`, null pointer; B `4/32`, new pointer), an empty
transform-cache free-ID array, and a progressive query-tree FIFO stack.
The narrowly guarded component restorers handle each allocation-lifetime
change. Malformed SAP images reject without mutation. Only after the full
checkpoint Oracle and every component image agree does the fixture replay B.
Ordered callbacks, the Oracle, and all component images match the original B
for 100 consecutive cold rewind/replay cycles.

A separate high-water control first runs a 12-to-8-to-12 warm-up cycle, so
the broadphase deletion-output buffer exists at both A and B. Run it with:

```bat
cmd /c experiments\physx333-offline\partial_contacts\Build-Check.cmd --subset-warm-probe
```

In that control the full source Oracle and the SAP, island, transform-cache,
shape-cache-binding, body, scene-clock, context, query, and contact-memory
images all match checkpoint A after restore. The next B step reproduces
ordered contact callbacks, the full Oracle, and every listed component image.
The high-water run is a separate regression control; the cold mode above
tests allocation-lifetime rollback directly. Both replay x100.

The real Story 1-1 checkpoint has twelve capsule/box managers with zero
current contact points; ten pairs still have the touch bit and a report
object, while two do not. It also has trigger and marker interactions. This
fixture instead uses twelve touching box/box pairs and gates out triggers and
markers; those level features require separate source-built coverage.

The separate `--mixed-baseline` mode uses twelve box/box broadphase pairs,
with rotated corner-gap geometry for mover shapes 8 and 10. Both pairs have
an overlap but no touch or report object. The other ten pairs touch. Its A
checkpoint therefore has twelve SIP/CM/ActorPair objects and exactly ten
report objects, with null reports at shapes 8 and 10. B removes shapes 8–11,
leaving eight pairs and reporting two ordered losses (static actors 10, 12)
followed by eight persistent contacts. Fresh-scene A/B Oracle and callbacks
match. `--mixed-subset-probe` performs the same full cold joined rewind and
replay x100; `--mixed-warm-subset-probe` is its separate high-water control.
This reproduces the checkpoint's mixed ownership pattern without yet claiming
capsule/box or trigger/marker coverage.
