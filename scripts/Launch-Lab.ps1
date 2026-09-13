param([int]$Port=17635)
$ErrorActionPreference='Stop'
$taskRoot=Split-Path -Parent $PSScriptRoot
$taskLab=Join-Path $taskRoot 'lab'
$taskSource=Join-Path $taskRoot 'runtime'
$taskDestination=Join-Path $taskLab 'runtime'
if (@(Get-Process Overcooked2 -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq (Join-Path $taskDestination 'Overcooked2.exe') }).Count) { throw 'The isolated lab is already running.' }
if (@(Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue).Count) { throw 'Lab protocol port is occupied.' }
New-Item -ItemType Directory -Force -Path $taskLab,(Join-Path $taskLab 'artifacts'),(Join-Path $taskLab 'profile'),$taskDestination | Out-Null
# Copy immutable runtime files so a lab session owns every writable game file.
# The primary runtime and its running verification series are never modified.
& robocopy $taskSource $taskDestination /E /XD (Join-Path $taskSource 'BepInEx\cache') /XF LogOutput.log /R:1 /W:1 /NFL /NDL /NJH /NJS /NP
if ($LASTEXITCODE -ge 8) { throw "Lab copy failed: $LASTEXITCODE" }
$env:SteamAppId='728880';$env:OC2TAS_ROOT=$taskLab;$env:OC2TAS_PORT=$Port.ToString()
Start-Process -FilePath (Join-Path $taskDestination 'Overcooked2.exe') -WorkingDirectory $taskDestination -ArgumentList @('-screen-fullscreen','0','-screen-width','1280','-screen-height','720','-logFile',(Join-Path $taskLab 'artifacts\player.log')) -WindowStyle Normal -PassThru | Select-Object Id,Path
