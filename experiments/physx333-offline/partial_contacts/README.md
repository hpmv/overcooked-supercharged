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

The existing six-contact restore cannot consume this fixture as-is:

1. `NPhaseTopology::fixtureShapes` requires exactly seven actors, six dynamic
   shapes, and six static shapes. The source DLL bridge requires exactly six
   requests and an empty successor interaction scene. Here there are thirteen
   actors and eight surviving interactions at B. Generalize identity mapping
   and bridge preflight to a requested subset, then use the source
   `onOverlapCreated` lifecycle only for the four missing pairs.
2. The eight surviving SIP/CM/ActorPair/edge objects must retain their physical
   bindings and order. A restore must merge four recreated pairs into the
   target InteractionScene and mover interaction order, while also rebuilding
   persistent contact event and report lists. A blind reverse creation into a
   nonempty scene leaves the new four appended after the eight survivors.
3. The missing pairs occupied original SIP/CM/edge slots 8 through 11. Their
   free-list/LIFO order at B differs by pool. Recreating in target slot order
   needs to be derived from each pool's actual next-allocation rule and
   validated against the target image. A particular six-pair reverse loop is
   insufficient as a general rule.
4. The joined image must capture both surviving and resurrected contact
   payloads and pointer bindings. Existing `InteractionImage` uses fixed
   six-entry arrays and requires every fixture pair to be present, while its
   metadata stage assumes six persistent/report pairs and zero report cursor.
   Preserve survivor object identity, report/touch predecessor data, and the
   contact memory-block allocations before publishing the restored island,
   SAP, body, context, and clock images.

The default run is a deterministic public-only red test and component-state
inventory. The shipped Story 1-1 trace has a distinct broadphase pattern
(zero created/six deleted), so this fixture is not an exact scene
reconstruction.

For the bounded source-lifecycle subset stage, run:

```bat
cmd /c experiments\physx333-offline\partial_contacts\Build-Check.cmd --subset-probe
```

This separate fail-stop mode captures A's NPhase pair keys and physical SIP/CM
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
existing island restore closes the next difference: the complete source
Oracle and separate island image match A exactly. The cold fixture then
stops at a strict SAP preflight. At A,
`AABBManager::mDeletedPairsSize/Capacity` are `0/0` and its buffer pointer
is null; after B they are `4/32` with a new allocation. The same-allocation
SAP restore correctly rejects the changed capacity before writing. No
post-restore physics step occurs in this cold mode.

A separate high-water control first runs a 12-to-8-to-12 warm-up cycle, so
the broadphase deletion-output buffer exists at both A and B. Run it with:

```bat
cmd /c experiments\physx333-offline\partial_contacts\Build-Check.cmd --subset-warm-probe
```

In that control the full source Oracle and the SAP, island, transform-cache,
shape-cache-binding, body, scene-clock, context, query, and contact-memory
images all match checkpoint A after restore. The next B step reproduces
ordered contact callbacks, the full Oracle, and every listed component image.
This high-water condition is a control, not a fix for the cold fixture's
allocation-lifetime problem. Repeated replay is separately tested x100.

The real Story 1-1 checkpoint has twelve capsule/box managers with zero
current contact points; ten pairs still have the touch bit and a report
object, while two do not. It also has trigger and marker interactions. This
fixture instead uses twelve touching box/box pairs and gates out triggers and
markers; those level features require separate source-built coverage.
