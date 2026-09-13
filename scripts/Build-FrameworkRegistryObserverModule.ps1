param([string]$CoreBuild='artifacts/framework-plugin-native-x',[Parameter(Mandatory=$true)][string]$Revision)
$ErrorActionPreference='Stop'
& (Join-Path $PSScriptRoot 'Build-FrameworkModule.ps1') -CoreBuild $CoreBuild -SourceDirectory (Join-Path (Split-Path -Parent $PSScriptRoot) 'framework/modules/registry-observer') -Revision $Revision -EntryType 'SuperchargedPatch.Authoring.Modules.RegistryObserverModule' -Name 'RegistryObserver'
if($LASTEXITCODE -ne 0) { throw 'Registry observer module build failed.' }
