param(
 [string]$Plugin='artifacts/framework-round-end-latch-check/SuperchargedPatch.dll',
 [string]$Output='artifacts/framework-round-end-latch-source-contract.json'
)
$ErrorActionPreference='Stop'
$tasRoot=Split-Path -Parent $PSScriptRoot
if(-not (Test-Path -LiteralPath (Join-Path $tasRoot 'runtime'))){$tasRoot=Split-Path -Parent $tasRoot}
$tasGame=Join-Path $tasRoot 'runtime/Overcooked2_Data/Managed/Assembly-CSharp.dll'
$tasPlugin=Join-Path $tasRoot $Plugin
$tasSource=Join-Path $tasRoot 'framework/patch/NativeRoundEndLatch.cs'
Add-Type -Path (Join-Path $tasRoot 'runtime/BepInEx/core/Mono.Cecil.dll')
$tasGameHash=(Get-FileHash -LiteralPath $tasGame -Algorithm SHA256).Hash
$tasModule=[Mono.Cecil.ModuleDefinition]::ReadModule($tasGame)
$tasPatch=[Mono.Cecil.ModuleDefinition]::ReadModule($tasPlugin)
$tasChecks=[Collections.Generic.List[string]]::new()
function Check([bool]$Value,[string]$Name) {
 if(-not $Value){throw "Round-end latch contract failed: $Name"}
 $tasChecks.Add($Name)
}
function AllTypes($Types) {
 foreach($tasType in $Types){$tasType;AllTypes $tasType.NestedTypes}
}
function ExactFields($Types,[string]$FullName,[string[]]$Expected) {
 $tasType=$Types | Where-Object FullName -eq $FullName
 Check ($null -ne $tasType) ("installed iterator exists: "+$FullName)
 $tasActual=@($tasType.Fields | Where-Object {-not $_.IsStatic} | ForEach-Object Name | Sort-Object)
 Check (($tasActual -join '|') -ceq (@($Expected | Sort-Object) -join '|')) ("exact complete native field schema: "+$FullName)
 return $tasType
}
$tasNativeTypes=@(AllTypes $tasModule.Types)
$tasPluginTypes=@(AllTypes $tasPatch.Types)
Check ($tasGameHash -ceq '9BB6A3791331201D32CA89C3509F019A9780309DA7110002F04020E8491E1908') 'installed game assembly is the disassembled pinned build'
$tasSimple=@('$this','$current','$disposing','$PC')
$tasServer=ExactFields $tasNativeTypes 'ServerFlowControllerBase/<RunRound>c__Iterator0' $tasSimple
$tasClient=ExactFields $tasNativeTypes 'ClientFlowControllerBase/<RunLevel>c__Iterator1' $tasSimple
$tasOutro=ExactFields $tasNativeTypes 'ClientFlowControllerBase/<RunLevelEnd>c__Iterator2' @('<outro>__0','$this','$current','$disposing','$PC')
foreach($tasName in @(
 'SuperchargedPatch.NativeRoundEndLatch',
 'SuperchargedPatch.NativeRoundEndLatch/Target',
 'SuperchargedPatch.NativeRoundEndServerActivationPatch',
 'SuperchargedPatch.NativeRoundEndClientStatePatch',
 'SuperchargedPatch.NativeRoundEndClientTimerZeroPatch')) {
 Check (@($tasPluginTypes | Where-Object FullName -eq $tasName).Count -eq 1) ("compiled plugin contains "+$tasName)
}
$tasLatch=$tasPluginTypes | Where-Object FullName -eq 'SuperchargedPatch.NativeRoundEndLatch'
$tasRestore=$tasLatch.Methods | Where-Object Name -eq 'Restore'
Check (@($tasRestore).Count -eq 1) 'compiled latch has one lifecycle Restore method'
$tasRestoreCalls=@($tasRestore.Body.Instructions | Where-Object {$_.Operand -is [Mono.Cecil.MethodReference]} | ForEach-Object Operand)
$tasForbidden=@($tasRestoreCalls | Where-Object {
 $_.Name -in @('ChangeGameState','StartCoroutine','StopCoroutine','MoveNext','RunLevel','RunLevelEnd','RunRound')
})
Check ($tasForbidden.Count -eq 0) 'compiled lifecycle restore invokes no state transition, coroutine scheduler or iterator continuation'
$tasController=$tasPluginTypes | Where-Object FullName -eq 'SuperchargedPatch.ControllerHandler'
$tasLate=$tasController.Methods | Where-Object Name -eq 'LateUpdate'
Check (@($tasLate).Count -eq 1) 'compiled controller has one LateUpdate'
$tasInstructions=@($tasLate.Body.Instructions)
$tasLastIndex=-1;$tasLatchIndex=-1;$tasNextIndex=-1
for($tasI=0;$tasI -lt $tasInstructions.Count;$tasI++) {
 $tasOperand=$tasInstructions[$tasI].Operand
 if($tasOperand -isnot [Mono.Cecil.MethodReference]){continue}
 if($tasOperand.Name -eq 'set_LastFramePaused'){$tasLastIndex=$tasI}
 if($tasOperand.Name -eq 'ApplyPendingPause'){$tasLatchIndex=$tasI}
 if($tasOperand.Name -eq 'set_NextFramePaused'){$tasNextIndex=$tasI}
}
Check ($tasLastIndex -ge 0 -and $tasLastIndex -lt $tasLatchIndex -and $tasLatchIndex -lt $tasNextIndex) 'terminal latch runs between LastFramePaused and NextFramePaused capture'
foreach($tasPatchName in @('SuperchargedPatch.NativeRoundEndServerActivationPatch','SuperchargedPatch.NativeRoundEndClientStatePatch','SuperchargedPatch.NativeRoundEndClientTimerZeroPatch')) {
 $tasType=$tasPluginTypes | Where-Object FullName -eq $tasPatchName
 Check (@($tasType.CustomAttributes | Where-Object {$_.AttributeType.FullName -eq 'HarmonyLib.HarmonyPatch'}).Count -gt 0) ("Harmony patch marker exists: "+$tasPatchName)
}
Check (@($tasPatch.AssemblyReferences | Where-Object {$_.Name -eq 'mscorlib' -and $_.Version.ToString() -eq '2.0.0.0'}).Count -eq 1) 'compiled plugin targets installed CLR2 mscorlib'
$tasResult=[ordered]@{
 passed=$true;checks=$tasChecks.Count;names=$tasChecks.ToArray();
 scope='Pinned installed metadata and compiled control-flow contract only; no live Unity lifecycle execution.';
 nativeGame=[ordered]@{path=$tasGame;sha256=$tasGameHash;moduleVersionId=$tasModule.Mvid.ToString()};
 plugin=[ordered]@{path=$tasPlugin;sha256=(Get-FileHash -LiteralPath $tasPlugin -Algorithm SHA256).Hash};
 source=[ordered]@{path=$tasSource;sha256=(Get-FileHash -LiteralPath $tasSource -Algorithm SHA256).Hash};
 iteratorLayouts=[ordered]@{
  server=@($tasServer.Fields | ForEach-Object {[ordered]@{name=$_.Name;type=$_.FieldType.FullName}});
  client=@($tasClient.Fields | ForEach-Object {[ordered]@{name=$_.Name;type=$_.FieldType.FullName}});
  outro=@($tasOutro.Fields | ForEach-Object {[ordered]@{name=$_.Name;type=$_.FieldType.FullName}})
 }
}
$tasResult | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $tasRoot $Output) -Encoding utf8
$tasModule.Dispose();$tasPatch.Dispose()
$tasResult | Select-Object passed,checks,scope
