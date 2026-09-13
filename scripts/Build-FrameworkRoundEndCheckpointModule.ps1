param(
 [string]$CoreBuild='artifacts/framework-build',
 [Parameter(Mandatory=$true)][string]$Revision
)
$ErrorActionPreference='Stop'
& (Join-Path $PSScriptRoot 'Build-FrameworkModule.ps1') `
 -CoreBuild $CoreBuild `
 -SourceDirectory (Join-Path (Split-Path -Parent $PSScriptRoot) 'modules/round-end-checkpoint') `
 -Revision $Revision `
 -EntryType 'SuperchargedPatch.Authoring.Modules.RoundEndCheckpointModule' `
 -Name 'RoundEndCheckpoint'
if($LASTEXITCODE -ne 0) { throw 'Round-end checkpoint module build failed.' }
