# Offline trigger/marker rewind join plan

This is the next source-built PhysX 3.3.3 parity step after the contact-only
capsule/box 12→8 rewind. It is not a game run or a Unity integration plan.
The [standalone baseline](../experiments/physx333-offline/trigger_marker/README.md)
already proves that public PhysX calls can make the level-shaped settled
interaction counts `12 contact / 4 trigger / 2 marker` and successor counts
`8 / 2 / 2`, with four contact and two trigger losses in deterministic order.
The [capsule contact fixture](../experiments/physx333-offline/partial_contacts/README.md)
already rewinds the four contact removals, including the ten-touch/two-null-
report pattern, for cold and warm 100-cycle next-step replays. A separate
[trigger rewind](../experiments/physx333-offline/trigger_rewind/README.md)
reconstructs two trigger removals with a surviving trigger and marker; none
of these component restorers has yet joined contacts and auxiliary pairs.

The interaction graph in the shipped checkpoint has a more specific topology
than either fixture: four contact chefs plus one zero-interaction active body,
eight static endpoints, and eighteen interactions total. These thirteen
actors are the graph's enumerated active bodies and interaction endpoints,
**not** a proven total of actors in the Unity PhysX scene. The twelve contact
pairs share just ten contact ShapeSims (four chef capsules and six static
boxes), with 24 transform-cache references and ten live IDs; the source-built
one-mover fixture instead uses 24 distinct contact shapes. The moving chef has a
main capsule with six contact pairs and an auxiliary shape. Two static
trigger shapes each overlap **both** chef shapes at A, making four trigger
interactions. The two rows on the main capsule are deleted at B; the two on
the auxiliary shape survive. Two marker interactions also involve that
auxiliary shape and two of the static boxes from disappearing contact pairs;
those markers survive. The current one-mover fixtures reproduce counts and
contact-slot ownership, not this actor/shape graph. A joined rewind on the
simplified graph is an intermediate step; exact level-shaped ownership needs
a separate fixture and replay gate.

The target `finishBroadPhase` observer orders its six deleted overlaps as
contact with graph static A10, contact A9, contact A8, trigger A12, contact
A7, trigger A11. The four contact removals release island edge IDs 8–11.
These graph labels are checkpoint-local names, not portable actor IDs; the
order is a fixture target, while raw pointer values from the game are not.

## Retained state to image

- Capture trigger and marker interactions in their ordered scene arrays,
  active prefixes, physical pool slots, and used/free LIFO chains. The
  current source Oracle records type counts and pool free chains, but not
  each trigger/marker object's semantic fields.
- Capture shape/actor identity and scene/actor reverse indices for every
  auxiliary interaction, plus ordered `Sc::Actor::mInteractions` arrays for
  the mover and each touched static actor. Source destruction swaps entries
  with the last entry, so the mixed contact/trigger/marker order can change.
- For each trigger retain `mFlags`, `mLastFrameHadContacts`, and the
  initialized `Gu::TriggerCache::state`. The capsule/box and box/box overlap
  callbacks ignore the cache's indeterminate `dir` and `gjkState` fields;
  do not read or image those fields for these geometries. A newly
  constructed trigger starts without previous touch history; restoring only
  its public pair flags cannot reproduce a loss notification. Markers have
  no derived payload beyond their base interaction linkage but still need
  identity, slot, and order verification.
- Key each interaction by type and canonical unordered shape endpoints, but
  retain its **oriented** shape0/shape1 order for filter and work-unit
  reproduction. Key ActorPair ownership separately by canonical actor
  endpoints: PhysX can share one ActorPair across multiple shape pairs from
  the same two actors. A shape-index-keyed map can silently collapse or
  duplicate its refcount, touch count, report object, and physical slot.
  Account separately for the reference held by the contact-report set. A
  deleted pair may leave an ActorPair in that set *during* a step, but normal
  completed `fetchResults` clears the set and destroys report-only owners.
  Require that settled-phase condition before graph restoration; do not
  generalize it to a mid-step checkpoint.
  A separate [source-built ActorPair graph observer](../experiments/physx333-offline/actor_pair_graph/README.md)
  captures physical AP/report pools and validates two SIPs sharing one AP
  through 2→1→0 ownership. The level-graph fixture now includes its full
  A/B ActorPair images and fresh-scene equality, but it has no ActorPair
  restore path.
- Check the scene trigger-report buffers at settled `fetchResults`; initially
  gate on empty logical sizes and unchanged backing, then add a guarded
  buffer image if the fixture proves that retained capacity/order matters.

## Integration boundaries

The present [NPhase topology helper](../experiments/physx333-offline/nphase/NPhaseTopology.cpp)
and [interaction image](../experiments/physx333-offline/interaction/InteractionImage.cpp)
reject nonzero trigger/marker counts and assume the mover's interaction array
contains only contact pairs. The [source lifecycle bridge](../experiments/physx333-offline/nphase/NPhaseBridge.cpp)
and [report bridge](../experiments/physx333-offline/interaction/InteractionReportBridge.cpp)
also gate them out. Generalize those guards only when a companion auxiliary
image validates the exact expected types and identities. Keep the contact
pair reconstruction contact-only; do not let trigger indices masquerade as
SIP indices in actor or scene arrays.

