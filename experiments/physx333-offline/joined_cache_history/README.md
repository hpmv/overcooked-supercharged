# Joined source-built cache-history rewind diagnostic

Run from the framework root after building the isolated `joined_topology`
source bridge:

```bat
cmd /c experiments\physx333-offline\joined_cache_history\Build-Check.cmd
```

This fixture extends the synthetic 12-contact/4-trigger/2-marker level graph
without modifying pinned PhysX source, Unity, or the game. It uses public
PhysX calls to attach four temporary box shapes to an existing static actor,
steps after each attachment, then detaches them in the order 12, 11, 10, 13
with a step after each. Their roles and geometry match the earlier
`level_arena --cache-history` diagnostic. At settled checkpoint A it requires
transform-cache `currentId=13`, ten live IDs, 24 references, and free-ID LIFO
`[12,11,10]`, together with the original 12/4/2 graph, twelve contact
managers/manifolds/island edges, and ten touching/report-owning pairs.

The build check runs two **independent fresh-process** traces under that
ledger. One tests direct `A -> B -> restore A -> replay B`; the other tests
`A -> no-input successor -> B -> restore A -> no-input successor -> replay B`.
Each repeats its complete component-based rewind and continuation 100 times.
The no-input step applies no fixture pose, velocity, or wake setter after the
full restored-A readback. Both traces use the same guarded order: native
contact/trigger topology, report/touch ownership, all twelve WorkUnits and
PCM payload, contact memory blocks, island, SAP, transform cache, ShapeSim
cache IDs, bodies, scene clock, context, and scene query. Full stopped A,
next-step component images, graph, allocation-dependent facts, and ordered
callbacks must match; a rejected stage exits without simulating the partial
scene. No existing restore guard or comparator is relaxed.

The two traces are separate because an attempted *later* no-input checkpoint
after 100 direct A/B cycles reached a query-pruner phase rejected by the
existing `CaptureQueryImage` guard (`dynamic has unsupported rebuild, bucket,
or commit state`). That longer history is **not** covered by this passing
gate. The public warmup also changes NPhase, island, SAP, query, scene-clock,
actor/shape-ID, and allocator histories; matching three cache-ledger facts is
not a claim that the full synthetic scene equals shipped game frame 444.
Actual geometry and body state, additional game actors, Unity's static ABI,
and unsupported CCD/constraint/articulation/actor-lifetime paths remain open.
