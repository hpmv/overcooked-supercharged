# Pure read-only fixtures; no native process/kernel/port calls and no launches.
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'IsolatedLab.ps1')
$script:checks=0
function Check([bool]$Value,[string]$Label) { if (-not $Value) { throw "FAIL: $Label" }; $script:checks++ }
function Reject([scriptblock]$Action,[string]$Label) { try { & $Action; throw "Accepted: $Label" } catch { if ($_.Exception.Message -like 'Accepted:*') { throw }; $script:checks++ } }
$taskTestRoot=[IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$script:fakeTicks=[DateTime]::UtcNow.Ticks
$script:fakeKernelPath=Join-Path $taskTestRoot 'lab\runtime\Overcooked2.exe'
$script:fakeProcesses=@([pscustomobject]@{Id=12345;StartTime=[DateTime]::new($script:fakeTicks,[DateTimeKind]::Utc);Path='deliberately-wrong-MainModule-path'})
$script:fakeListeners=@()
function Get-Process { param([string]$Name,[int]$Id); if ($Id) { return $script:fakeProcesses | Where-Object Id -eq $Id }; return $script:fakeProcesses }
function Get-FileHash { param($LiteralPath,$Algorithm); [pscustomobject]@{Hash=('a'*64)} }
function Get-IsolatedLabKernelPath { param([int]$ProcessId,[long]$ExpectedStartTicks); if ($ExpectedStartTicks -ne $script:fakeTicks) { throw 'Creation-time mismatch' }; return $script:fakeKernelPath }
function Get-NetTCPConnection { param($LocalPort,$State); return $script:fakeListeners }
function Start-Process { throw 'TEST VIOLATION: attempted native launch' }
$identity=Get-VerifiedIsolatedLab -ProcessId 12345 -TaskRoot $taskTestRoot
Check ($identity.Id -eq 12345 -and $identity.ownership -eq $false -and $identity.StartTicks -eq $script:fakeTicks) 'allowed lab is identified but never owned'
Check ($identity.ImagePathSource -eq 'QueryFullProcessImageNameW') 'kernel path wins over misleading MainModule path'
foreach ($bad in @((Join-Path $taskTestRoot 'runtime\Overcooked2.exe'),(Join-Path $taskTestRoot 'lab-other\runtime\Overcooked2.exe'),'C:\Program Files\Steam\Overcooked2.exe','lab\runtime\Overcooked2.exe')) {
    $script:fakeKernelPath=$bad
    Reject { Get-VerifiedIsolatedLab -ProcessId 12345 -TaskRoot $taskTestRoot } "reject unrelated image $bad"
}
$script:fakeKernelPath=Join-Path $taskTestRoot 'lab\runtime\Overcooked2.exe'
# Load the actual launcher body, supplying the already-mocked identity dependency.
$taskLaunchText=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'Launch.ps1'))
$taskLaunchText=$taskLaunchText.Replace(". (Join-Path `$PSScriptRoot 'IsolatedLab.ps1')",'')
$taskLaunchText=$taskLaunchText.Replace('$taskRoot=Split-Path -Parent $PSScriptRoot',('$taskRoot='''+$taskTestRoot.Replace("'","''")+''''))
$taskLauncher=[scriptblock]::Create($taskLaunchText)
Reject { & $taskLauncher -PlanOnly } 'default strict mode refuses preexisting lab'
$plan=& $taskLauncher -PlanOnly -AllowIsolatedLab
Check ($plan.PlanOnly -and @($plan.AllowedConcurrentLab).Count -eq 1) 'explicit lab option gives read-only launch plan'
$script:fakeListeners=@([pscustomobject]@{OwningProcess=12345})
Reject { & $taskLauncher -PlanOnly -AllowIsolatedLab } 'primary port owned by lab is rejected'
$script:fakeListeners=@()
$script:fakeProcesses+= [pscustomobject]@{Id=22222;StartTime=[DateTime]::UtcNow}
Reject { & $taskLauncher -PlanOnly -AllowIsolatedLab } 'more than one preexisting game rejected'
$script:fakeProcesses=@()
$plan=& $taskLauncher -PlanOnly
Check ($plan.PlanOnly -and @($plan.AllowedConcurrentLab).Count -eq 0) 'strict empty-runtime plan does not launch'
Write-Output "PASS: $script:checks isolated-lab identity/launcher assertions. No game calls performed."