The existing `AuxInteractionImage` observes mixed scene/actor order and
trigger pool/history. Its trigger-cache policy now accepts box/box and
capsule/box in either orientation. A pinned-source audit found that these
overlap callbacks ignore the cache's `dir` and `gjkState`: the trigger
constructor/initialize path sets only `state = TRIGGER_DISJOINT`, while the
other fields are indeterminate for these geometries. The observer therefore
checks only initialized `state`, never reads or compares those other fields,
and still checks trigger flags and `mLastFrameHadContacts`.

The isolated bridge now recreates two missing trigger pairs through original
`NPhaseCore::onOverlapCreated`, preserving a trigger and marker survivor.
The next joined bridge must preserve **two** survivor triggers and both
markers while also recreating four missing contacts. Verify filter results,
shape flags, expected physical pool slots, and free-list head order before
the first write. After contact and trigger lifecycle creation, restore
unified scene and actor interaction order and reverse indices, then trigger
history and pool state. Markers should remain the same objects in this
fixture; no marker reconstruction is required for the 12/4/2→8/2/2
transaction.
An [isolated two-trigger deletion rewind](../experiments/physx333-offline/trigger_rewind/README.md)
now validates the native trigger lifecycle and exact cold/warm replay both
without survivors and with one surviving trigger plus one marker. It restores
mixed per-actor order and physical trigger slots in the latter case. Neither
variant has contact pairs, so joining contact and auxiliary restoration in
the level graph remains the next integration gate.
The four missing contact pairs each have a distinct static actor in the
level-like graph, so each should receive a distinct ActorPair; surviving
markers on two of those actor endpoints do not count as SIPs for source
`findActorPair` reuse. Verify the four expected AP allocations and their
report ownership rather than inferring pool use from the SIP count.

The existing SAP image appears structurally able to roll back six deleted
overlaps: all actor/shape AABB elements survive, and the cold deletion-output
buffer grows to capacity 32 for either four or six outputs. The island graph
tracks contact-manager edges, not trigger/marker edges, so its contact restore
may remain valid. Both are hypotheses until the joined six-deletion fixture
passes full image readback and next-step replay.

The source-built level graph currently starts its TransformCache at current
ID 10 with no free IDs, unlike shipped f444 (current ID 13 and free-ID LIFO
`[12,11,10]`). Source `Cm::IDPool` semantics show that reproducing that
history from the synthetic baseline requires four concurrent temporary IDs
10–13, then releasing them in order 12, 11, 10, 13: the last release lowers
the high-water mark. Three temporary IDs cannot produce this free stack.
Publicly creating and detaching four extra contact shapes could test this,
but it also changes NPhase, island, SAP, query, actor/shape ID, clock, and
allocator history. Treat such a warmup as a separate diagnostic, not a
neutral adjustment to the main fixture or proof of game-state equality.

## Verification order

1. Read-only A/B auxiliary image and fresh-scene equality in the existing
   trigger baseline, including physical slots, free chains, all actor
   arrays, and trigger history.
2. Before making restoration helpers more general, build a second fixture
   with the shipped four-chef/five-active-body, eight-static-endpoint,
   two-shape-moving-chef graph. Require deterministic fresh-scene A/B
   images, 12/4/2→8/2/2 counts, and exact ownership of the four contact and
   two trigger deletions, plus the shared 10-shape/24-reference contact
   topology. **Fresh-scene baseline now passes** in
   [level_graph](../experiments/physx333-offline/level_graph/README.md).
   Its full component images also pass immediate same-scene recapture. A
   separate fixed-address arena diagnostic replays this graph's five-step
   suffix and a non-adjacent suffix with full image equality, but the
   component restorer has not been joined. The main fixture does not yet
   reproduce the game's historical TransformCache free-ID chain or ordered
   SAP deletions. Drive new restore logic against this graph; keep the
   simpler one-mover fixtures as regression controls.
3. Recreate only four missing contacts, then only two missing triggers,
   checking survivor identity and no marker mutation. Key pair lookup by
   actor/shape identity rather than contiguous mover shape indices.
   **Topology-only joined reconstruction now passes** in
   [joined_topology](../experiments/physx333-offline/joined_topology/README.md):
   it recreates all six missing pairs, restores their physical SIP/ActorPair/
   trigger slots and unified scene/actor order, and rejects seven malformed
   plans with complete B-state nonmutation. It does not restore contact
   reports, manifolds, or other checkpoint components.
4. Restore unified interaction order and trigger history, then contact
   reports, memory, island, SAP, body, cache, clock, context, and query
   images as one stopped-scene transaction.
5. Require the full implemented Oracle, every component image, and ordered
   callbacks at checkpoint A and successor B for 100 cold and 100 warm
   rewinds. Reject bad identities, geometry/filter flags, pool slots, and
   free-chain requests before mutation. Keep fail-stop behavior for any
   postwrite mismatch.

This still would not establish unrestricted PhysX-only parity. Actor/shape
lifetime in the component restorer, CCD, multi-manifold contacts, other pair
filters, and Unity's statically linked layout remain separate gates.
