param(
    [ValidateSet('release', 'checked', 'debug')]
    [string]$Configuration = 'release',
    [string]$PhysXRoot,
    [string]$VisualStudio = 'C:\Program Files\Microsoft Visual Studio\2022\Community',
    [int]$MaxCpuCount = 4,
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
$compat = Join-Path $PSScriptRoot '..\build\compat'

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

# All generated files and binaries remain under this query_cross_swap folder.
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
# needs the source's existing explicit instantiations on Win32.
$sceneQueriesPath = Join-Path $sdkMirror 'Source\PhysX\src\NpSceneQueries.cpp'
$sceneQueriesText = [System.IO.File]::ReadAllText($sceneQueriesPath)
$oldGuard = '#if !(PX_IS_WINDOWS | PX_IS_X360 | PX_IS_SPU)'
if (-not $sceneQueriesText.Contains($oldGuard)) {
    throw "Expected template instantiation guard missing: $sceneQueriesPath"
}
$sceneQueriesText = $sceneQueriesText.Replace($oldGuard, '#if !PX_IS_SPU')
[System.IO.File]::WriteAllText($sceneQueriesPath, $sceneQueriesText,
    (New-Object System.Text.UTF8Encoding($false)))

$queryDestination = Join-Path $sdkMirror 'Source\SceneQuery'
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'CrossSwapBridge.cpp') `
    -Destination $queryDestination -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'CrossSwapBridge.h') `
    -Destination $queryDestination -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '..\query_image\QueryTreeBridge.cpp') `
    -Destination $queryDestination -Force

# Apply only to the disposable mirror. The default patch adds bridge compile
# entries and force-link directives; optional patches can extend the probe.
foreach ($patchPath in @((Join-Path $PSScriptRoot 'CrossSwapMirror.patch')) + $MirrorPatch) {
    $resolvedPatch = (Resolve-Path -LiteralPath $patchPath).Path
    & git -C $sdkMirror apply --check $resolvedPatch
    if ($LASTEXITCODE -ne 0) { throw "Mirror patch did not apply cleanly: $resolvedPatch" }
    & git -C $sdkMirror apply $resolvedPatch
    if ($LASTEXITCODE -ne 0) { throw "Failed to apply mirror patch: $resolvedPatch" }
}

$binSource = Join-Path $sdkSource 'Bin\vc12win32'
$binMirror = Join-Path $sdkMirror 'Bin\vc12win32'
New-Item -ItemType Directory -Path $binMirror -Force | Out-Null
foreach ($name in @('PhysX3Gpu_x86.dll', 'PhysX3GpuCHECKED_x86.dll',
                    'PhysX3GpuDEBUG_x86.dll', 'nvToolsExt32_1.dll')) {
    $from = Join-Path $binSource $name
    if (Test-Path -LiteralPath $from) {
        Copy-Item -LiteralPath $from -Destination $binMirror -Force
    }
}

$previousClFlags = $env:_CL_
try {
    $env:_CL_ = "/I$compat /DDELAYIMP_INSECURE_WRITABLE_HOOKS /w /WX- $previousClFlags"
    Write-Host "Building isolated $solution ($Configuration, Win32, v143)"
    & $msbuild $solution '/t:PhysX' "/m:$MaxCpuCount" '/clp:ErrorsOnly' '/nologo' `
        "/p:Configuration=$Configuration" '/p:Platform=Win32' `
        '/p:PlatformToolset=v143' '/p:WindowsTargetPlatformVersion=10.0.19041.0' `
        '/p:UseClStructuredOutput=false'
    if ($LASTEXITCODE -ne 0) {
        throw "Isolated PhysX 3.3.3 build failed with exit code $LASTEXITCODE"
    }
}
finally {
    $env:_CL_ = $previousClFlags
}

Write-Host "Isolated PhysX DLLs: $binMirror"
Write-Host "Isolated import/static libraries: $(Join-Path $sdkMirror 'Lib\vc12win32')"
