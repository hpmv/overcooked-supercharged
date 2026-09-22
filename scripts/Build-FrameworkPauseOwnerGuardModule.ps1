param(
 [string]$CoreBuild='',
 [Parameter(Mandatory=$true)][string]$Revision
)
$ErrorActionPreference='Stop'
$scriptParent=Split-Path -Parent $PSScriptRoot
if(Test-Path -LiteralPath (Join-Path $scriptParent '.git')) {$repo=$scriptParent}
elseif(Test-Path -LiteralPath (Join-Path $scriptParent 'framework/.git')) {$repo=Join-Path $scriptParent 'framework'}
else {throw 'Cannot locate the Supercharged repository root from the scripts directory.'}
$workspace=Split-Path -Parent $repo
if(-not $CoreBuild) {$CoreBuild=Join-Path $workspace 'artifacts/framework-build-focus-v27-topology-target-order'}
& (Join-Path $PSScriptRoot 'Build-FrameworkModule.ps1') `
 -CoreBuild $CoreBuild `
 -SourceDirectory (Join-Path $repo 'modules/pause-owner-guard') `
 -Revision $Revision `
 -EntryType 'SuperchargedPatch.Authoring.Modules.PauseOwnerGuardModule' `
 -Name 'PauseOwnerGuard'
