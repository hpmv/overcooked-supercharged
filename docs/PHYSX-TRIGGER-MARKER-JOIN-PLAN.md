# Offline trigger/marker rewind join plan

This is the next source-built PhysX 3.3.3 parity step after the contact-only
capsule/box 12→8 rewind. It is not a game run or a Unity integration plan.
The [standalone baseline](../experiments/physx333-offline/trigger_marker/README.md)
already proves that public PhysX calls can make the level-shaped settled
interaction counts `12 contact / 4 trigger / 2 marker` and successor counts
`8 / 2 / 2`, with four contact and two trigger losses in deterministic order.
The [capsule contact fixture](../experiments/physx333-offline/partial_contacts/README.md)
already rewinds the four contact removals, including the ten-touch/two-null-
report pattern, for cold and warm 100-cycle next-step replays. Neither fixture
yet rewinds trigger or marker state.

The interaction graph in the shipped checkpoint has a more specific topology
than either fixture: four contact chefs plus one zero-interaction active body,
eight static actors, and eighteen interactions total. The moving chef has a
main capsule with six contact pairs and an auxiliary shape. Two static
trigger shapes each overlap **both** chef shapes at A, making four trigger
interactions. The two rows on the main capsule are deleted at B; the two on
the auxiliary shape survive. Two marker interactions also involve that
auxiliary shape and two of the static boxes from disappearing contact pairs;
those markers survive. The current one-mover fixtures reproduce counts and
contact-slot ownership, not this actor/shape graph. A joined rewind on the
simplified graph is an intermediate step; exact level-shaped ownership needs
a separate fixture and replay gate.

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
  `Gu::TriggerCache` direction/state/GJK state. A newly constructed trigger
  starts without previous touch history; restoring only its public pair
  flags cannot reproduce a loss notification. Markers have no derived
  payload beyond their base interaction linkage but still need identity,
  slot, and order verification.
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

Add a separate, preflighted source bridge to recreate the two missing trigger
pairs through original `NPhaseCore::onOverlapCreated`, preserving the two
survivor triggers and both survivor markers. Verify filter results, shape
flags, expected physical trigger slots, and free-list head order before the
first write. After contact and trigger lifecycle creation, restore unified
scene and actor interaction order and reverse indices, then trigger history
and pool state. Markers should remain the same objects in this fixture; no
marker reconstruction is required for the 12/4/2→8/2/2 transaction.

The existing SAP image appears structurally able to roll back six deleted
overlaps: all actor/shape AABB elements survive, and the cold deletion-output
buffer grows to capacity 32 for either four or six outputs. The island graph
tracks contact-manager edges, not trigger/marker edges, so its contact restore
may remain valid. Both are hypotheses until the joined six-deletion fixture
passes full image readback and next-step replay.

## Verification order

1. Read-only A/B auxiliary image and fresh-scene equality in the existing
   trigger baseline, including physical slots, free chains, all actor
   arrays, and trigger history.
2. Before making restoration helpers more general, build a second fixture
   with the shipped four-chef/five-active-body, eight-static,
   two-shape-moving-chef graph. Require deterministic fresh-scene A/B
   images, 12/4/2→8/2/2 counts, and exact ownership of the four contact and
   two trigger deletions. Drive new restore logic against this graph; keep
   the simpler one-mover fixtures as regression controls.
3. Recreate only four missing contacts, then only two missing triggers,
   checking survivor identity and no marker mutation. Key pair lookup by
   actor/shape identity rather than contiguous mover shape indices.
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
