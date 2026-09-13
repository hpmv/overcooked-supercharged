param(
 [Parameter(Mandatory=$true)][string]$CoreBuild,
 [Parameter(Mandatory=$true)][string]$SourceDirectory,
 [Parameter(Mandatory=$true)][string]$Revision,
 [Parameter(Mandatory=$true)][string]$EntryType,
 [string]$Name='AuthoringHelper',
 [string]$Define=''
)
$ErrorActionPreference='Stop'
if($Revision -notmatch '^[a-zA-Z0-9_-]+$' -or $Name -notmatch '^[a-zA-Z][a-zA-Z0-9_]*$') { throw 'Use a simple unique revision and module name.' }
if($Define -and $Define -notmatch '^[a-zA-Z_][a-zA-Z0-9_]*(;[a-zA-Z_][a-zA-Z0-9_]*)*$') { throw 'Invalid compiler symbols.' }
$tasScriptParent=Split-Path -Parent $PSScriptRoot
if(Test-Path -LiteralPath (Join-Path $tasScriptParent '.git')) {$tasRepositoryRoot=$tasScriptParent}
elseif(Test-Path -LiteralPath (Join-Path $tasScriptParent 'framework/.git')) {$tasRepositoryRoot=Join-Path $tasScriptParent 'framework'}
else {throw 'Cannot locate the Supercharged repository root from the scripts directory.'}
$tasWorkspaceRoot=Split-Path -Parent $tasRepositoryRoot
$tasCore=(Resolve-Path -LiteralPath $CoreBuild).Path
$tasSource=(Resolve-Path -LiteralPath $SourceDirectory).Path
$tasManaged=Join-Path $tasWorkspaceRoot 'runtime/Overcooked2_Data/Managed'
$tasOutput=Join-Path $tasWorkspaceRoot "framework-run/modules/$Name-$Revision"
if(Test-Path -LiteralPath $tasOutput) { throw 'Revision output already exists; choose a new revision.' }
$tasCoreDll=Join-Path $tasCore 'SuperchargedPatch.dll'
if(-not (Test-Path -LiteralPath $tasCoreDll)) {throw 'Frozen core DLL missing.'}
$tasSources=@(Get-ChildItem -LiteralPath $tasSource -Recurse -Filter '*.cs' | Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' } | Sort-Object FullName)
if(-not $tasSources.Count) {throw 'No module sources found.'}
New-Item -ItemType Directory -Path $tasOutput | Out-Null
$tasSdk=(& dotnet --list-sdks | Select-Object -Last 1) -replace '^([^ ]+).*','$1'
$tasCsc=Join-Path 'C:/Program Files/dotnet/sdk' "$tasSdk/Roslyn/bincore/csc.dll"
$tasDll=Join-Path $tasOutput "$Name.$Revision.dll"
$tasArguments=@($tasCsc,'/nologo','/noconfig','/nostdlib+','/target:library','/langversion:7.3','/debug:portable','/optimize+','/deterministic+',('/out:'+$tasDll),('/reference:'+$tasCoreDll))
if($Define) {$tasArguments+='/define:'+$Define}
foreach($tasName in @('mscorlib.dll','System.dll','System.Core.dll','Assembly-CSharp.dll','Assembly-CSharp-firstpass.dll')) {$tasArguments+='/reference:'+(Join-Path $tasManaged $tasName)}
foreach($tasAssembly in @(Get-ChildItem -LiteralPath $tasManaged -Filter 'UnityEngine*.dll')) {$tasArguments+='/reference:'+$tasAssembly.FullName}
foreach($tasDependency in @((Join-Path $tasCore 'Thrift.dll'),(Join-Path $tasWorkspaceRoot 'runtime/BepInEx/core/0Harmony.dll'))) {$tasArguments+='/reference:'+$tasDependency}
$tasArguments+=@($tasSources | ForEach-Object {$_.FullName})
& dotnet @tasArguments
if($LASTEXITCODE -ne 0) {throw 'Authoring module compilation failed; output retained.'}
$tasManifest=[ordered]@{
 apiVersion=1; revision=$Revision; entryType=$EntryType; define=$Define
 builtUtc=[DateTime]::UtcNow.ToString('o'); dll=$tasDll
 sha256=(Get-FileHash -LiteralPath $tasDll -Algorithm SHA256).Hash
 coreDll=$tasCoreDll; coreSha256=(Get-FileHash -LiteralPath $tasCoreDll -Algorithm SHA256).Hash
 gameAssemblySha256=(Get-FileHash -LiteralPath (Join-Path $tasManaged 'Assembly-CSharp.dll') -Algorithm SHA256).Hash
 sources=@($tasSources | Get-FileHash -Algorithm SHA256 | Select-Object Path,Hash)
}
$tasManifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $tasOutput 'manifest.json') -Encoding utf8
$tasManifest | ConvertTo-Json -Depth 6
