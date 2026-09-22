# Trigger and marker interaction baseline (PhysX 3.3.3)

This standalone, source-built Win32 fixture uses the pinned PhysX 3.3.3
mirror. It does not load Unity or the game. Run:

```bat
cmd /c experiments\physx333-offline\trigger_marker\Build-Check.cmd
```

The scene contains one twelve-shape dynamic mover, twelve contact statics,
four trigger statics, and two marker statics. The latter use public simulation
filter data plus a shader returning `PxFilterFlag::eSUPPRESS`; the pinned source
maps that filter result to `PX_INTERACTION_TYPE_MARKER` in
`ScNPhaseCore::getRbElementInteractionType`. `eKILL` would create no
interaction. The fixture reads private state only through the existing
source-built Oracle, after `fetchResults`.

At settled A it asserts twelve overlap/contact interactions, four trigger
interactions, and two marker interactions, with matching pool usage. Moving
the dynamic actor by 0.2 units on the z axis deletes four contacts and two
triggers, leaving 8/2/2. It prints callbacks in delivered order, checks the
expected four contact losses, eight persistent contacts, and two trigger
losses, and compares callback ordering against a fresh scene following the
same steps. The B order is asserted explicitly: trigger losses 16 and 17,
contact losses 9 through 12, then contact persists 1 through 8.

This is a baseline interaction-count and event fixture, not a rewind
implementation or an exact level reconstruction. It does not model the
level's capsule/box contact geometry or game-specific filters.
