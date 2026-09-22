param(
 [string]$Module='framework-run/modules/LevelSession-r4b-early-tutorial-skip-core-bg4-v27-r44i/LevelSession.r4b-early-tutorial-skip-core-bg4-v27-r44i.dll'
)
$ErrorActionPreference='Stop'
$scriptParent=Split-Path -Parent $PSScriptRoot
if(Test-Path -LiteralPath (Join-Path $scriptParent '.git')){$repo=$scriptParent;$workspace=Split-Path -Parent $repo}
elseif(Test-Path -LiteralPath (Join-Path $scriptParent 'framework/.git')){$workspace=$scriptParent;$repo=Join-Path $workspace 'framework'}
else{throw 'Cannot locate the Supercharged repository and workspace roots.'}
$native=Join-Path $workspace 'runtime/Overcooked2_Data/Managed/Assembly-CSharp.dll'
$cecil=Join-Path $workspace 'runtime/BepInEx/core/Mono.Cecil.dll'
$candidate=if([IO.Path]::IsPathRooted($Module)){$Module}else{Join-Path $workspace $Module}
foreach($path in @($native,$cecil,$candidate)){if(-not(Test-Path -LiteralPath $path)){throw "Missing $path"}}
Add-Type -Path $cecil
$game=[Mono.Cecil.AssemblyDefinition]::ReadAssembly($native)
$built=[Mono.Cecil.AssemblyDefinition]::ReadAssembly($candidate)
$checks=[Collections.Generic.List[string]]::new()
function Check([bool]$pass,[string]$name){if(-not$pass){throw $name};$checks.Add($name)}
try {
 $nativeHash=(Get-FileHash -LiteralPath $native -Algorithm SHA256).Hash
 Check ($nativeHash -eq '9BB6A3791331201D32CA89C3509F019A9780309DA7110002F04020E8491E1908') 'Installed game assembly is the audited revision'
 Check ($game.MainModule.Mvid.ToString() -eq '114d606a-29d5-472c-8ebe-c2c20fbe3be3') 'Installed game assembly MVID is audited'
 $flow=$game.MainModule.Types|Where-Object FullName -eq 'LevelIntroFlowroutine'
 $target=$flow.Methods|Where-Object Name -eq 'TutorialDismissRoutine'
 Check ($null-ne$target-and$target.IsPrivate-and-not$target.IsStatic-and$target.ReturnType.FullName-eq'System.Collections.IEnumerator'-and$target.Parameters.Count-eq1-and$target.Parameters[0].ParameterType.FullName-eq'UnityEngine.GameObject') 'Exact private native tutorial wait seam exists'
 $client=$game.MainModule.Types|Where-Object FullName -eq 'ClientTutorialPopupController'
 $shutdown=$client.Methods|Where-Object Name -eq 'Shutdown'
 Check ($null-ne$shutdown-and$shutdown.Parameters.Count-eq0-and$shutdown.ReturnType.FullName-eq'System.Void') 'Exact native popup shutdown seam exists'
 $runIterator=$client.NestedTypes|Where-Object {$_.Name-like'<RunTutorial>*'}
 $move=$runIterator.Methods|Where-Object Name -eq 'MoveNext'
 $pauseCalls=@($move.Body.Instructions|Where-Object {$_.Operand-is[Mono.Cecil.MethodReference]-and$_.Operand.DeclaringType.FullName-eq'TimeManager'-and$_.Operand.Name-eq'SetPaused'})
 Check ($pauseCalls.Count-eq4) 'Native tutorial coroutine owns two pause acquisitions and two releases'
 Check (@($move.Body.Instructions|Where-Object {$_.Operand-is[Mono.Cecil.MethodReference]-and$_.Operand.DeclaringType.FullName-eq'TutorialPopupController'-and$_.Operand.Name-eq'OnTutorialDismissed'}).Count-eq1) 'Native tutorial coroutine owns the dismissal callback'
 Check (@($move.Body.Instructions|Where-Object {$_.Operand-is[Mono.Cecil.MethodReference]-and$_.Operand.Name-eq'set_enabled'}).Count-eq4) 'Native tutorial coroutine owns canvas disable and enable'
 $policy=$built.MainModule.Types|Where-Object FullName -eq 'SuperchargedPatch.Authoring.Modules.StoryTutorialSkipPolicy'
 Check ($null-ne$policy) 'Compiled module contains the scoped tutorial policy'
 $instructions=@($policy.Methods|Where-Object HasBody|ForEach-Object {$_.Body.Instructions})
 Check (@($instructions|Where-Object {$_.Operand-is[Mono.Cecil.MethodReference]-and$_.Operand.DeclaringType.FullName-eq'HarmonyLib.Harmony'-and$_.Operand.Name-eq'Patch'}).Count-eq2) 'Policy patches only wait and shutdown observation seams'
 $banned=@('SetPaused','Shutdown','OnTutorialDismissed','SetActive','Destroy','set_enabled')
 Check (@($instructions|Where-Object {$_.Operand-is[Mono.Cecil.MethodReference]-and$_.Operand.Name-in$banned}).Count-eq0) 'Policy directly calls no native popup pause canvas dismissal or shutdown mutator'
 Check (@($instructions|Where-Object {$_.OpCode.Code-eq[Mono.Cecil.Cil.Code]::Stfld-and$_.Operand-is[Mono.Cecil.FieldReference]-and$_.Operand.DeclaringType.Scope.Name-eq'Assembly-CSharp'}).Count-eq0) 'Policy writes no installed game field'
 $prefix=$policy.Methods|Where-Object Name -eq 'BeforeTutorialDismissRoutine'
 Check ($prefix.ReturnType.FullName-eq'System.Boolean'-and$prefix.Parameters[-1].ParameterType.FullName-eq'System.Collections.IEnumerator&') 'Harmony prefix can substitute only the returned wait enumerator'
 Check ($policy.Methods.Name-contains'ImmediateComplete') 'Replacement enumerator is explicit and bounded'
 $dispose=$policy.Methods|Where-Object Name -eq 'Dispose'
 Check (@($dispose.Body.Instructions|Where-Object {$_.Operand-is[Mono.Cecil.MethodReference]-and$_.Operand.Name-eq'UnpatchSelf'}).Count-eq1) 'Policy disposal unpatches its Harmony owner'
 $moduleType=$built.MainModule.Types|Where-Object FullName -eq 'SuperchargedPatch.Authoring.Modules.LevelSessionModule'
 $run=$moduleType.Methods|Where-Object Name -eq 'Run'
 $runIteratorBuilt=$moduleType.NestedTypes|Where-Object {$_.Name-like'<Run>*'}
 $runMove=$runIteratorBuilt.Methods|Where-Object Name -eq 'MoveNext'
 $policyCalls=@($runMove.Body.Instructions|Where-Object {$_.Operand-is[Mono.Cecil.MethodReference]-and$_.Operand.DeclaringType.FullName-eq'SuperchargedPatch.Authoring.Modules.StoryTutorialSkipPolicy'}|ForEach-Object {$_.Operand.Name})
 foreach($required in @('Arm','ObserveCleanup','RequireCompleted')){Check ($policyCalls-contains$required) "Level-session lifecycle invokes $required"}
 [pscustomobject]@{passed=$true;scope='Installed native tutorial lifecycle plus compiled external policy IL';count=$checks.Count;checks=$checks;nativeAssembly=$native;nativeSha256=$nativeHash;module=$candidate;moduleSha256=(Get-FileHash -LiteralPath $candidate -Algorithm SHA256).Hash}|ConvertTo-Json -Depth 5
}
finally {$game.Dispose();$built.Dispose()}
