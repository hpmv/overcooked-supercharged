# PhysX 3.3.3 offline Win32 build

This build uses the local `PhysX-3.3.3` source checkout and the original
`PhysXSDK/Source/compiler/vc12win32` solution. It mirrors the source and its
small required dependencies under `work/`, so the vendor checkout is not
modified by compilation. The mirror and resulting binaries are ignored by Git.
The checkout must be at commit `efd57d99e04a1b017df06a3495ec8a326988abe3`.

From this directory in PowerShell:

```powershell
.\Build-PhysX333.ps1 -Configuration release
```

The default compiler is Visual Studio 2022 Community's v143 toolset. Supply
`-VisualStudio` for another installation, or `-PhysXRoot` for another checkout.
The build targets `PhysX` and its solution dependencies for Win32. Its DLLs
land in `work/PhysXSDK/Bin/vc12win32`; import and static libraries land in
`work/PhysXSDK/Lib/vc12win32`. Both public and private SDK headers are
available under `work/PhysXSDK/Include` and `work/PhysXSDK/Source`.

The original release projects define `NDEBUG` but not `PX_CHECKED` or
`PX_DEBUG`. `checked` and `debug` are available for invariant diagnostics.
The local compatibility shim maps obsolete `<typeinfo.h>` to `<typeinfo>`;
the build process disables warnings treated as errors by the old VS2013
projects and opts into writable delay-load hooks for their legacy declarations.
MSBuild structured compiler output is disabled because old source diagnostics
are not reliably encoded for the modern JSON parser. These adjustments do not
edit the upstream checkout.
The mirror also enables the source's existing explicit scene-query template
instantiations for Win32, which v143 needs at link time.

For the isolated NPhase lifecycle and report-metadata probes, use
`-NPhaseBridge`. This copies our small test-only bridges into the ignored
mirror, adds them to the mirrored SimulationController project, and force-links
their exports into the Win32 DLL:

```powershell
.\Build-PhysX333.ps1 -Configuration release -NPhaseBridge
```

For a cold scene-query tree rewind, also use `-QueryBridge`. It adds a
test-only source export for the original `AABBTree::release()` lifecycle
method, which the Win32 DLL otherwise does not expose. Both switches may be
used together; the pinned vendor checkout remains untouched:

```powershell
.\Build-PhysX333.ps1 -Configuration release -NPhaseBridge -QueryBridge
```

Run `..\harness\Build-Harness.cmd --nphase-topology-probe` or
`..\harness\Build-Harness.cmd --interaction-metadata-probe` from a command
prompt after that build. These probes intentionally exit without normal PhysX
teardown because they do not reconstruct all coupled predecessor state. They
must not be used with the shipped Unity DLL.

Small test-only changes to private SDK internals can be supplied as patch
files with `-MirrorPatch path1.patch,path2.patch`. Patches are applied after
refreshing the disposable mirror, before compilation. Their paths should be
relative to the mirror's `PhysXSDK` root; the vendor checkout stays clean.
This is a best-effort build of source matching the PhysX 3.3.3 tag; Unity's
shipped binary can still differ in compiler settings and integration patches.
