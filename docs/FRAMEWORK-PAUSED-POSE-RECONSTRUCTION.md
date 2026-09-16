# Native pose deltas received while paused

Native W fresh-search candidate 0 reconstructed chef 106 with the setup's identity quaternion at frames 1 and 31. Candidate 3 reconstructed the actual 180-degree rotation. The paired native Rigidbody receipts agree exactly: `(0, 1, 0, -4.371138828673793e-08)`. This was a reconstruction error, not a changed native initial condition.

The exact W trace shows the difference in observation timing:

- Candidate 0's load is line 11041. The authoritative initial rotation arrives at line 11474, paused block row 1, after InLevel and the bridge pause. The previous controller applied only server messages during paused callbacks and discarded this `ItemData` field. The native collector subsequently omitted unchanged rotation, as required by its delta protocol.
- Candidate 3 also receives the rotation during startup at line 12967. That path already applies item observations, so this candidate retained the correct value before the paused refresh.

`RealGameConnector` now applies received `ItemData` position, rotation, velocity, and angular velocity through the existing `ApplyPositionUpdate` method while paused, awaiting physics alignment, or awaiting resume. It writes at the current logical frame; it does not advance time, automatic progress, or action completion. Optional fields still require their actual wire-presence flags. Unknown IDs do not create records. Warping continues to use the separate verified-acknowledgement path.

This relies on the current native producer's effective frozen-physics velocities, introduced before W. It does not synthesize values from transforms, movement direction, registry geometry, or a later successful candidate.

## Verification

`scripts/extract_framework_paused_pose_fixtures.py` expands the existing hash-verified trace blocks and extracts unchanged load/start/pause/warmup callbacks and recorded control requests for both candidates. The extraction manifest pins the source trace, initial setup protobuf, callback locations, and fixture hashes under `artifacts/framework-paused-pose-fixtures`.

`scripts/FrameworkPausedPoseCheck` replays 982 actual callbacks and checks 20 additional conditions (1,002 assertions total), including the actual paired native quaternion, frame 1 and frame 31, omitted-field preservation, alignment/resume boundaries, unacknowledged warp exclusion, and unknown-ID rejection. The same test against immutable V10 fails on the first candidate-0 paused-rotation assertion. The report is `artifacts/framework-paused-pose-check/tests.json`; the old-version failure is retained in `artifacts/framework-paused-pose-v10-negative.txt`.

The isolated candidate also passes the original 324 headless selftests and 12 actual native-K warp acknowledgement/failure/history checks. The DLL tested is SHA-256 `bf302c81c473890251023aabb7eb85ee8c5f534f00d93c0d6297083efc29bb38`. All checks are offline; no native input, game connection, or frozen bundle was changed.

```powershell
$env:NUGET_PACKAGES='M:\projects\game-test-2\framework\.packages'
dotnet run --project scripts/FrameworkPausedPoseCheck -c Release -p:HeadlessDirectory=ABSOLUTE_FROZEN_HOST_DIRECTORY
```

This establishes faithful reconstruction of the captured observations. A future native fresh-search run is still needed to validate the complete updated controller and input changes together.
