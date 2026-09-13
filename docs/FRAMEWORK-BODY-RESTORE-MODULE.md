# Replaceable body restore algorithm

The permanent plugin owns native checkpoint capture, identity and mass-mode validation, history, and exact postconditions. `NativeBodyPoseCheckpoint.Restore` selects an installed `IBodyRestoreStrategy` or the built-in implementation. A module exception fails the restore; it never triggers a second built-in attempt after possible mutation.

The version1 contract is `Name`, `ApiVersion`, and `Restore(NativeBodyPoseCheckpoint.Snapshot[] saved)`. Captured pose and mass properties are public getters with internal setters. Caller-visible arrays are copies; the module cannot replace the permanent target array through the argument it receives. Native Rigidbody and Transform references remain intentionally writable by trusted authoring code. This is a lifecycle and correctness seam, not an isolation boundary for untrusted assemblies.

`InstallRestoreStrategy` requires the original capture thread, a paused native game, a supported API and no restore in progress. It returns a disposable ownership lease. Disposing a replaced lease cannot clear a newer registration. The permanent dispatch code pins current velocities and kinematic/gravity flags and independently verifies exact target poses, mass properties and immutable settings after the algorithm returns. Reentrant restore and strategy replacement during restore are rejected. Target collider geometry is required before mass recomputation; final verification after the existing cannon sidecar still checks the complete collider topology.

The separately compiled implementation is `framework/modules/body-restore/BodyRestoreModule.cs`, entry type `SuperchargedPatch.Authoring.Modules.BodyRestoreModule`. It contains the actual native reset and bounded quaternion-correction algorithm, with its own reset/setter receipts. It does not call the built-in implementation. It implements `SuperchargedPatch.Authoring.IAuthoringModule`: construction is inactive, `Invoke("activate", {})` installs its strategy, `deactivate` relinquishes its lease, and `status` reports module and permanent-dispatch receipts. `Dispose` removes only its own lease.

Build against the immutable core containing the contract:

```powershell
./scripts/Build-FrameworkBodyModule.ps1 -CoreBuild <frozen-core-directory> -Revision body-r1
```

The wrapper uses `Build-FrameworkModule.ps1` to compile CLR2 code into a uniquely named DLL under `framework-run/modules`, with source/core/game/DLL hashes. It performs no deployment or game connection. Root's permanent module host loads the exact hash-pinned DLL through `hot-load`, then activation is an explicit paused `hot-call`. To revise the algorithm, edit or copy the external source directory, compile a new revision, and replace the module in slot `body-restore`. No capture schema or process restart is required for compatible revisions. The loaded core is still permanent; changed core guards or snapshot schemas require a new core build.

`nativeBodyMassFrameRestores.restoreStrategy` identifies the installed strategy and bounded dispatch receipts. Historical built-in reset records are labeled `attemptScope: builtin-only`; external algorithm details are returned by that module's `status` operation. Module failure preserves the actual native state and error for diagnosis; it does not claim rollback.

`artifacts/framework-body-module-tests.json` records 170 production-linked offline assertions. The same-process fixture first uses a one-assignment strategy that fails on a modeled native setter residual, then replaces it with the external algorithm and reaches the same saved target in two assignments without recapture. Tests also cover paused/thread/API gates, inactive construction, successor-safe disposal, private target setters, copied arrays, final-pose and velocity rejection, reentry, and no fallback after a post-mutation exception. The Unity objects are controlled stand-ins. This proves strategy dispatch and algorithm replacement behavior offline, not native DLL replacement, automatic-mass equivalence, or replay parity. Root owns the independent installed-runtime test and frozen-core build.
