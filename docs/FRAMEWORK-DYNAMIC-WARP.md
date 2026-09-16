# Dynamic object authoring rewind

The migrated framework can now preflight and recreate **previously observed dynamic spawn paths** through the native `NetworkUtils.ServerSpawnPrefab` and `DestroyObject` APIs. This is authoring support; final score-validation runs must start fresh and never warp. Native runtime validation of this addition is pending.

`NativeDynamicWarpPlan` observes each successful native spawn's prefab, ordered child-prefab list, native component signature and collider count. It retains copied metadata and asset references within the same native kitchen flow. This makes a crate → raw item → prepared replacement chain inspectable even after its raw parent was consumed. An unobserved chain, wrong component block, duplicate path, stale root, dangling target or invalid throw-collider reference is rejected before native world mutation.

During explicit rewind, the framework creates the chain in order, enables each native physical attachment, removes intermediate items and their registered physics containers, and attaches the original logical path to the final instance. All target creations finish before existing attachment restoration. Future branch objects are deleted only after the ordinary restoration stages release them. Partial creation failures clean up only newly created objects; failure remains explicit and paused. This is **not a rollback of an already partially restored world**.

The native registry allocates IDs from a free-ID queue. This change does not rewind that allocator. `nativeCheckpoints.nativeDynamicWarp` reports each recreated logical path alongside its actual native object/container IDs, and each deletion receipt. Raw native IDs and physical state must remain available in parity reports; only a separately named logical-path comparison may remap them.

Current scope excludes recreating an original fixed path without a validated dynamic logical path. Active cannon sessions/flights and pending delivered-plate fades remain rejected by the kitchen checkpoint. PhysX contact caches, sleep state, accumulator and interpolation are not recreated by this feature. Food composition, native progress, attachments, clocks, input state and post-warp physics require independent native observations.

Offline verification uses the actual plugin plan/transaction against controlled registry, spawn, batch-flush and failure callbacks. **42 checks pass**, including nested replacement, physical-container removal, failures after registration, later-target failure, current incarnation changes, stale rounds, missing prefab metadata, path conflicts and bounded cleanup. These fixtures do not execute Unity physics. The full plugin also compiled against the installed CLR2/Unity assemblies. See `artifacts/framework-dynamic-warp-tests.json` and `artifacts/framework-dynamic-warp-review.json`.

Run the offline fixtures with:

```powershell
dotnet run --project scripts/FrameworkDynamicWarpCheck/FrameworkDynamicWarpCheck.csproj -c Release
```

The first native acceptance experiment should save an observed raw ingredient, prepare/consume it through ordinary inputs, rewind to that raw checkpoint, and repeat the exact recorded inputs twice. Require actual warp acknowledgment, the same logical item/parent graph, native raw/prepared composition and progress, no orphan containers, exact accepted input edges and release-frame counts, and separately reported raw versus path-mapped entity state. Also test rewinding to before the spawn, and recreating the prepared child after both its native raw parent and prepared instance have been consumed.
