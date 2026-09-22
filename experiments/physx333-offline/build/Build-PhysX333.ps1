param(
    [ValidateSet('release', 'checked', 'debug')]
    [string]$Configuration = 'release',
    [string]$PhysXRoot,
    [string]$VisualStudio = 'C:\Program Files\Microsoft Visual Studio\2022\Community',
    [int]$MaxCpuCount = 4,
    [switch]$NPhaseBridge,
    [string[]]$MirrorPatch = @()
)

$ErrorActionPreference = 'Stop'

if (-not $PhysXRoot) {
    $PhysXRoot = Join-Path $PSScriptRoot '..\..\..\..\PhysX-3.3.3'
}
$PhysXRoot = [System.IO.Path]::GetFullPath($PhysXRoot)
$sdkSource = Join-Path $PhysXRoot 'PhysXSDK'
$sdkMirror = Join-Path $PSScriptRoot 'work\PhysXSDK'
$solution = Join-Path $sdkMirror 'Source\compiler\vc12win32\PhysX.sln'
$msbuild = Join-Path $VisualStudio 'MSBuild\Current\Bin\MSBuild.exe'
$compat = Join-Path $PSScriptRoot 'compat'

if (-not (Test-Path -LiteralPath (Join-Path $sdkSource 'Source\compiler\vc12win32\PhysX.sln'))) {
    throw "PhysX 3.3.3 Win32 solution not found under $sdkSource"
}
if (-not (Test-Path -LiteralPath $msbuild)) {
    throw "MSBuild not found at $msbuild"
}
$safeDirectory = $PhysXRoot.Replace('\', '/')
$sourceRevision = & git -c "safe.directory=$safeDirectory" -C $PhysXRoot rev-parse HEAD
if ($LASTEXITCODE -ne 0 -or $sourceRevision -ne 'efd57d99e04a1b017df06a3495ec8a326988abe3') {
    throw "Expected pinned PhysX source efd57d99e04a1b017df06a3495ec8a326988abe3; found $sourceRevision"
}

# The original project files use relative paths into their SDK tree and write
# generated files there. Mirror only the required parts to keep that tree clean.
$mirrorDirs = @(
    'Source',
    'Include',
    'externals\nvToolsExt\1\include',
    'externals\nvToolsExt\1\lib\Win32',
    'Lib\vc12win32'
)
foreach ($relative in $mirrorDirs) {
    $from = Join-Path $sdkSource $relative
    $to = Join-Path $sdkMirror $relative
    if (-not (Test-Path -LiteralPath $from)) {
        throw "Required PhysX SDK directory not found: $from"
    }
    New-Item -ItemType Directory -Path $to -Force | Out-Null
    Copy-Item -Path (Join-Path $from '*') -Destination $to -Recurse -Force
}

# VS2013 emitted these inline template bodies from their call sites. v143
# does not, so use the existing explicit instantiations on Win32 too.
$sceneQueriesPath = Join-Path $sdkMirror 'Source\PhysX\src\NpSceneQueries.cpp'
$sceneQueriesText = [System.IO.File]::ReadAllText($sceneQueriesPath)
$oldGuard = '#if !(PX_IS_WINDOWS | PX_IS_X360 | PX_IS_SPU)'
if (-not $sceneQueriesText.Contains($oldGuard)) {
    throw "Expected template instantiation guard missing: $sceneQueriesPath"
}
$sceneQueriesText = $sceneQueriesText.Replace($oldGuard, '#if !PX_IS_SPU')
[System.IO.File]::WriteAllText($sceneQueriesPath, $sceneQueriesText, (New-Object System.Text.UTF8Encoding($false)))

# Our test-only bridge is built only in the disposable source mirror. It
# exposes selected original lifecycle methods without changing vendor files
# or linking a private PhysX symbol directly from the external harness.
if ($NPhaseBridge) {
    $bridgeSource = Join-Path $PSScriptRoot '..\nphase\NPhaseBridge.cpp'
    $bridgeHeader = Join-Path $PSScriptRoot '..\nphase\NPhaseBridge.h'
    $reportBridgeSource = Join-Path $PSScriptRoot '..\interaction\InteractionReportBridge.cpp'
    $reportBridgeHeader = Join-Path $PSScriptRoot '..\interaction\InteractionReportBridge.h'
    $bridgeDestination = Join-Path $sdkMirror 'Source\SimulationController\src'
    foreach ($bridgeFile in @($bridgeSource, $bridgeHeader,
                              $reportBridgeSource, $reportBridgeHeader)) {
        if (-not (Test-Path -LiteralPath $bridgeFile)) {
            throw "NPhase bridge source not found: $bridgeFile"
        }
        Copy-Item -LiteralPath $bridgeFile -Destination $bridgeDestination -Force
    }

    $simulationProject = Join-Path $sdkMirror 'Source\compiler\vc12win32\SimulationController.vcxproj'
    $simulationText = [System.IO.File]::ReadAllText($simulationProject)
    $compileAnchor = '<ClCompile Include="..\..\SimulationController\src\ScNPhaseCore.cpp">'
    if (-not $simulationText.Contains($compileAnchor)) {
        throw "Expected NPhase compile entry missing: $simulationProject"
    }
    $simulationText = $simulationText.Replace($compileAnchor,
        '<ClCompile Include="..\..\SimulationController\src\NPhaseBridge.cpp" />' + "`r`n`t`t" +
        '<ClCompile Include="..\..\SimulationController\src\InteractionReportBridge.cpp" />' + "`r`n`t`t" +
        $compileAnchor)
    [System.IO.File]::WriteAllText($simulationProject, $simulationText,
        (New-Object System.Text.UTF8Encoding($false)))

    $physxProject = Join-Path $sdkMirror 'Source\compiler\vc12win32\PhysX.vcxproj'
    $physxText = [System.IO.File]::ReadAllText($physxProject)
    $linkAnchor = '/DELAYLOAD:PhysX3Common_x86.dll /INCREMENTAL:NO</AdditionalOptions>'
    if (-not $physxText.Contains($linkAnchor)) {
        throw "Expected release linker options missing: $physxProject"
    }
    $physxText = $physxText.Replace($linkAnchor,
        '/DELAYLOAD:PhysX3Common_x86.dll /INCREMENTAL:NO /INCLUDE:_oc2_physx333_nphase_recreate_v1 /INCLUDE:_oc2_physx333_report_create_v1</AdditionalOptions>')
    [System.IO.File]::WriteAllText($physxProject, $physxText,
        (New-Object System.Text.UTF8Encoding($false)))
}

# Optional test-only accessors can be supplied as small patch files. They are
# applied to the disposable mirror after it is refreshed from pinned source.
foreach ($patchPath in $MirrorPatch) {
    $resolvedPatch = (Resolve-Path -LiteralPath $patchPath).Path
    & git -C $sdkMirror apply --check $resolvedPatch
    if ($LASTEXITCODE -ne 0) { throw "Mirror patch did not apply cleanly: $resolvedPatch" }
    & git -C $sdkMirror apply $resolvedPatch
    if ($LASTEXITCODE -ne 0) { throw "Failed to apply mirror patch: $resolvedPatch" }
}

$binSource = Join-Path $sdkSource 'Bin\vc12win32'
$binMirror = Join-Path $sdkMirror 'Bin\vc12win32'
New-Item -ItemType Directory -Path $binMirror -Force | Out-Null
foreach ($name in @('PhysX3Gpu_x86.dll', 'PhysX3GpuCHECKED_x86.dll', 'PhysX3GpuDEBUG_x86.dll', 'nvToolsExt32_1.dll')) {
    $from = Join-Path $binSource $name
    if (Test-Path -LiteralPath $from) {
        Copy-Item -LiteralPath $from -Destination $binMirror -Force
    }
}

# MSVC 19.40 removed <typeinfo.h> and reports many diagnostics that the
# VS2013 projects mark /WX. These flags affect only this process's compiler
# invocations and leave the mirrored vendor project files unchanged.
$previousClFlags = $env:_CL_
$env:_CL_ = "/I$compat /DDELAYIMP_INSECURE_WRITABLE_HOOKS /w /WX- $previousClFlags"
Write-Host "Building $solution ($Configuration, Win32, v143)"
& $msbuild $solution '/t:PhysX' "/m:$MaxCpuCount" '/clp:ErrorsOnly' '/nologo' `
    "/p:Configuration=$Configuration" '/p:Platform=Win32' `
    '/p:PlatformToolset=v143' '/p:WindowsTargetPlatformVersion=10.0.19041.0' `
    '/p:UseClStructuredOutput=false'
$env:_CL_ = $previousClFlags
if ($LASTEXITCODE -ne 0) {
    throw "PhysX 3.3.3 build failed with exit code $LASTEXITCODE"
}

Write-Host "PhysX DLLs: $binMirror"
Write-Host "PhysX import/static libraries: $(Join-Path $sdkMirror 'Lib\vc12win32')"
