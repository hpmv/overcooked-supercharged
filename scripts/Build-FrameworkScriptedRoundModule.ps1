param([string]$CoreBuild='artifacts/framework-plugin-native-x',[Parameter(Mandatory=$true)][string]$Revision)
$ErrorActionPreference='Stop'
& (Join-Path $PSScriptRoot 'Build-FrameworkModule.ps1') -CoreBuild $CoreBuild -SourceDirectory (Join-Path (Split-Path -Parent $PSScriptRoot) 'framework/modules/scripted-round') -Revision $Revision -EntryType 'SuperchargedPatch.Authoring.Modules.ScriptedRoundModule' -Name 'ScriptedRound'
if($LASTEXITCODE -ne 0) { throw 'Scripted round module build failed.' }
