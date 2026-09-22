# Offline trigger deletion rewind (PhysX 3.3.3)

Run from the framework root:

```bat
cmd /c experiments\physx333-offline\trigger_rewind\Build-Check.cmd
```

The build first makes a private copy of the existing pinned PhysX 3.3.3
source mirror in this directory's ignored `work/` tree. It adds test-only
exports and incrementally builds its own Win32 PhysX DLL. The pinned vendor
checkout, the shared source mirror, Unity, and the game are not modified or
loaded. `out-ninja/` is also ignored.

The first scene has one live dynamic capsule and two live static box triggers. Two
settled steps establish A with two touching trigger interactions. At B the
dynamic moves outside both broadphase bounds, so both trigger objects are
deleted; the actors and shapes remain in the same scene. The v1 bridge validates
the source cores, geometry, actor/shape ownership, filter result, empty
interaction graph, complete 32-slot trigger pool free chain, and the two
expected free-head slots before its first write. It arranges those two free
nodes into checkpoint allocation order, calls PhysX's original
`NPhaseCore::onOverlapCreated` lifecycle twice, and restores the two
trigger objects' settled flags, prior-touch bits, and cache states.

The second scene keeps a third static box trigger overlapping the dynamic
capsule across both frames. A second box shape on that dynamic actor overlaps
a fourth static box; the filter suppresses that pair, so PhysX retains it as
a marker. A therefore has three triggers and one marker; B keeps the third
trigger and the marker while deleting the first two trigger objects. The v2
bridge preflights those survivor identities, geometry, filters, pool slots,
and array capacities. After native pair creation, it restores both the
per-type trigger scene order and the dynamic actor's mixed trigger/marker
interaction order, including their reverse indices. The test asserts that
the target orders differ from the order that merely appending the missing
pairs would produce, in both cold and warm cases. A postwrite mismatch
aborts instead of allowing another physics step.

The executable restores the existing same-scene body, SAP, and scene-clock
images around each native interaction reconstruction. It requires full
equality with checkpoint A for those three component images, the complete
`AuxInteractionImage`, and the existing source Oracle. The first case checks
four malformed inputs; the mixed case rejects malformed order, free-head,
and survivor-identity inputs. All are rejected before writes and leave the
full B image unchanged. Each case
repeats B→A and the next B step 100 times for two histories:

- **Cold A:** the deleted-overlap result array has never grown.
- **Warm A:** an earlier public API separation and return has already grown
  that array and reallocated the two trigger interactions.

Every cycle matches A exactly and matches the first B's full Oracle,
auxiliary, SAP, body, and scene-clock images, as well as its ordered
touch-lost callback stream. The public API setup steps are only for
establishing the warm baseline; every measured rewind uses the same-scene
native bridge. The original two-pair test and the mixed survivor test both
pass cold and warm histories independently.

This is deliberately narrow. Both cases cover exactly two deleted
capsule/box trigger pairs, one dynamic actor, `eTRIGGER_DEFAULT`, and no
filter callback. The mixed case covers one surviving trigger and one
surviving marker; it has no contact pairs. It does not restore the level's
12/4/2 graph, arbitrary pool growth, multiple dynamic actors, surviving
contacts, or the game's scene. The Oracle also documents unrelated low-level
fields outside its image. The experiment establishes a working source-native
trigger reconstruction path with mixed survivors, not complete level rewind
parity or an ABI-compatible Unity replacement.
