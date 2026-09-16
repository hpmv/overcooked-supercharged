param([switch]$Restore,[string[]]$ExcludeSource=@(),[string]$Output='')
$ErrorActionPreference='Stop'
$tasRoot=Split-Path -Parent $PSScriptRoot
$tasFramework=Join-Path $tasRoot 'framework'
$tasManaged=Join-Path $tasRoot 'runtime\Overcooked2_Data\Managed'
$tasOutput=if($Output){Join-Path $tasRoot $Output}else{Join-Path $tasRoot 'artifacts\framework-build'}
New-Item -ItemType Directory -Force -Path $tasOutput | Out-Null
if ($Restore) {
 & dotnet restore (Join-Path $tasFramework 'PluginDependencies.csproj') --packages (Join-Path $tasFramework '.packages') --nologo
 if ($LASTEXITCODE -ne 0) { throw 'Framework dependencies failed to restore.' }
}
$tasThrift=Join-Path $tasFramework '.packages\thrift\0.9.1.3\lib\net35\Thrift.dll'
foreach ($tasDependency in @($tasThrift)) {
 if (-not (Test-Path -LiteralPath $tasDependency)) { throw "Missing dependency $tasDependency. Run with -Restore." }
}
Push-Location (Join-Path $tasFramework 'patch')
try {
 & .\thrift.exe -gen csharp ../common/game.thrift
 if ($LASTEXITCODE -ne 0) { throw 'Thrift plugin schema generation failed.' }
} finally { Pop-Location }
$tasSdk=(& dotnet --list-sdks | Select-Object -Last 1) -replace '^([^ ]+).*','$1'
$tasCsc=Join-Path 'C:\Program Files\dotnet\sdk' "$tasSdk\Roslyn\bincore\csc.dll"
$tasCompilerArgs=@($tasCsc,'/nologo','/noconfig','/nostdlib+','/target:library','/langversion:7.3','/debug:portable','/optimize+','/deterministic+',('/out:'+(Join-Path $tasOutput 'SuperchargedPatch.dll')))
foreach ($tasDll in @('mscorlib.dll','System.dll','System.Core.dll','Assembly-CSharp.dll','Assembly-CSharp-firstpass.dll')) { $tasCompilerArgs += '/reference:'+(Join-Path $tasManaged $tasDll) }
Get-ChildItem -LiteralPath $tasManaged -Filter 'UnityEngine*.dll' | ForEach-Object { $tasCompilerArgs += '/reference:'+$_.FullName }
foreach ($tasDll in @('BepInEx.dll','0Harmony.dll','BepInEx.Harmony.dll')) { $tasCompilerArgs += '/reference:'+(Join-Path $tasRoot "runtime\BepInEx\core\$tasDll") }
foreach ($tasDll in @($tasThrift)) { $tasCompilerArgs += '/reference:'+$tasDll }
$tasSources=@(Get-ChildItem -LiteralPath (Join-Path $tasFramework 'patch') -Recurse -Filter '*.cs' | Where-Object { $_.FullName -notmatch '\\(obj|bin|Libs)\\' })
if (Test-Path -LiteralPath (Join-Path $tasFramework 'bridge')) { $tasSources += @(Get-ChildItem -LiteralPath (Join-Path $tasFramework 'bridge') -Filter '*.cs') }
$tasExcludedPaths=@($ExcludeSource | ForEach-Object { (Resolve-Path -LiteralPath $_).Path })
$tasSources=@($tasSources | Where-Object { $_.FullName -notin $tasExcludedPaths })
foreach ($tasSource in $tasSources) { $tasCompilerArgs += $tasSource.FullName }
& dotnet @tasCompilerArgs
if ($LASTEXITCODE -ne 0) { throw 'Framework plugin compilation failed.' }
Copy-Item -LiteralPath $tasThrift -Destination $tasOutput -Force
$tasManifest=[ordered]@{
 builtUtc=[DateTime]::UtcNow.ToString('o'); upstream='49701883ff20755daddfb819d54710c91a6d5486'
 excludedExperimentalSources=$tasExcludedPaths
 gameAssembly=(Get-FileHash -LiteralPath (Join-Path $tasManaged 'Assembly-CSharp.dll') -Algorithm SHA256).Hash
 files=@(@('SuperchargedPatch.dll','Thrift.dll') | ForEach-Object { Get-FileHash -LiteralPath (Join-Path $tasOutput $_) -Algorithm SHA256 | Select-Object Path,Hash })
 sources=@($tasSources | Sort-Object FullName | Get-FileHash -Algorithm SHA256 | Select-Object Path,Hash)
}
$tasManifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $tasOutput 'manifest.json') -Encoding utf8
$tasManifest.files
