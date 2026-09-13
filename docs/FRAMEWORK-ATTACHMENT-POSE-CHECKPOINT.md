# Native attachment pose checkpoint

The first native S plate-search candidate moved original plate 10 from counter 38 to counter 32 in 73 frames. It passed the exact attachment/composition goal. Search then stopped because rollback restored the plate's attachment to 38 while its observed position remained the future counter's position.

`artifacts/framework-migration/native-s/plate-search/observations.json` is unchanged (SHA256 `e0474dd45c3c2927fb3fc9121e0acbc877fc0c40afd85405b12efd1036128504`). Its `base`, `chef-103-walk-end`, and `candidate-1-restored` responses show:

| Observation | Plate parent | Plate Z |
| --- | --- | --- |
| Baseline frame 31 | 38 | -15.599998 |
| Candidate endpoint frame 104 | 32 | -10.799999 |
| Restored frame 31 | 38 | -10.799999 |

The exact first warp acknowledgement in the lossless exchange trace reports plate Z `-10.799999237060547`. Subsequent paused callbacks do not report a position correction. This is a native collected pose, not an invented headless continuation.

The original `WarpCalculator` supplies an attachable item's position only when it is free. `WarpHandler.WarpChefAndPositions` writes free physics containers and actual rigidbodies. The fixed-body checkpoint similarly selects registered rigidbodies. An attached logical item's transform therefore had no explicit captured-pose restoration. Actual plate 10 metadata contains `PhysicalAttachment` and `EmptyLerp`, and no own `Rigidbody` or `BasicLerp`. Calling a generic interpolation reset would not address this demonstrated case.

`NativeAttachmentPoseCheckpoint` adds an authoring-only checkpoint for the initial observed physical attachment IDs. It records each native registration/component/container incarnation, the exact parent and its registered ancestor, local/world position and rotation, and local/lossy scale. Preflight allows the current parent to differ but requires the captured parent to retain its original identity. After ordinary warp callbacks restore the native parent and attachment flags, the helper restores only the observed local position, local rotation, and local scale. It verifies local and world fields exactly before acknowledgement. It does not move the separate rigidbody, call native placement/update callbacks, advance a frame, or derive a target offset from station geometry.

The first T idle probe rejected untouched extinguisher 1 because the initial helper confused prediction mode with a running predictor. Installed native `Attach` stores the parent's prediction capability in a mode flag; native client update additionally requires a non-null predictor before executing it. The corrected helper captures/restores both exact mode flags and the client's cached parent. A null predictor remains valid even with mode enabled. A native `ConveyorPrediction` with an observed empty queue retains its actual object, transform, remaining-move value and calculated-distance flag. Nonempty queues and unknown predictor implementations remain unsupported. Diagnostics expose the mode flags, predictor type and captured queue count.

Active mesh interpolation, stateful world interpolation, and inconsistent server/client attachment flags are also unsupported. These conditions are recorded during capture and reject target validation. Inactive cached mesh objects do not reject. A current pending prediction must have been retired by native callbacks before pose restoration. Target parenting or identity failures reject the entire set before any pose setter runs.

`scripts/FrameworkAttachmentPoseCheck` passes 49 checks: synthetic identity/lifecycle/transform and prediction fixtures, the unchanged native S witness, and installed native IL contracts. The installed `Assembly-CSharp.dll` hash is `9bb6a3791331201d32ca89c3509f019a9780309da7110002f04020e8491e1908`. The helper also compiles directly against the installed CLR2/native assemblies. The corrected report is `artifacts/framework-attachment-check/tests-u.json`; the earlier 41-check report and T rejection remain preserved.

These checks establish the bounded helper and its evidence source. They do not establish native rollback or subsequent fixed-input replay parity; that requires the next native trial. Dynamic replacement of an original fixed attachment, active interpolation, and full recipe/score qualification remain outside this helper's supported scope.
