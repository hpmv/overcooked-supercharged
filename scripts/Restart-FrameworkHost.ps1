param(
 [Parameter(Mandatory=$true)][string]$PreviousRun,
 [Parameter(Mandatory=$true)][string]$Run,
 [Parameter(Mandatory=$true)][string]$ControllerDll,
 [switch]$ProbePausedGap,
 [ValidateSet('carnival34','story11')][string]$Level='carnival34',
 [switch]$SkipLevelRestart,
 [string]$Setup=''
)
$ErrorActionPreference='Stop'
if($Level -eq 'story11' -and -not $SkipLevelRestart){throw 'Story11 requires -SkipLevelRestart; use the explicit level-session module after the new host connects. The legacy bridge restart selects Carnival.'}
foreach($tasRunName in @($PreviousRun,$Run)) {if($tasRunName -notmatch '^[a-zA-Z0-9_-]+$'){throw 'Run names must be simple evidence directory names.'}}
$tasRoot=Split-Path -Parent $PSScriptRoot
$tasReplacementDll=(Resolve-Path -LiteralPath $ControllerDll -ErrorAction Stop).Path
if(-not (Test-Path -LiteralPath $tasReplacementDll -PathType Leaf)){throw 'Replacement controller DLL must exist before the running host is stopped.'}
$tasSetup=if($Setup){(Resolve-Path -LiteralPath $Setup -ErrorAction Stop).Path}else{''}
$tasPrevious=Join-Path $tasRoot "artifacts/framework-migration/$PreviousRun"
$tasOldHost=Get-Content -LiteralPath (Join-Path $tasPrevious 'host-process.json') -Raw | ConvertFrom-Json
$tasOldGame=Get-Content -LiteralPath (Join-Path $tasPrevious 'game-process.json') -Raw | ConvertFrom-Json
$tasGameProcess=Get-Process -Id $tasOldGame.pid -ErrorAction Stop
if($tasGameProcess.StartTime.ToUniversalTime().Ticks -ne $tasOldGame.startTicks){throw 'Original game process identity changed.'}
$tasHostProcess=Get-Process -Id $tasOldHost.pid -ErrorAction Stop
$tasHostCim=$null
try{$tasHostCim=Get-CimInstance Win32_Process -Filter "ProcessId=$($tasOldHost.pid)" -ErrorAction Stop}catch [Microsoft.Management.Infrastructure.CimException]{}
if($tasHostProcess.StartTime.ToUniversalTime().Ticks -ne $tasOldHost.startTicks){throw 'Host process identity does not match receipt.'}
if($tasHostCim){
 if($tasHostCim.CommandLine -notmatch [regex]::Escape($tasOldHost.command[0])){throw 'Host process command does not match receipt.'}
}else{
 # Some restricted Windows sessions deny Win32_Process.CommandLine. Retain an
 # exact fallback fence: the receipt-selected PID/start time must be dotnet,
 # and the recorded controller path must still contain its recorded bytes.
 $tasDotnet=(Get-Command dotnet.exe -ErrorAction Stop).Source
 $tasRecordedController=(Resolve-Path -LiteralPath $tasOldHost.command[0] -ErrorAction Stop).Path
 $tasRecordedHash=(Get-FileHash -LiteralPath $tasRecordedController -Algorithm SHA256).Hash
 if($tasHostProcess.Path -ne $tasDotnet -or $tasRecordedHash -ne $tasOldHost.controllerHash){throw 'Restricted host-process identity fallback does not match receipt.'}
}
$tasNext=Join-Path $tasRoot "artifacts/framework-migration/$Run"
if(Test-Path -LiteralPath $tasNext){throw 'New run evidence directory already exists.'}
$tasPause=Join-Path $tasPrevious 'pause-before-host-replacement.json'
if(Test-Path -LiteralPath $tasPause){throw 'Replacement already attempted from this run; use its preserved receipt.'}
$tasPauseScript=Join-Path $tasPrevious 'pause-before-host-replacement-script.json'
'[{"target":"bridge","request":{"command":"pause"}}]' | Set-Content -LiteralPath $tasPauseScript -Encoding utf8
& python (Join-Path $PSScriptRoot 'framework_rpc.py') --script $tasPauseScript --out $tasPause | Out-Null
if($LASTEXITCODE -ne 0){throw 'Native pause failed; host remains running.'}
Stop-Process -Id $tasHostProcess.Id
$tasHostProcess.WaitForExit(10000) | Out-Null
if($ProbePausedGap){
 & python (Join-Path $PSScriptRoot 'framework_controller_gap_probe.py') --out (Join-Path $tasPrevious 'controller-disconnected-responsiveness.json')
 if($LASTEXITCODE -ne 0){throw 'Disconnected pause check failed; game remains open and paused with evidence saved.'}
}
& (Join-Path $PSScriptRoot 'Launch-FrameworkHost.ps1') -Run $Run -ControllerDll $tasReplacementDll -Level $Level -Setup $tasSetup
$tasGameAfter=Get-Process -Id $tasOldGame.pid -ErrorAction Stop
if($tasGameAfter.StartTime.ToUniversalTime().Ticks -ne $tasOldGame.startTicks){throw 'Game identity changed during host replacement.'}
[ordered]@{previousRun=$PreviousRun;run=$Run;level=$Level;controllerSetup=if($tasSetup){$tasSetup}else{$null};gamePid=$tasOldGame.pid;gameStartTicks=$tasOldGame.startTicks;gameProcessPreserved=$true;nativeLevelRestartStillRequired=$true;levelRestartDelegatedToOperator=[bool]$SkipLevelRestart} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $tasNext 'host-replacement.json') -Encoding utf8
if($SkipLevelRestart){return}
$tasRestartScript=Join-Path $tasNext 'restart-level-script.json'
'[{"target":"controller","request":{"command":"status"},"until":{"path":"connected","equals":true},"timeoutSeconds":30},{"target":"bridge","request":{"command":"restart","seed":0}},{"target":"bridge","request":{"command":"status"},"until":{"path":"bridge.loadComplete","equals":true},"timeoutSeconds":150},{"target":"controller","request":{"command":"inspect","full":true},"until":{"all":[{"path":"state","equals":"Paused"},{"path":"requestPending","equals":false},{"path":"freshLevelLoadObserved","equals":true}]},"timeoutSeconds":20}]' | Set-Content -LiteralPath $tasRestartScript -Encoding utf8
& python (Join-Path $PSScriptRoot 'framework_rpc.py') --script $tasRestartScript --out (Join-Path $tasNext 'restart-level.json') > (Join-Path $tasNext 'restart-level-stdout.log')
if($LASTEXITCODE -ne 0){throw 'Replacement controller started, but level restart did not complete; receipts retained.'}
$tasReady=Get-Content -LiteralPath (Join-Path $tasNext 'restart-level.json') -Raw | ConvertFrom-Json
$tasReadyHost=$tasReady.steps[-1].response
$tasReadyGame=Get-Process -Id $tasOldGame.pid -ErrorAction Stop
if($tasReadyGame.StartTime.ToUniversalTime().Ticks -ne $tasOldGame.startTicks){throw 'Game identity changed during the native level restart.'}
[ordered]@{previousRun=$PreviousRun;run=$Run;gamePid=$tasOldGame.pid;gameStartTicks=$tasOldGame.startTicks;gameProcessPreserved=$true;nativeLevelRestartComplete=$true;controllerFrame=$tasReadyHost.frame;controllerState=$tasReadyHost.state;freshLevelLoadObserved=$tasReadyHost.freshLevelLoadObserved;elapsedRestartSeconds=$tasReady.elapsedSeconds} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $tasNext 'host-replacement-complete.json') -Encoding utf8
