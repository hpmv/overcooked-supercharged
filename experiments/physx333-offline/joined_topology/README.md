# Offline joined NPhase topology experiment (PhysX 3.3.3)

Run from the framework root:

```bat
cmd /c experiments\physx333-offline\joined_topology\Build-Check.cmd
```

This test uses a private copy of the pinned Win32 PhysX 3.3.3 source build
under this directory's ignored `work/` tree. Its only new SDK export is a
test-only source-native interaction reconstruction function. The pinned
vendor checkout, shared source build, Unity, and the game are not modified
or loaded.

The public-API scene comes from `level_graph`: four chef capsules, an
auxiliary shape on the moving chef, an isolated active body, and eight static
box endpoints. Settled A has `12 contact / 4 trigger / 2 marker` interactions;
after moving one chef, B has `8 / 2 / 2`. Four capsule/box contacts and two
capsule/box triggers are deleted. The two surviving triggers share static
actors with the missing main-shape triggers; two surviving markers share
static actors with missing contacts. Thus scene and actor interaction arrays
have genuinely mixed survivor order.

The gate runs this reconstruction twice in independent scenes. The cold case
uses the first settled A/B pair. Before checkpoint A in the warm case, public
PhysX calls first traverse a complete `12/4/2 -> 8/2/2 -> 12/4/2` cycle,
then settle once more at A. Both cases run the same positive topology readback
and all seven atomic malformed-plan/nonmutation controls. This checks that
the test-only bridge does not depend on a first-use scene, but it does not
claim arbitrary allocation histories or full native rewind parity.

The bridge accepts only this exact fixed topology at a stopped scene. Before
writing, it validates all 18 source-core pair bindings and fixture filters,
survivor identities and pool slots, six missing role IDs, independent SIP,
ActorPair, trigger, contact-manager, and island-edge allocation heads, array
capacities, and every actor's requested mixed order. Manager indices and edge
IDs come from the settled A Oracle image, while the B free stack/chain is
validated using the source allocation rules (`PxcPoolList::get` pops the
stack end; `ElemManager::getAvailableElem` consumes the linked-list head).
Only those four free positions may be reordered, and the untouched free
tails are checked after reconstruction. It then uses PhysX's original
`NPhaseCore::onOverlapCreated` lifecycle for the four missing contacts and
two triggers, keeps all survivors and markers, restores per-type scene and
per-actor order/reverse indices, and restores initialized trigger history.
The harness checks malformed plans leave the complete B Oracle, auxiliary,
ActorPair, graph, and nine component images unchanged. A successful call
checks the A graph, full auxiliary image, physical SIP/ActorPair pool topology,
per-pair contact-manager indices and island-edge IDs, and selected
source-Oracle pool sections. The cold and warm fixtures already have the
needed manager/edge allocation order, so neither requires reordering; the
prewrite guards still check it independently.

The nine negative controls cover an absent contact core, a filter mismatch,
an invalid trigger slot, an impossible ActorPair slot, impossible missing
contact-manager and island-edge IDs, a wrong surviving trigger slot,
duplicate scene order, and duplicate actor order. Each is
rejected before any source write and checked against the complete B image.
The positive topology readback is intentionally narrower: creating contacts
leaves the island manager's change queues pending, so its complete image
cannot be captured at the same post-fetch boundary until the later full
restore stage. The full A Oracle's first remaining difference is in
`contact.managers`, consistent with unrestored contact payload.

This is a **topology-only** experiment. It does not restore the deleted
contacts' report objects, touch metadata, contact-manager work units,
persistent manifolds, contact-memory streams, the complete island graph, SAP, body state,
or other full checkpoint state. It does not simulate the successor after the
native reconstruction. Consequently it is not a full rewind, a game test,
or a Unity-compatible PhysX replacement. The synthetic scene also differs
from the game's allocation history and ordered SAP deletion sequence.
