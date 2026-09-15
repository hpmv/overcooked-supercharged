param([int]$Port=14455,[int]$BridgePort=17636,[switch]$PlanOnly,[string]$PluginBuild,[switch]$Minimized,[switch]$SkipBackgroundPrime)
$ErrorActionPreference='Stop'
$tasRoot=Split-Path -Parent $PSScriptRoot
$tasGame=Join-Path $tasRoot 'lab\runtime'
$tasExe=Join-Path $tasGame 'Overcooked2.exe'
$tasRun=Join-Path $tasRoot 'framework-run'
$tasBuild=if ($PluginBuild) { (Resolve-Path -LiteralPath $PluginBuild).Path } else { Join-Path $tasRoot 'artifacts\framework-build' }
$tasPlugins=Join-Path $tasGame 'BepInEx\plugins'
if (@(Get-Process Overcooked2 -ErrorAction SilentlyContinue).Count) { throw 'Close existing Overcooked2 processes before this framework migration launch.' }
foreach ($tasPort in @($Port,$BridgePort)) {
 if (@(Get-NetTCPConnection -LocalPort $tasPort -State Listen -ErrorAction SilentlyContinue).Count) { throw "Port $tasPort is occupied." }
}
foreach ($tasFile in @($tasExe,(Join-Path $tasBuild 'SuperchargedPatch.dll'),(Join-Path $tasBuild 'manifest.json'))) {
 if (-not (Test-Path -LiteralPath $tasFile)) { throw "Missing $tasFile; run Build-Framework.ps1 first." }
}
$tasAllowed=@('Oc2Tas.dll','SuperchargedPatch.dll','Thrift.dll','Newtonsoft.Json.dll','System.Xml.dll')
$tasUnexpected=@(Get-ChildItem -LiteralPath $tasPlugins -Recurse -Filter '*.dll' | Where-Object { $_.Name -notin $tasAllowed })
if ($tasUnexpected.Count) { throw ('Unexpected lab plugins: '+($tasUnexpected.FullName -join ', ')) }
$tasPlan=[ordered]@{runtime=$tasExe;profileRoot=$tasRun;port=$Port;bridgePort=$BridgePort;fullscreen=$false;width=1280;height=720;minimized=[bool]$Minimized;backgroundPrimeRequested=[bool]($Minimized -and -not $SkipBackgroundPrime);upstream='49701883ff20755daddfb819d54710c91a6d5486'}
if ($PlanOnly) { [pscustomobject]$tasPlan; return }
New-Item -ItemType Directory -Force -Path $tasRun,(Join-Path $tasRun 'artifacts') | Out-Null
$tasOldPlugin=Join-Path $tasPlugins 'Oc2Tas.dll'
if (Test-Path -LiteralPath $tasOldPlugin) {
 $tasBackup=Join-Path $tasRoot 'artifacts\framework-migration\legacy-lab-plugin'
 New-Item -ItemType Directory -Force -Path $tasBackup | Out-Null
 $tasOldHash=(Get-FileHash -LiteralPath $tasOldPlugin -Algorithm SHA256).Hash
 $tasBackupFile=Join-Path $tasBackup ($tasOldHash+'.dll')
 if (Test-Path -LiteralPath $tasBackupFile) {
  if ((Get-FileHash -LiteralPath $tasBackupFile -Algorithm SHA256).Hash -ne $tasOldHash) { throw 'Backup hash mismatch.' }
  $tasBackupFile=Join-Path $tasBackup ($tasOldHash+'-'+[Guid]::NewGuid().ToString('N')+'.dll')
 }
 # Both explicit, nonrecursive paths are inside this workspace; retain the old binary.
 Move-Item -LiteralPath $tasOldPlugin -Destination $tasBackupFile
 $tasPlan.legacyPluginBackup=$tasBackupFile
}
# Retire compatibility dependencies no longer referenced by the slim bridge.
foreach ($tasRetired in @('Newtonsoft.Json.dll','System.Xml.dll')) {
 $tasRetiredPath=Join-Path $tasPlugins $tasRetired
 if (Test-Path -LiteralPath $tasRetiredPath) {
  $tasDependencyBackup=Join-Path $tasRoot 'artifacts\framework-migration\retired-dependencies'
  New-Item -ItemType Directory -Force -Path $tasDependencyBackup | Out-Null
  $tasRetiredDestination=Join-Path $tasDependencyBackup ([Guid]::NewGuid().ToString('N')+'-'+$tasRetired)
  Move-Item -LiteralPath $tasRetiredPath -Destination $tasRetiredDestination
 }
}
foreach ($tasDll in @('SuperchargedPatch.dll','Thrift.dll')) {
 Copy-Item -LiteralPath (Join-Path $tasBuild $tasDll) -Destination (Join-Path $tasPlugins $tasDll) -Force
}
$env:SteamAppId='728880'
$env:OC2SC_ROOT=$tasRun
$env:OC2SC_PORT=$Port.ToString()
$env:OC2SC_BRIDGE_PORT=$BridgePort.ToString()
$tasArguments=@('-screen-fullscreen','0','-screen-width','1280','-screen-height','720','-logFile',(Join-Path $tasRun 'artifacts\player.log'))
$tasWindowStyle=if ($Minimized) { 'Minimized' } else { 'Normal' }
$tasProcess=Start-Process -FilePath $tasExe -WorkingDirectory $tasGame -ArgumentList $tasArguments -WindowStyle $tasWindowStyle -PassThru
$tasPlan.pid=$tasProcess.Id
$tasPlan.startedUtc=[DateTime]::UtcNow.ToString('o')
$tasPlan.startTicks=$tasProcess.StartTime.ToUniversalTime().Ticks
$tasPlan.pluginHash=(Get-FileHash -LiteralPath (Join-Path $tasPlugins 'SuperchargedPatch.dll') -Algorithm SHA256).Hash
$tasPlan | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $tasRun 'artifacts\process.json') -Encoding utf8
if ($Minimized -and -not $SkipBackgroundPrime) {
 $tasDeadline=(Get-Date).AddSeconds(30)
 do {
  $tasListener=@(Get-NetTCPConnection -LocalPort $BridgePort -State Listen -ErrorAction SilentlyContinue | Where-Object { $_.OwningProcess -eq $tasProcess.Id })
  if ($tasListener.Count) { break }
  if ($tasProcess.HasExited) { throw 'Overcooked exited before the background primer became available.' }
  Start-Sleep -Milliseconds 100
 } while ((Get-Date) -lt $tasDeadline)
 if (-not $tasListener.Count) { throw 'Timed out waiting for the framework background primer.' }
 $tasPrimePath=Join-Path $tasRun ('artifacts\background-prime-'+$tasProcess.Id+'.json')
 & python (Join-Path $tasRoot 'scripts\framework_prime_background.py') --out $tasPrimePath --bridge-port $BridgePort --timeout 10 | Out-Null
 if ($LASTEXITCODE -ne 0) { throw 'Framework background primer failed; inspect '+$tasPrimePath }
 $tasPlan['backgroundPrime']=(Get-Content -LiteralPath $tasPrimePath -Raw | ConvertFrom-Json)
 $tasPlan['backgroundPrimePath']=$tasPrimePath
 $tasPlan | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $tasRun 'artifacts\process.json') -Encoding utf8
}
[pscustomobject]$tasPlan
