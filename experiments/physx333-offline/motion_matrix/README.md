# PhysX 3.3.3 motion/sleep rewind matrix

This is a separate, offline Win32 fixture linked to the already-built PhysX
3.3.3 source mirror. It does not load Unity or the game and it does not rebuild
or patch vendor source. Run from the framework root with
`cmd /c experiments\physx333-offline\motion_matrix\Build-Check.cmd`.

## Measured cases

| Checkpoint A → successor B | Same-scene public-only rewind | Existing image gate | Joined result |
| --- | --- | --- | --- |
| Quiet awake → naturally asleep, with A's wake counter already zero | Replayed B stays awake instead of sleeping; `BodySim.internalFlags` differs at restored A | `BodyImage` rejects changed active state; `IslandImage` alone accepts | Exact A, B, and next-step body/island/scene-clock/public state for 100 B→A→B cycles after the guarded sleep-list join below |
| Quiet asleep → velocity-induced awake | One replay step happens to match, but restored A's `BodySim.internalFlags` differs | `BodyImage` rejects changed active state | Exact A, B, and next-step body/island/scene-clock/public state for 100 cycles |
| Sleeping box on high-friction static floor → horizontal velocity/dash | One replay step happens to match, but restored A's `BodySim.internalFlags` differs | `BodyImage` rejects active-state change; `IslandImage` rejects changed actor/contact-manager binding | Not proved; the contact-manager lifecycle must be restored by the interaction/NPhase join |

The quiet scene has one dynamic box, no contacts, and zero gravity. The floor
scene uses gravity, box/box contact on both sides of the dash checkpoint, and
0.9 static/dynamic friction. The floor box naturally sleeps after 25 steps.
The fixture compares bit-exact public pose, velocity, wake counter, sleep bit,
simulation statistics, and callbacks. Joined checks also compare the existing
`BodyImage`, `IslandImage`, and `SceneClockImage` at restored A and successor B.

## Source-backed join for the quiet cases

`ScBodySim.h` defines four sleep/wake list and notification marker bits on each
body. `ScScene.cpp`'s `onBodySleep`, `onBodyWakeUp`, and
`clearSleepWakeBodies` maintain the matching `mSleepBodies` and `mWokeBodies`
arrays. `SceneClockImage` captures/restores those arrays. `BodyImage` correctly
refuses to restore a body when its active state or scene-list marker bits still
disagree, because changing only the body would make the scene inconsistent.

The isolated fixture first uses `PxRigidDynamic::wakeUp` or `putToSleep` to make
the actor's active topology match A, then restores the island image and scene
clock. Its local guarded bridge validates that the saved four marker bits agree
with the saved scene-list membership, changes only those four bits, and invokes
`RestoreBodies` for the remaining body payload. A failed body restore rolls
those four bits back. This bridge is intentionally limited to one dynamic actor
and same-scene allocation addresses; it is not a general production restore.

The first sleep/wake event allocates notification arrays. `SceneClockImage`
rightly rejects restoring an earlier image when those array addresses or
capacities changed. The passing 100-cycle fixtures prime both event arrays
before taking A. General allocator-growth rebasing and atomic rollback of all
components remain unimplemented.

This does **not** establish parity for contact-bearing dash motion, multi-body
sleep islands, constraints, kinematics, CCD, SAP/transform-cache state, longer
suffixes, or the Unity-shipped PhysX binary. In particular, a matching
public-only step in the floor case must not be interpreted as exact rewind.
