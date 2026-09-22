# Auxiliary PhysX interaction image

`AuxInteractionImage` is a read-only, source-built observer for the trigger
and marker branch of a stopped PhysX 3.3.3 scene. It captures normalized
actor/shape identities, physical trigger/marker pool slots and ordered free
chains, trigger flags and prior-touch state, ordered per-type scene arrays,
and every rigid actor's mixed interaction order. Contact pairs appear only as
keys in those order arrays; their contact payload is owned elsewhere.

The image intentionally does not contain allocation addresses, C++ padding,
or the uninitialized `TriggerCache.dir` and `gjkState` members. The pinned
source overlap callbacks for box/box and capsule/box (in either stored shape
order) ignore the cache. `TriggerCache.state` is initialized to DISJOINT and
must still equal DISJOINT when captured. Other trigger geometry combinations
fail closed until their cache read/write semantics have been audited. The
observer has no restore API.

The integration test is the sibling `trigger_marker` executable. Run
`cmd /c experiments\physx333-offline\trigger_marker\Build-Check.cmd` from
the repository root. The test checks fresh-scene A/B image equality, exact
12/4/2 to 8/2/2 interaction deltas, ordered callbacks, and the trigger and
marker pool changes. This fixture is deliberately simpler than the shipped
level's multi-chef capsule and auxiliary-shape layout.

The same executable also runs an isolated capsule/box trigger test with the
trigger flag first on the static box and then on the dynamic capsule. Each
touching pair remains a broadphase pair after moving to a geometrically
separated pose, so the test checks a touch-found event, a touch-lost event,
prior-touch state 1→0, and DISJOINT cache state in both images. Two fresh
scenes must produce identical images and callbacks. A direct overlap-callback
check seeds different `dir`, `state`, and `gjkState` values and confirms that
neither pose reads or changes the cache.
