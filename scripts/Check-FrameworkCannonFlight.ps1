param(
 [string]$Plugin='artifacts/framework-flight-check/SuperchargedPatch.dll',
 [string]$Output='artifacts/framework-cannon-flight-source-contract.json'
)
$ErrorActionPreference='Stop'
$tasRoot=Split-Path -Parent $PSScriptRoot
$tasGame=Join-Path $tasRoot 'runtime/Overcooked2_Data/Managed/Assembly-CSharp.dll'
$tasPlugin=Join-Path $tasRoot $Plugin
$tasSource=Join-Path $tasRoot 'framework/patch/NativeCannonFlightCheckpoint.cs'
Add-Type -Path (Join-Path $tasRoot 'runtime/BepInEx/core/Mono.Cecil.dll')
$tasModule=[Mono.Cecil.ModuleDefinition]::ReadModule($tasGame)
$tasPatch=[Mono.Cecil.ModuleDefinition]::ReadModule($tasPlugin)
$tasChecks=[Collections.Generic.List[string]]::new()
function Check([bool]$Value,[string]$Name) {
 if(-not $Value){throw "Cannon flight contract failed: $Name"}
 $tasChecks.Add($Name)
}
function AllTypes($Types) {
 foreach($tasType in $Types){$tasType;AllTypes $tasType.NestedTypes}
}
$tasNativeTypes=@(AllTypes $tasModule.Types)
$tasPluginTypes=@(AllTypes $tasPatch.Types)
$tasText=Get-Content -LiteralPath $tasSource -Raw
$tasGuid=[regex]::Match($tasText,'new Guid\("([^"]+)"\)').Groups[1].Value
Check ($tasModule.Mvid.ToString() -eq $tasGuid) 'installed native module matches explicitly pinned iterator version'
$tasLayouts=@{}
foreach($tasPair in @(@('launchFields','ClientCannon/<LaunchProjectile>c__Iterator1'),@('animationFields','ProjectileAnimation/<Run>c__Iterator0'))){
 $tasType=$tasNativeTypes | Where-Object FullName -eq $tasPair[1]
 Check ($null -ne $tasType) ("installed iterator exists: "+$tasPair[1])
 $tasList=[regex]::Match($tasText,($tasPair[0]+'\s*=\s*\{([^}]+)\}')).Groups[1].Value
 $tasNames=@([regex]::Matches($tasList,'"([^"]+)"') | ForEach-Object {$_.Groups[1].Value} | Sort-Object)
 $tasActual=@($tasType.Fields | Where-Object {-not $_.IsStatic} | ForEach-Object Name | Sort-Object)
 Check (($tasActual -join '|') -ceq ($tasNames -join '|')) ("exact complete native field schema: "+$tasPair[1])
 $tasLayouts[$tasPair[1]]=@($tasType.Fields | ForEach-Object { [ordered]@{name=$_.Name;type=$_.FieldType.FullName} })
}
foreach($tasMatch in [regex]::Matches($tasText,'Read\(typeof\(([^)]+)\),[^\r\n]+?,\s*"([^"]+)"\)')){
 $tasName=$tasMatch.Groups[1].Value;$tasField=$tasMatch.Groups[2].Value
 $tasType=$tasNativeTypes | Where-Object Name -eq $tasName
 $tasFound=$false
 while($null -ne $tasType){
  if(@($tasType.Fields | Where-Object Name -eq $tasField).Count){$tasFound=$true;break}
  if($null -eq $tasType.BaseType){break}
  $tasType=$tasNativeTypes | Where-Object FullName -eq $tasType.BaseType.FullName
 }
 Check $tasFound ("installed reflected native field: "+$tasName+'.'+$tasField)
}
$tasControls=$tasNativeTypes | Where-Object FullName -eq 'PlayerControls'
$tasSelected=[regex]::Match($tasText,'controlFields\s*=\s*\{([^}]+)\}').Groups[1].Value
foreach($tasName in @([regex]::Matches($tasSelected,'"([^"]+)"') | ForEach-Object {$_.Groups[1].Value})){
 Check (@($tasControls.Fields | Where-Object Name -eq $tasName).Count -eq 1) ("installed selected control field: "+$tasName)
}
$tasHelper=$tasPluginTypes | Where-Object {$_.FullName -eq 'SuperchargedPatch.NativeCannonFlightCheckpoint' -or $_.FullName.StartsWith('SuperchargedPatch.NativeCannonFlightCheckpoint/') -or $_.FullName -eq 'SuperchargedPatch.NativeIteratorCopy' -or $_.FullName.StartsWith('SuperchargedPatch.NativeIteratorCopy/')}
Check (@($tasHelper).Count -ge 2) 'compiled plugin includes both actual production helpers'
$tasCalls=@(foreach($tasType in $tasHelper){foreach($tasMethod in $tasType.Methods){if($tasMethod.HasBody){foreach($tasInstruction in $tasMethod.Body.Instructions){if($tasInstruction.Operand -is [Mono.Cecil.MethodReference]){$tasInstruction.Operand}}}}})
$tasForbidden=@($tasCalls | Where-Object {
 $_.Name -in @('StartCoroutine','StopCoroutine','Load','Launch','Land','EndCannonRoutine','DeliverPlate','OnInteractionStart','OnInteractionEnd') -or
 ($_.Name -eq 'MoveNext' -and $_.DeclaringType.FullName -eq 'System.Collections.IEnumerator')
})
# Ordinary foreach over captured field lists emits generic enumerator MoveNext
# and IDisposable.Dispose; those are not the saved native IEnumerator graph.
Check ($tasForbidden.Count -eq 0) 'compiled helper calls no native IEnumerator continuation, coroutine scheduler, load, fire, landing or delivery callback'
Check (@($tasCalls | Where-Object {$_.DeclaringType.FullName -eq 'System.Activator'}).Count -eq 0) 'compiled helper does not construct native sessions through Activator'
Check (@($tasPatch.AssemblyReferences | Where-Object {$_.Name -eq 'mscorlib' -and $_.Version.ToString() -eq '2.0.0.0'}).Count -eq 1) 'compiled plugin targets installed CLR2 mscorlib'
$tasChef=$tasNativeTypes | Where-Object FullName -eq 'Team17.Online.Multiplayer.Messaging.ClientChefSynchroniser'
$tasResume=$tasChef.Methods | Where-Object Name -eq 'ApplyResumeData'
Check (@($tasResume.Body.Instructions | Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.FullName -match 'UnityEngine.Time::get_time'}).Count -gt 0) 'known raw Unity Time.time resume-clock limitation remains explicitly evidenced'
$tasResult=[ordered]@{
 passed=$true;checks=$tasChecks.Count;names=$tasChecks.ToArray();
 scope='Installed metadata and compiled call-contract checks only; no native flight or scheduler execution.';
 nativeGame=[ordered]@{path=$tasGame;sha256=(Get-FileHash -LiteralPath $tasGame -Algorithm SHA256).Hash;moduleVersionId=$tasModule.Mvid.ToString()};
 plugin=[ordered]@{path=$tasPlugin;sha256=(Get-FileHash -LiteralPath $tasPlugin -Algorithm SHA256).Hash};
 source=[ordered]@{path=$tasSource;sha256=(Get-FileHash -LiteralPath $tasSource -Algorithm SHA256).Hash};
 iteratorLayouts=$tasLayouts
}
$tasResult | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $tasRoot $Output) -Encoding utf8
$tasModule.Dispose();$tasPatch.Dispose()
$tasResult | Select-Object passed,checks,scope
