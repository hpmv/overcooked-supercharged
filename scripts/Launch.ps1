param([int]$Port=17634,[switch]$AllowIsolatedLab,[switch]$PlanOnly)
$ErrorActionPreference='Stop'
$taskRoot=Split-Path -Parent $PSScriptRoot
$taskGame=Join-Path $taskRoot 'runtime'
. (Join-Path $PSScriptRoot 'IsolatedLab.ps1')
$taskExisting=@(Get-Process Overcooked2 -ErrorAction SilentlyContinue)
$taskAllowedLabs=@()
if ($taskExisting.Count -gt 0) {
    if (-not $AllowIsolatedLab) { throw 'An Overcooked2 process is already running; close it before launching an isolated session.' }
    if ($taskExisting.Count -ne 1) { throw 'AllowIsolatedLab permits at most one verified concurrent workspace lab.' }
    $taskAllowedLabs=@(Get-VerifiedIsolatedLab -ProcessId $taskExisting[0].Id -TaskRoot $taskRoot)
}
if (@(Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue).Count -gt 0) { throw 'Requested primary control port already has a listener; refusing to launch.' }
if ($PlanOnly) {
    [pscustomobject]@{ PlanOnly=$true; Runtime=(Join-Path $taskGame 'Overcooked2.exe'); Port=$Port; AllowedConcurrentLab=$taskAllowedLabs }
    return
}
$env:SteamAppId='728880'
$env:OC2TAS_ROOT=$taskRoot
$env:OC2TAS_PORT=$Port.ToString()
$taskLaunched=Start-Process -FilePath (Join-Path $taskGame 'Overcooked2.exe') -WorkingDirectory $taskGame -ArgumentList @('-screen-fullscreen','0','-screen-width','1280','-screen-height','720','-logFile',(Join-Path $taskRoot 'artifacts\player.log')) -WindowStyle Normal -PassThru
[pscustomobject]@{ Id=$taskLaunched.Id; Path=$taskLaunched.Path; AllowedConcurrentLab=$taskAllowedLabs }
