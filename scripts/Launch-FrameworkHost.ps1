param([string]$Run='native-test',[int]$GamePort=14455,[int]$ControlPort=17637,[string]$ControllerDll,[ValidateSet('carnival34','story11')][string]$Level='carnival34',[string]$Setup='')
$ErrorActionPreference='Stop'
if ($Run -notmatch '^[a-zA-Z0-9_-]+$') { throw 'Run must be a simple evidence directory name.' }
$tasRoot=Split-Path -Parent $PSScriptRoot
$tasEvidence=Join-Path $tasRoot ('artifacts\framework-migration\'+$Run)
$tasHostDll=if ($ControllerDll) { (Resolve-Path -LiteralPath $ControllerDll).Path } else { Join-Path $tasRoot 'framework\headless\bin\Release\net10.0\Headless.dll' }
$tasSetup=if($Setup){(Resolve-Path -LiteralPath $Setup -ErrorAction Stop).Path}else{''}
if (Test-Path -LiteralPath (Join-Path $tasEvidence 'host-process.json')) { throw 'Choose a new run name; host evidence already exists.' }
New-Item -ItemType Directory -Force -Path $tasEvidence | Out-Null
$tasHostArgs=@($tasHostDll,'serve','--game-port',$GamePort.ToString(),'--control-port',$ControlPort.ToString(),'--level',$Level,'--evidence-root',$tasEvidence,'--trace',(Join-Path $tasEvidence 'exchange.jsonl'),'--seed','0')
if($tasSetup){$tasHostArgs+=@('--setup',$tasSetup)}
$tasProcess=Start-Process -FilePath 'C:\Program Files\dotnet\dotnet.exe' -ArgumentList $tasHostArgs -WorkingDirectory $tasRoot -WindowStyle Hidden -RedirectStandardOutput (Join-Path $tasEvidence 'host-stdout.log') -RedirectStandardError (Join-Path $tasEvidence 'host-stderr.log') -PassThru
$tasReceipt=[ordered]@{pid=$tasProcess.Id;startTicks=$tasProcess.StartTime.ToUniversalTime().Ticks;command=$tasHostArgs;level=$Level;setup=if($tasSetup){$tasSetup}else{$null};controllerHash=(Get-FileHash -LiteralPath $tasHostDll -Algorithm SHA256).Hash}
$tasReceipt | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $tasEvidence 'host-process.json') -Encoding utf8
Copy-Item -LiteralPath (Join-Path $tasRoot 'framework-run\artifacts\process.json') -Destination (Join-Path $tasEvidence 'game-process.json')
$tasReceipt
