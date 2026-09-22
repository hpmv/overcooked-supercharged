param(
    [string]$VisualStudio = 'C:\Program Files\Microsoft Visual Studio\2022\Community'
)

$ErrorActionPreference = 'Stop'
$repo = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..\..'))
$vendor = Join-Path $repo 'PhysX-3.3.3'
$shared = Join-Path $PSScriptRoot '..\build\work\PhysXSDK'
$mirror = Join-Path $PSScriptRoot 'work\PhysXSDK'
$solution = Join-Path $mirror 'Source\compiler\vc12win32\PhysX.sln'
$msbuild = Join-Path $VisualStudio 'MSBuild\Current\Bin\MSBuild.exe'
$compat = Join-Path $PSScriptRoot '..\build\compat'

$safeVendor = $vendor.Replace('\', '/')
$revision = & git -c "safe.directory=$safeVendor" -C $vendor rev-parse HEAD
if ($LASTEXITCODE -ne 0 -or
    $revision -ne 'efd57d99e04a1b017df06a3495ec8a326988abe3') {
    throw "The PhysX source checkout must be pinned to 3.3.3-1.3.3"
}
if (-not (Test-Path -LiteralPath (Join-Path $shared 'Bin\vc12win32\PhysX3_x86.dll'))) {
    throw "Build the pinned shared source mirror first"
}
if (-not (Test-Path -LiteralPath $msbuild)) { throw "MSBuild is absent" }

# The shared mirror is read only throughout this build. Keep its object files
# so this isolated test bridge requires only an incremental source rebuild.
if (-not (Test-Path -LiteralPath $mirror)) {
    New-Item -ItemType Directory -Path $mirror -Force | Out-Null
    Copy-Item -Path (Join-Path $shared '*') -Destination $mirror -Recurse -Force
}
$destination = Join-Path $mirror 'Source\SimulationController\src'
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'TriggerRewindBridge.cpp') -Destination $destination -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'TriggerRewindBridge.h') -Destination $destination -Force

$simulationProject = Join-Path $mirror 'Source\compiler\vc12win32\SimulationController.vcxproj'
$simulationText = [System.IO.File]::ReadAllText($simulationProject)
$compileEntry = '<ClCompile Include="..\..\SimulationController\src\TriggerRewindBridge.cpp" />'
$compileAnchor = '<ClCompile Include="..\..\SimulationController\src\ScNPhaseCore.cpp">'
if (-not $simulationText.Contains($compileEntry)) {
    if (-not $simulationText.Contains($compileAnchor)) { throw "SimulationController anchor absent" }
    $simulationText = $simulationText.Replace($compileAnchor,
        $compileEntry + "`r`n`t`t" + $compileAnchor)
    [System.IO.File]::WriteAllText($simulationProject, $simulationText,
        (New-Object System.Text.UTF8Encoding($false)))
}

$physxProject = Join-Path $mirror 'Source\compiler\vc12win32\PhysX.vcxproj'
$physxText = [System.IO.File]::ReadAllText($physxProject)
$force = '/INCLUDE:_oc2_physx333_trigger_recreate_v1'
if (-not $physxText.Contains($force)) {
    $anchor = '/DELAYLOAD:PhysX3Common_x86.dll /INCREMENTAL:NO'
    if (-not $physxText.Contains($anchor)) { throw "PhysX linker anchor absent" }
    $physxText = $physxText.Replace($anchor, $anchor + ' ' + $force)
    [System.IO.File]::WriteAllText($physxProject, $physxText,
        (New-Object System.Text.UTF8Encoding($false)))
}

$oldCl = $env:_CL_
$env:_CL_ = "/I$compat /DDELAYIMP_INSECURE_WRITABLE_HOOKS /w /WX- $oldCl"
try {
    & $msbuild $solution '/t:PhysX' '/m:4' '/clp:ErrorsOnly' '/nologo' `
        '/p:Configuration=release' '/p:Platform=Win32' `
        '/p:PlatformToolset=v143' '/p:WindowsTargetPlatformVersion=10.0.19041.0' `
        '/p:UseClStructuredOutput=false'
    if ($LASTEXITCODE -ne 0) { throw "Isolated PhysX bridge build failed" }
} finally {
    $env:_CL_ = $oldCl
}
