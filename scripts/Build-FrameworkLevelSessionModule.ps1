param([string]$CoreBuild='artifacts/framework-plugin-native-x',[Parameter(Mandatory=$true)][string]$Revision)
$ErrorActionPreference='Stop'
& (Join-Path $PSScriptRoot 'Build-FrameworkModule.ps1') -CoreBuild $CoreBuild -SourceDirectory (Join-Path (Split-Path -Parent $PSScriptRoot) 'framework/modules/level-session') -Revision $Revision -EntryType 'SuperchargedPatch.Authoring.Modules.LevelSessionModule' -Name 'LevelSession'
if($LASTEXITCODE -ne 0) { throw 'Level session module build failed.' }
