# What is still missing to reproduce the shipped f444 scene offline

The source-built joined fixtures validate rewind mechanisms against a
level-*shaped* interaction graph. They do not contain the game's complete
live PhysX scene at checkpoint f444. This note separates the evidence we
already have from the one-time native inventory a level-matched source test
would require. It does not request a game run during the current offline
development track.

## Evidence already available

- The serialized Unity scene extraction at
  `M:/projects/game-test-2/artifacts/unity-2017-physics-repro/story11-physics-scene-v1.json`
  records 135 hierarchy objects and 133 physics components: four rigidbodies,
  123 boxes (39 triggers), five capsules, and one mesh collider. It includes
  local transform ancestry, serialized geometry, enabled/trigger flags,
  layers, and PhysicMaterial GUID references. It is an *initial serialized
  scene*, not the live f444 scene or its construction history.
- The `story11-full-scene-summary-v1.json` reproduction made from that
  geometry rewound bit-identically in Unity 2017.4.8f1. Its four plate-proxy
  rigidbodies were additions in the reproduction, not evidence of the
  game's total f444 actor count.
- The f444 interaction-graph and contact-owner sidecars under
  `framework/artifacts/readiness-plan-interaction-graph-f1048-to-f444-r1/`
  identify 12 contact, four trigger, and two marker interactions; five
  ordered active bodies; eight static interaction endpoints; the twelve
  contact owners; and relevant native pointer/slot identities. The 13 actors
  enumerated by that graph are **not** a complete `PxScene` actor census.
- The transform/broadphase sidecars under
  `framework/artifacts/readiness-plan-transform-broadphase-f1048-to-f444-r1/`
  establish the f444 transform-cache ledger (`currentId=13`, ten live IDs,
  24 references, free-ID LIFO `[12,11,10]`) and the f445 six-deletion order.
  Island, report, and manifold sidecars cover additional predecessor state.

## One-time capture needed for a truly level-matched source scene

At one pause-fenced, settled f444 boundary, read out every live native
`PxScene` actor and attached shape, including actors with no interaction in
the captured graph. Preserve the Unity Collider/Rigidbody mapping and, for
each shape, complete geometry, local/world pose, flags, filter data, contact
offset, and material coefficients. Record every dynamic body's construction-
ready mass/inertia, pose, velocity, sleep/kinematic/CCD state; scene descriptor
and filter settings; and the entire relevant SAP and scene-query images with
their element handles, pair/order tables, free lists, and live tree storage.

A final-state dump cannot recover insertion and activation history. A compact
one-time order log from fresh level load through f444 is also needed for actor
and shape creation/attachment, activation, relevant property calls, and
destruction. This is a **bounded capture design**, not a request for per-frame
full dumps or for searching by level reload. Until it exists, a source-built
fixture can prove component algorithms and deliberately matched subgraphs,
but cannot establish complete PhysX-only rewind parity for the actual level.

The current `level_graph` fixture invents several poses, dimensions, filters,
and actor histories. Its shipped-order option matches the `C,C,C,T,C,T`
deletion *type* sequence only. Matching the cache ledger in
`joined_cache_history` also perturbs other native histories. Neither is a
substitute for the full live inventory above.
