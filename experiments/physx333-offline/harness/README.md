# Offline joined PhysX 3.3.3 rewind probe

The `--joined-replay-probe` mode exercises the pinned, source-built Win32
PhysX 3.3.3 SDK; it never starts Overcooked or the Unity player. Run from the
framework root:

```bat
cmd /c experiments\physx333-offline\harness\Build-Harness.cmd --joined-replay-probe
```

The fixture has six box contacts on one dynamic actor. It warms a checkpoint
(`A`), moves the actor away to delete all six contacts (`B`), and reconstructs
`A` in the **same scene**. The joined transaction runs the source NPhase
lifecycle in reverse physical-pool order, restores InteractionScene/ActorPair
order and report/touch metadata, installs contact-manager bindings, transfers
existing 16 KiB friction/cache blocks from the unused stack to their saved
owners, restores contact payload and PCM manifolds, then restores island, SAP,
transform-cache, per-ShapeSim transform-cache ID bindings, body, and
scene-clock images. No physics step occurs between
those stages. If any stage fails, this disposable process exits instead of
simulating a partial scene.

The probe checks the 39-section source oracle at the reconstructed checkpoint,
then simulates `A -> B` and compares the ordered contact callbacks, public
body/scene observations, the oracle, and SAP/cache/body/clock/island/block
images to the original step. It repeats the
rewind and next-step check 100 times, then compares a five-step suffix that
creates, maintains, and deletes contacts against an independently created
reference scene. These are reproducible fixture results, not yet complete
PhysX-only parity for a level or the Unity-shipped binary.

The extended next-step image check found a dependency missed by the original
oracle: restoring the transform-cache ID pool without its owning `ShapeSim`
ID fields produced a different free-ID order while public results still
matched. The [shape-cache binding image](../shape_cache/README.md) repairs
that dependency; the joined probe includes its corruption-rejection check.

Current boundaries:

- The bridge only recreates this six-pair box/box topology from an empty
  successor. It does not rewind arbitrary pair subsets, triggers, filter
  changes, shape/actor lifetimes, or allocation growth.
- The oracle explicitly marks four source-state categories unsupported:
  allocator/free-slot tails, NPhase filter/dirty state, constraints and
  articulations, and solver/friction backing not covered by the fixture.
- The fixture has no scene queries, CCD, particles, cloth, or kinematic actor.
  Those paths need their own source-backed images and replay matrices.
- Several component restorers require the same scene, allocation addresses,
  capacities, and actor/shape topology. Their guards reject changed topology;
  passing a component A/B test alone does not authorize a physics step.
- There is no atomic **whole-scene** rollback. The joined harness is a
  fail-stop offline experiment, not yet a production rewind framework.

The broader source-state inventory and remaining test matrix are in
[`docs/PHYSX-SOURCE-STATE-AUDIT.md`](../../../docs/PHYSX-SOURCE-STATE-AUDIT.md).
