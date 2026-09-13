param(
    [Parameter(Mandatory=$true)][string]$CoreBuild,
    [Parameter(Mandatory=$true)][string]$CoreTag
)

$ErrorActionPreference = 'Stop'
if ($CoreTag -notmatch '^[a-zA-Z0-9_-]+$') { throw 'Use a simple unique core tag.' }

$tasRoot = Split-Path -Parent $PSScriptRoot
$tasBuilder = Join-Path $PSScriptRoot 'Build-FrameworkModule.ps1'
$tasModules = @(
    @{ Name='LevelSession'; Source='level-session'; Revision="r2-core-$CoreTag"; Entry='SuperchargedPatch.Authoring.Modules.LevelSessionModule' },
    @{ Name='ScriptedRound'; Source='scripted-round'; Revision="r2-core-$CoreTag"; Entry='SuperchargedPatch.Authoring.Modules.ScriptedRoundModule' },
    @{ Name='RegistryObserver'; Source='registry-observer'; Revision="r2-core-$CoreTag"; Entry='SuperchargedPatch.Authoring.Modules.RegistryObserverModule' },
    @{ Name='Inspection'; Source='inspection'; Revision="r1-core-$CoreTag"; Entry='SuperchargedPatch.Authoring.Modules.InspectionModule' },
    @{ Name='WorldSyncCache'; Source='world-sync-cache'; Revision="r13d-live-fixed-membership-core-$CoreTag"; Entry='SuperchargedPatch.Authoring.Modules.WorldSyncCacheModule' },
    @{ Name='LocalSyncBypass'; Source='local-sync-bypass'; Revision="r1-core-$CoreTag"; Entry='SuperchargedPatch.Authoring.Modules.LocalSyncBypassModule' },
    @{ Name='ChefPausePose'; Source='chef-pause-pose'; Revision="r4-core-$CoreTag"; Entry='SuperchargedPatch.Authoring.Modules.ChefPausePoseModule' },
    @{ Name='PhysicsSyncAfterRestore'; Source='physics-sync-after-restore'; Revision="r13-core-$CoreTag"; Entry='SuperchargedPatch.Authoring.Modules.PhysicsSyncAfterRestoreModule' },
    @{ Name='RigidbodyMotionTarget'; Source='rigidbody-motion-target'; Revision="r1-core-$CoreTag"; Entry='SuperchargedPatch.Authoring.Modules.RigidbodyMotionTargetModule' },
    @{ Name='ChefMovementHistoryCheckpoint'; Source='chef-movement-history-checkpoint'; Revision="r3-core-$CoreTag"; Entry='SuperchargedPatch.Authoring.Modules.ChefMovementHistoryCheckpointModule' },
    @{ Name='RigidbodyActorRebuild'; Source='rigidbody-actor-rebuild'; Revision="r13y-core-$CoreTag"; Entry='SuperchargedPatch.Authoring.Modules.RigidbodyActorRebuildModule' },
    @{ Name='ResumePhase'; Source='resume-phase'; Revision="r1bc-core-$CoreTag"; Entry='SuperchargedPatch.Authoring.Modules.ResumePhaseModule' },
    @{ Name='ChefAnimatorCheckpoint'; Source='chef-animator-checkpoint'; Revision="r53b-core-$CoreTag"; Entry='SuperchargedPatch.Authoring.Modules.ChefAnimatorCheckpointModule' },
    @{ Name='BodyRestore'; Source='body-restore'; Revision="r32-recreated-native-shape-state-rebind-core-$CoreTag"; Entry='SuperchargedPatch.Authoring.Modules.BodyRestoreModule' },
    @{ Name='DeliveryFadeCheckpoint'; Source='delivery-fade-checkpoint'; Revision="r10h-future-return-tree-transaction-core-$CoreTag"; Entry='SuperchargedPatch.Authoring.Modules.DeliveryFadeCheckpointModule' }
)

foreach ($tasModule in $tasModules) {
    & $tasBuilder `
        -CoreBuild $CoreBuild `
        -SourceDirectory (Join-Path $tasRoot ('framework/modules/' + $tasModule.Source)) `
        -Revision $tasModule.Revision `
        -EntryType $tasModule.Entry `
        -Name $tasModule.Name
}

$tasModules | ForEach-Object {
    $tasDirectory = Join-Path $tasRoot ('framework-run/modules/' + $_.Name + '-' + $_.Revision)
    $tasManifest = Get-Content -LiteralPath (Join-Path $tasDirectory 'manifest.json') -Raw | ConvertFrom-Json
    [pscustomobject]@{
        directory = $tasDirectory
        sha256 = $tasManifest.sha256
        coreSha256 = $tasManifest.coreSha256
    }
} | ConvertTo-Json -Depth 3
