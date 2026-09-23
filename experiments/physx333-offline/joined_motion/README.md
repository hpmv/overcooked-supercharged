# Joined source-built motion and friction rewind fixture

From the framework root, after building the isolated `joined_topology` source
bridge, run:

```bat
cmd /c experiments\physx333-offline\joined_motion\Build-Check.cmd
```

This uses source-built Win32 PhysX 3.3.3, not Unity or the game. It retains
the synthetic level graph's 12 contacts, 4 triggers, and 2 suppressed marker
pairs at checkpoint A, and the 8/2/2 deletion endpoint B. Its public-API
warmup enables gravity, gives three chefs tangential velocity, advances the
solver without fixture inputs, and then re-establishes the checkpoint pose.
A has nonzero solver-generated body velocities and ten contact WorkUnits with
live friction patches. The ordered contact/trigger callbacks are captured
as part of each complete image, including the mixed found/persist reports
caused by the warmup.

The build check runs three independent fresh-process histories, each with
100 complete rewind/replay cycles:

1. `A -> scripted deletion B -> restore A -> replay B` under the warmed
   gravity/friction state.
2. `A -> no-input motion C -> scripted deletion B -> restore A -> replay C/B`.
   The no-input step changes contact ownership before the fixture pose step.
3. `A -> two no-input moving boundaries -> no-input deletion B -> restore A
   -> replay all three steps`. This kinetic variant gives the moving chef a
   public-API dash velocity before simulating A; its measured checkpoint
   velocity remains below -4 m/s along Z. No pose, velocity, force, or wake
   setter is applied during the A-to-B continuation. Its synthetic auxiliary
   box is made taller through `PxShape::setGeometry` before the trace so its
   two surviving trigger pairs remain touching through the dash. The original
   narrower box produces `lastFrameHadContacts=0` at B and is rejected by
   the existing topology bridge's surviving-trigger guard; this fixture
   does not weaken that guard.

Every cycle first restores topology, report/touch ownership, all twelve
WorkUnit bindings and PCM/contact payload, contact memory blocks, island,
SAP, transform cache, shape-cache IDs, bodies, scene clock, context, and
query. It requires exact restored-A contact readback before simulating; it
then compares the complete stopped component images, deletion facts, and
ordered callbacks at every successor. A rejected stage aborts without
simulating a partially restored scene. The fixture uses the existing image
comparators and restore guards without relaxation.

This is an offline synthetic stress test, not proof of game-level rewind
parity. It does not cover the game's full geometry, Unity's statically linked
PhysX ABI, CCD, constraints, articulation, or actor lifetime changes.
