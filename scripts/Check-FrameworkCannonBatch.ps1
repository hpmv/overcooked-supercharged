param(
 [string]$Plugin='artifacts/framework-cannon-batch-check/SuperchargedPatch.dll',
 [string]$Output='artifacts/framework-cannon-batch-contract.json'
)
$ErrorActionPreference='Stop'
$tasRoot=Split-Path -Parent $PSScriptRoot
$tasPlugin=Join-Path $tasRoot $Plugin
Add-Type -Path (Join-Path $tasRoot 'runtime/BepInEx/core/Mono.Cecil.dll')
$tasPatch=[Mono.Cecil.ModuleDefinition]::ReadModule($tasPlugin)
$tasChecks=[Collections.Generic.List[string]]::new()
function Check([bool]$Value,[string]$Name) {if(-not $Value){throw "Cannon batch contract failed: $Name"};$tasChecks.Add($Name)}
function Calls($Method,[string]$Type,[string]$Name) {
 @($Method.Body.Instructions | Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.DeclaringType.FullName -eq $Type -and $_.Operand.Name -eq $Name})
}
$tasCannon=$tasPatch.Types | Where-Object FullName -eq 'SuperchargedPatch.NativeCannonCheckpoint'
$tasCollector=$tasPatch.Types | Where-Object FullName -eq 'SuperchargedPatch.ActiveStateCollector'
$tasScope=$tasCollector.Methods | Where-Object Name -eq 'CollectDataForFrame'
$tasFinally=@($tasScope.Body.ExceptionHandlers | Where-Object HandlerType -eq 'Finally')
Check ($tasFinally.Count -eq 1) 'actual collector wrapper has one finally handler'
$tasBegin=@(Calls $tasScope $tasCannon.FullName 'BeginObservationBatch')
$tasEnd=@(Calls $tasScope $tasCannon.FullName 'EndObservationBatch')
$tasCore=@(Calls $tasScope $tasCollector.FullName 'CollectDataForFrameCore')
Check ($tasBegin.Count -eq 1 -and $tasCore.Count -eq 1 -and $tasEnd.Count -eq 1) 'compiled wrapper brackets its one collection call with Begin and End'
Check ($tasBegin[0].Offset -lt $tasFinally[0].TryStart.Offset) 'batch begin occurs before protected collector call'
Check ($tasCore[0].Offset -ge $tasFinally[0].TryStart.Offset -and $tasCore[0].Offset -lt $tasFinally[0].TryEnd.Offset) 'all collection work executes inside protected try'
Check ($tasEnd[0].Offset -ge $tasFinally[0].HandlerStart.Offset -and $tasEnd[0].Offset -lt $tasFinally[0].HandlerEnd.Offset) 'compiled finally clears batch on normal and exceptional exits'
foreach($tasName in @('ClearCacheAfterWarp','NotifyFrame')) {
 $tasMethod=$tasCollector.Methods | Where-Object Name -eq $tasName
 Check (@(Calls $tasMethod $tasCannon.FullName 'EndObservationBatch').Count -eq 1) ("actual collector invalidates batch on "+$tasName)
}
foreach($tasName in @('Validate','VerifyRestored')) {
 $tasMethod=$tasCannon.Methods | Where-Object Name -eq $tasName
 $tasReset=@(Calls $tasMethod $tasCannon.FullName 'EndObservationBatch');$tasRead=@(Calls $tasMethod $tasCannon.FullName 'CaptureAll')
 Check ($tasReset.Count -eq 1 -and $tasRead.Count -eq 1 -and $tasReset[0].Offset -lt $tasRead[0].Offset) ("warp "+$tasName+" invalidates batch before native enumeration")
}
$tasCapture=$tasCannon.Methods | Where-Object Name -eq 'CaptureAll'
Check (@(Calls $tasCapture 'UnityEngine.Object' 'FindObjectsOfType').Count -eq 1) 'CaptureAll still enumerates actual native cannon membership'
$tasBody=$tasCollector.Methods | Where-Object Name -eq 'CollectDataForFrameCore'
$tasAux=@(Calls $tasBody $tasCannon.FullName 'ObserveFrame');$tasKitchen=@(Calls $tasBody 'SuperchargedPatch.NativeKitchenCheckpoint' 'CaptureFrame')
Check ($tasAux.Count -eq 1 -and $tasKitchen.Count -eq 1 -and $tasAux[0].Offset -lt $tasKitchen[0].Offset) 'aux and kitchen observations retain their original synchronous order'
Check (@($tasPatch.AssemblyReferences | Where-Object {$_.Name -eq 'mscorlib' -and $_.Version.ToString() -eq '2.0.0.0'}).Count -eq 1) 'isolated plugin targets installed CLR2 mscorlib'
$tasResult=[ordered]@{
 passed=$true;checks=$tasChecks.Count;names=$tasChecks.ToArray();
 scope='Compiled control-flow contract plus linked production unit fixtures; native timing and parity require a new native probe.';
 plugin=[ordered]@{path=$tasPlugin;sha256=(Get-FileHash -LiteralPath $tasPlugin -Algorithm SHA256).Hash};
 source=@(foreach($tasName in @('NativeCannonCheckpoint.cs','ActiveStateCollector.cs')) {
  $tasPath=Join-Path $tasRoot ('framework/patch/'+$tasName)
  [ordered]@{path=$tasPath;sha256=(Get-FileHash -LiteralPath $tasPath -Algorithm SHA256).Hash}
 });
 behaviorTests=[ordered]@{path=(Join-Path $tasRoot 'artifacts/framework-cannon-batch-tests.json');sha256=(Get-FileHash -LiteralPath (Join-Path $tasRoot 'artifacts/framework-cannon-batch-tests.json') -Algorithm SHA256).Hash}
}
$tasResult | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $tasRoot $Output) -Encoding utf8
$tasPatch.Dispose()
[pscustomobject]@{passed=$true;checks=$tasChecks.Count;output=$Output}
