# Auxiliary PhysX interaction image

`AuxInteractionImage` is a read-only, source-built observer for the trigger
and marker branch of a stopped PhysX 3.3.3 scene. It captures normalized
actor/shape identities, physical trigger/marker pool slots and ordered free
chains, trigger flags and prior-touch state, ordered per-type scene arrays,
and every rigid actor's mixed interaction order. Contact pairs appear only as
keys in those order arrays; their contact payload is owned elsewhere.

The image intentionally does not contain allocation addresses, C++ padding,
or the uninitialized `TriggerCache.dir` and `gjkState` members. In the current
box/box trigger fixture, the source overlap callback ignores the cache;
`TriggerCache.state` is initialized and captured as a guarded scalar.
Other trigger geometry combinations fail closed until their cache read/write
semantics have been audited. The observer has no restore API.

The integration test is the sibling `trigger_marker` executable. Run
`cmd /c experiments\physx333-offline\trigger_marker\Build-Check.cmd` from
the repository root. The test checks fresh-scene A/B image equality, exact
12/4/2 to 8/2/2 interaction deltas, ordered callbacks, and the trigger and
marker pool changes. This fixture is deliberately simpler than the shipped
level's multi-chef capsule and auxiliary-shape layout.
