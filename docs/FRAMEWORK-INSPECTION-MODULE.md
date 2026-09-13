# Registered-component inspection module

`framework/modules/inspection/InspectionModule.cs` is a separately compiled authoring module. Entry type: `SuperchargedPatch.Authoring.Modules.InspectionModule`. It installs no callbacks and retains no native objects between calls. The core module host supplies the main-thread, ready-level, paused, input-fenced gate.

Build with a unique revision against a frozen core that contains `IAuthoringModule`:

```powershell
scripts/Build-FrameworkModule.ps1 -CoreBuild artifacts/framework-plugin-native-x -SourceDirectory framework/modules/inspection -Revision r1 -Name Inspection -EntryType SuperchargedPatch.Authoring.Modules.InspectionModule
```

Use the actual DLL path and SHA256 returned by the build manifest when issuing `hot-load`. Do not load an offline test assembly into the game.

First enumerate the actual registered chef and component instance IDs:

```json
{"command":"hot-call","slot":"inspection","operation":"inspect","args":{"entityId":103}}
```

Then explicitly select the native Rigidbody and properties to read. Member enumeration reports metadata without invoking all property getters; only requested members are read.

```json
{"command":"hot-call","slot":"inspection","operation":"inspect","args":{"entityId":103,"component":"UnityEngine.Rigidbody","members":[{"kind":"property","name":"position"},{"kind":"property","name":"rotation"},{"kind":"property","name":"centerOfMass"},{"kind":"property","name":"inertiaTensor"},{"kind":"property","name":"inertiaTensorRotation"}]}}
```

For `set` and `invoke`, add integer `expectedObjectId` and `expectedComponentId` from that actual observation. The following template must be populated with those observed integer values before sending; the names below are placeholders:

```text
command: hot-call
slot: inspection
operation: invoke
args:
  entityId: 103
  component: UnityEngine.Rigidbody
  expectedObjectId: <observed GameObject instance ID>
  expectedComponentId: <observed Rigidbody instance ID>
  method: ResetCenterOfMass
  members:
    - {kind: property, name: centerOfMass}
    - {kind: property, name: inertiaTensor}
    - {kind: property, name: inertiaTensorRotation}
```

`ResetInertiaTensor` uses the same shape. No-argument instance methods only are supported. `invoke` requires explicit observation members for its before/after receipt. Public and private ancestor methods or members can be selected with their exact full `declaringType`; ambiguous inherited names are rejected.

A `set` request uses `kind` (`field` or `property`), `name`, optional `declaringType`, and an explicit `value`. Values may be finite numeric scalars, booleans, strings, exact named enums, `{x,y,z}` vectors, or `{x,y,z,w}` quaternions. There is no quaternion normalization. Integer fractions/overflow, missing vector components, readonly fields, indexers, static members, and arbitrary object reference writes are rejected. The selected set member is read before mutation and again afterward. Read-only component selection may omit instance IDs when the type is unique; providing IDs disambiguates duplicate components and verifies the current incarnation.

`batch` accepts `operations`, an array of up to 32 `{operation,args}` objects. It validates every operation before the first write, then revalidates native entity/component identity before and after each operation. Operations run sequentially. A failed method may already have changed the native world: its receipt records `mutationAttempted`, an error, and an after observation where the identity still exists. The batch stops at that receipt. Nothing is rolled back, and success means the requested call completed, not that a physics equivalence condition was proved. Inspect the module result's `ok`, not merely transport success.

All writes and calls are explicit authoring mutations. Manual center-of-mass or inertia setters are tracked by the native body-mode observer and can exclude that body from automatic mass restoration. Native reset calls preserve the engine's automatic update mode, but their actual effects still require the returned native observations. This module does not certify score qualification, deterministic replay, or a body restoration algorithm.

The isolated adapter harness runs without Unity or game connections:

```powershell
dotnet run --project scripts/FrameworkInspectionModuleCheck/FrameworkInspectionModuleCheck.csproj -c Release
```

Its 31 tests cover exact instance identities, private ancestor reads, supported typed writes, no-argument methods, batch preflight, method failure after mutation, identity replacement, and module retirement. Final module compilation against the frozen core and installed Unity/game assemblies is separate from these controlled-adapter tests.
