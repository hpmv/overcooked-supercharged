# Offline trigger deletion rewind (PhysX 3.3.3)

Run from the framework root:

```bat
cmd /c experiments\physx333-offline\trigger_rewind\Build-Check.cmd
```

The build first makes a private copy of the existing pinned PhysX 3.3.3
source mirror in this directory's ignored `work/` tree. It adds one test-only
export and incrementally builds its own Win32 PhysX DLL. The pinned vendor
checkout, the shared source mirror, Unity, and the game are not modified or
loaded. `out-ninja/` is also ignored.

The scene has one live dynamic capsule and two live static box triggers. Two
settled steps establish A with two touching trigger interactions. At B the
dynamic moves outside both broadphase bounds, so both trigger objects are
deleted; the actors and shapes remain in the same scene. The bridge validates
the source cores, geometry, actor/shape ownership, filter result, empty
interaction graph, complete 32-slot trigger pool free chain, and the two
expected free-head slots before its first write. It arranges those two free
nodes into checkpoint allocation order, calls PhysX's original
`NPhaseCore::onOverlapCreated` lifecycle twice, and restores the two
trigger objects' settled flags, prior-touch bits, and cache states. A
postwrite mismatch aborts instead of allowing another physics step.

The executable restores the existing same-scene body, SAP, and scene-clock
images around that native interaction reconstruction. It requires full
equality with checkpoint A for those three component images, the complete
`AuxInteractionImage`, and the existing source Oracle. Four malformed inputs
are rejected before writes, after which the full B image is unchanged. It
then repeats B→A and the next B step 100 times for each of two histories:

- **Cold A:** the deleted-overlap result array has never grown.
- **Warm A:** an earlier public-api separation and return has already grown
  that array and reallocated the two trigger interactions.

Every cycle matches A exactly and matches the first B's full Oracle,
auxiliary, SAP, body, and scene-clock images plus the ordered touch-lost
callback stream. The two public API
setup steps are only for establishing the warm baseline; every measured
rewind uses the same-scene native bridge.

This is deliberately narrow. It covers exactly two deleted capsule/box
trigger pairs, no surviving contacts, triggers, or markers, one dynamic
actor, and two static actors, with `eTRIGGER_DEFAULT` and no filter callback.
It does not restore a mixed 12/4/2 interaction graph, arbitrary pool growth,
shared actor interaction arrays with survivors, or the game's scene. The
Oracle also documents unrelated low-level fields outside its image. The
experiment establishes a working source-native trigger reconstruction path,
not complete level rewind parity or an ABI-compatible Unity replacement.
