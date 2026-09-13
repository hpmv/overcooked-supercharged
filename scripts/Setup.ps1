param([string]$GamePath = 'K:\trash\Steam\steamapps\common\Overcooked! 2')
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskRuntime = Join-Path $taskRoot 'runtime'
$GamePath = (Resolve-Path -LiteralPath $GamePath).Path
if (-not (Test-Path -LiteralPath (Join-Path $GamePath 'Overcooked2.exe'))) { throw 'Original game executable missing' }
$taskSteamManifest = Join-Path (Split-Path -Parent (Split-Path -Parent $GamePath)) 'appmanifest_728880.acf'
if (-not (Test-Path -LiteralPath $taskSteamManifest)) { throw 'Steam installation manifest is missing; cannot verify the requested build.' }
$taskSteamText = Get-Content -LiteralPath $taskSteamManifest -Raw
$taskBuildMatch = [regex]::Match($taskSteamText, '(?m)^\s*"buildid"\s+"(\d+)"')
$taskAppMatch = [regex]::Match($taskSteamText, '(?m)^\s*"appid"\s+"(\d+)"')
if (-not $taskBuildMatch.Success -or $taskBuildMatch.Groups[1].Value -ne '20236421' -or $taskAppMatch.Groups[1].Value -ne '728880') {
 throw 'This TAS requires the observed Steam app 728880 build 20236421; the source manifest differs.'
}
if (Get-Process Overcooked2 -ErrorAction SilentlyContinue) { throw 'Close game processes before replacing files in the isolated runtime.' }
New-Item -ItemType Directory -Force -Path $taskRuntime,(Join-Path $taskRoot 'artifacts'),(Join-Path $taskRoot 'profile') | Out-Null
& robocopy $GamePath $taskRuntime /E /XD BepInEx /XF doorstop_config.ini /R:1 /W:1 /NFL /NDL /NJH /NJS /NP
if ($LASTEXITCODE -ge 8) { throw "Runtime copy failed: $LASTEXITCODE" }
& robocopy (Join-Path $GamePath 'BepInEx\core') (Join-Path $taskRuntime 'BepInEx\core') /E /R:1 /W:1 /NFL /NDL /NJH /NJS /NP
if ($LASTEXITCODE -ge 8) { throw "BepInEx copy failed: $LASTEXITCODE" }
Copy-Item -LiteralPath (Join-Path $GamePath 'doorstop_config.ini') -Destination $taskRuntime
New-Item -ItemType Directory -Force -Path (Join-Path $taskRuntime 'BepInEx\plugins'),(Join-Path $taskRuntime 'BepInEx\config') | Out-Null
$taskManifest = [ordered]@{source=$GamePath; steamBuild=$taskBuildMatch.Groups[1].Value;
 steamManifestPath=$taskSteamManifest; steamManifestSha256=(Get-FileHash -LiteralPath $taskSteamManifest -Algorithm SHA256).Hash;
 createdUtc=[DateTime]::UtcNow.ToString('o'); files=@()}
foreach ($taskFile in @('Overcooked2.exe','UnityPlayer.dll','Overcooked2_Data\Managed\Assembly-CSharp.dll','Overcooked2_Data\StreamingAssets\Windows\s_day_3_4')) {
 $taskHash=Get-FileHash -LiteralPath (Join-Path $GamePath $taskFile) -Algorithm SHA256
 $taskCopyHash=Get-FileHash -LiteralPath (Join-Path $taskRuntime $taskFile) -Algorithm SHA256
 if ($taskHash.Hash -ne $taskCopyHash.Hash) { throw "Copy mismatch: $taskFile" }
 $taskManifest.files += [ordered]@{path=$taskFile;sha256=$taskHash.Hash}
}
$taskManifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $taskRoot 'artifacts\installation.json') -Encoding utf8
Write-Output 'Isolated runtime ready.'
