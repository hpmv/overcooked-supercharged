$ErrorActionPreference='Stop'
$taskRoot=Split-Path -Parent $PSScriptRoot
$taskGame=Join-Path $taskRoot 'runtime'
foreach ($taskProcess in @(Get-Process Overcooked2 -ErrorAction SilentlyContinue)) {
 if ($taskProcess.Path -eq (Join-Path $taskGame 'Overcooked2.exe')) {
  if (-not $taskProcess.WaitForExit(10000)) { throw 'The isolated game must be closed before rebuilding the plugin.' }
 }
}
$taskManaged=Join-Path $taskGame 'Overcooked2_Data\Managed'
$taskOutput=Join-Path $taskGame 'BepInEx\plugins\Oc2Tas.dll'
$taskSdk=(& dotnet --list-sdks | Select-Object -Last 1) -replace '^([^ ]+).*','$1'
$taskCsc=Join-Path 'C:\Program Files\dotnet\sdk' "$taskSdk\Roslyn\bincore\csc.dll"
$taskArgs=@($taskCsc,'/nologo','/noconfig','/nostdlib+','/target:library','/langversion:7.3','/debug:portable','/optimize+','/deterministic+',('/out:'+$taskOutput))
foreach ($taskDll in @('mscorlib.dll','System.dll','System.Core.dll','Assembly-CSharp.dll','Assembly-CSharp-firstpass.dll') ) { $taskArgs += '/reference:'+(Join-Path $taskManaged $taskDll) }
Get-ChildItem -LiteralPath $taskManaged -Filter 'UnityEngine*.dll' | ForEach-Object { $taskArgs += '/reference:'+$_.FullName }
foreach ($taskDll in @('BepInEx.dll','0Harmony.dll')) { $taskArgs += '/reference:'+(Join-Path $taskGame "BepInEx\core\$taskDll") }
Get-ChildItem -LiteralPath (Join-Path $taskRoot 'plugin') -Filter '*.cs' | ForEach-Object { $taskArgs += $_.FullName }
& dotnet @taskArgs
if ($LASTEXITCODE -ne 0) { throw 'Plugin compilation failed' }
if (Test-Path -LiteralPath (Join-Path $taskRoot 'controller\OvercookedTAS.Controller.csproj')) {
 & dotnet build (Join-Path $taskRoot 'controller\OvercookedTAS.Controller.csproj') -c Release --nologo
 if ($LASTEXITCODE -ne 0) { throw 'Controller compilation failed' }
}
Get-FileHash -LiteralPath $taskOutput -Algorithm SHA256 | Select-Object Path,Hash
$taskPluginHash=(Get-FileHash -LiteralPath $taskOutput -Algorithm SHA256).Hash
$taskManifestDirectory=Join-Path $taskRoot 'artifacts\builds'
New-Item -ItemType Directory -Force -Path $taskManifestDirectory | Out-Null
$taskManifest=[ordered]@{
 builtUtc=[DateTime]::UtcNow.ToString('o')
 sdk=$taskSdk
 plugin=(Get-FileHash -LiteralPath $taskOutput -Algorithm SHA256 | Select-Object Path,Hash)
 controller=(Get-FileHash -LiteralPath (Join-Path $taskRoot 'controller\bin\Release\net10.0\OvercookedTAS.Controller.dll') -Algorithm SHA256 | Select-Object Path,Hash)
 gameAssembly=(Get-FileHash -LiteralPath (Join-Path $taskManaged 'Assembly-CSharp.dll') -Algorithm SHA256 | Select-Object Path,Hash)
 source=@(Get-ChildItem -LiteralPath (Join-Path $taskRoot 'plugin') -Filter '*.cs' | Sort-Object Name | ForEach-Object { Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256 | Select-Object Path,Hash })
}
$taskManifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $taskManifestDirectory ($taskPluginHash+'.json')) -Encoding utf8
