param(
 [Parameter(Mandatory=$true)][string]$CoreBuild,
 [Parameter(Mandatory=$true)][string]$Revision,
 [string]$SourceDirectory=''
)
$ErrorActionPreference='Stop'
$tasRoot=Split-Path -Parent $PSScriptRoot
if(-not $SourceDirectory){$SourceDirectory=Join-Path $tasRoot 'framework/modules/body-restore'}
# This compiles an external DLL only. Activation remains an explicit fenced,
# paused hot-call; no plugin deployment, process restart or game connection.
& (Join-Path $PSScriptRoot 'Build-FrameworkModule.ps1') -CoreBuild $CoreBuild -Revision $Revision -SourceDirectory $SourceDirectory -Name 'BodyRestore' -EntryType 'SuperchargedPatch.Authoring.Modules.BodyRestoreModule'
if($LASTEXITCODE -ne 0){throw 'Body restore module build failed.'}
