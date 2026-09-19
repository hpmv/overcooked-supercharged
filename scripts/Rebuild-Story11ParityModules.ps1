param(
    [Parameter(Mandatory=$true)][string]$CoreBuild,
    [Parameter(Mandatory=$true)][string]$CoreTag
)

$ErrorActionPreference = 'Stop'
if ($CoreTag -notmatch '^[a-zA-Z0-9_-]+$') { throw 'Use a simple unique core tag.' }

$tasScriptParent = Split-Path -Parent $PSScriptRoot
if (Test-Path -LiteralPath (Join-Path $tasScriptParent '.git')) { $tasRepositoryRoot = $tasScriptParent }
elseif (Test-Path -LiteralPath (Join-Path $tasScriptParent 'framework/.git')) { $tasRepositoryRoot = Join-Path $tasScriptParent 'framework' }
else { throw 'Cannot locate the Supercharged repository root from the scripts directory.' }
$tasWorkspaceRoot = Split-Path -Parent $tasRepositoryRoot
$tasBuilder = Join-Path $PSScriptRoot 'Build-FrameworkModule.ps1'
$tasModules = @(
    @{ Name='LevelSession'; Source='level-session'; Revision="r2-core-$CoreTag-r44i"; Entry='SuperchargedPatch.Authoring.Modules.LevelSessionModule' },
    @{ Name='ScriptedRound'; Source='scripted-round'; Revision="r2-core-$CoreTag-r44i"; Entry='SuperchargedPatch.Authoring.Modules.ScriptedRoundModule' },
    @{ Name='RegistryObserver'; Source='registry-observer'; Revision="r2-core-$CoreTag-r44i"; Entry='SuperchargedPatch.Authoring.Modules.RegistryObserverModule' },
    @{ Name='Inspection'; Source='inspection'; Revision="r1-core-$CoreTag-r44i"; Entry='SuperchargedPatch.Authoring.Modules.InspectionModule' },
    @{ Name='WorldSyncCache'; Source='world-sync-cache'; Revision="r13v-one-shot-pause-diagnostic-core-$CoreTag"; Entry='SuperchargedPatch.Authoring.Modules.WorldSyncCacheModule' },
    @{ Name='LocalSyncBypass'; Source='local-sync-bypass'; Revision="r1-core-$CoreTag-r44i"; Entry='SuperchargedPatch.Authoring.Modules.LocalSyncBypassModule' },
    @{ Name='ChefPausePose'; Source='chef-pause-pose'; Revision="r4-core-$CoreTag-r44i"; Entry='SuperchargedPatch.Authoring.Modules.ChefPausePoseModule' },
    @{ Name='AuthoringPhysicsPauseGate'; Source='authoring-physics-pause-gate'; Revision="r2-core-$CoreTag-r44i"; Entry='SuperchargedPatch.Authoring.Modules.AuthoringPhysicsPauseGateModule' },
    @{ Name='PhysicsSyncAfterRestore'; Source='physics-sync-after-restore'; Revision="r13-core-$CoreTag-r44i"; Entry='SuperchargedPatch.Authoring.Modules.PhysicsSyncAfterRestoreModule' },
    @{ Name='RigidbodyMotionTarget'; Source='rigidbody-motion-target'; Revision="r1-core-$CoreTag-r44i"; Entry='SuperchargedPatch.Authoring.Modules.RigidbodyMotionTargetModule' },
    @{ Name='ChefMovementHistoryCheckpoint'; Source='chef-movement-history-checkpoint'; Revision="r3-core-$CoreTag-r44i"; Entry='SuperchargedPatch.Authoring.Modules.ChefMovementHistoryCheckpointModule' },
    @{ Name='RigidbodyActorRebuild'; Source='rigidbody-actor-rebuild'; Revision="r14h-target-frame-json-int64-focus-$CoreTag"; Entry='SuperchargedPatch.Authoring.Modules.RigidbodyActorRebuildModule' },
    @{ Name='ResumePhase'; Source='resume-phase'; Revision="r1bc-core-$CoreTag-r44i"; Entry='SuperchargedPatch.Authoring.Modules.ResumePhaseModule' },
    @{ Name='ChefAnimatorCheckpoint'; Source='chef-animator-checkpoint'; Revision="r57-scheduled-final-controller-core-$CoreTag"; Entry='SuperchargedPatch.Authoring.Modules.ChefAnimatorCheckpointModule' },
    @{ Name='AnimatorCheckpointInspector'; Source='animator-checkpoint-inspector'; Revision="r25b-update-zero-probe-core-$CoreTag-r44i"; Entry='SuperchargedPatch.Authoring.Modules.AnimatorCheckpointInspectorModule' },
    @{ Name='BodyRestore'; Source='body-restore'; Revision="r48-deferred-settling-mass-core-$CoreTag"; Entry='SuperchargedPatch.Authoring.Modules.BodyRestoreModule' },
    @{ Name='DeliveryFadeCheckpoint'; Source='delivery-fade-checkpoint'; Revision="r18e-signed-unity-material-ids-core-$CoreTag"; Entry='SuperchargedPatch.Authoring.Modules.DeliveryFadeCheckpointModule' },
    @{ Name='RoundEndCheckpoint'; Source='round-end-checkpoint'; Revision="dev5-core-$CoreTag-r44i"; Entry='SuperchargedPatch.Authoring.Modules.RoundEndCheckpointModule' }
)

foreach ($tasModule in $tasModules) {
    & $tasBuilder `
        -CoreBuild $CoreBuild `
        -SourceDirectory (Join-Path $tasRepositoryRoot ('modules/' + $tasModule.Source)) `
        -Revision $tasModule.Revision `
        -EntryType $tasModule.Entry `
        -Name $tasModule.Name
}

$tasModules | ForEach-Object {
    $tasDirectory = Join-Path $tasWorkspaceRoot ('framework-run/modules/' + $_.Name + '-' + $_.Revision)
    $tasManifest = Get-Content -LiteralPath (Join-Path $tasDirectory 'manifest.json') -Raw | ConvertFrom-Json
    [pscustomobject]@{
        directory = $tasDirectory
        sha256 = $tasManifest.sha256
        coreSha256 = $tasManifest.coreSha256
    }
} | ConvertTo-Json -Depth 3
