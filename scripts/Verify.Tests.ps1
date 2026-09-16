#requires -Version 7.0
# Pure identity fixtures: import only the function AST; never launch, connect,
# quit or terminate a game. Optional PlanOnly checks are run separately by callers.
$ErrorActionPreference = 'Stop'
$testTokens = $null; $testErrors = $null
$testAst = [System.Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'Verify.ps1'), [ref]$testTokens, [ref]$testErrors)
if (@($testErrors).Count -ne 0) { throw ($testErrors | Out-String) }
$testFunction = $testAst.Find({param($node) $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Wait-TaskProcessIdentity'}, $true)
Invoke-Expression $testFunction.Extent.Text
$taskRuntimeExe = [IO.Path]::GetFullPath((Join-Path (Split-Path -Parent $PSScriptRoot) 'runtime\Overcooked2.exe'))
$script:testSamples = @(); $script:testReads = 0; $script:testSleeps = 0
function Get-Process {
    param([int]$Id, [string]$ErrorAction)
    $testIndex = [Math]::Min($script:testReads, $script:testSamples.Count - 1)
    $script:testReads++
    if ($testIndex -ge 0) { return $script:testSamples[$testIndex] }
    return $null
}
function Start-Sleep { param([int]$Milliseconds) if ($Milliseconds -ne 100) { throw 'Identity polling must use 100 ms.' }; $script:testSleeps++ }
function Get-TaskKernelImagePath($Process) { if ($null -ne $Process.KernelPath) { return $Process.KernelPath }; return $Process.Path }
function Set-TestSamples([object[]]$Samples) { $script:testSamples = $Samples; $script:testReads = 0; $script:testSleeps = 0 }
function Test-Process([string]$Path, [long]$Ticks = 123456789) { [pscustomobject]@{Id = 42; Path = $Path; StartTime = [DateTime]::new($Ticks, [DateTimeKind]::Utc)} }
$testChecks = 0
function Assert-Test([bool]$Condition, [string]$Label) { if (-not $Condition) { throw "FAIL: $Label" }; $script:testChecks++ }
function Reject-Test([scriptblock]$Action, [string]$Expected) {
    try { & $Action | Out-Null } catch { if ($_.Exception.Message -like "*$Expected*") { $script:testChecks++; return }; throw }
    throw "FAIL: Expected rejection matching $Expected"
}
Set-TestSamples @((Test-Process ''), (Test-Process ''), (Test-Process $taskRuntimeExe))
$testIdentity = Wait-TaskProcessIdentity -ProcessId 42
Assert-Test ($testIdentity.StartTicks -eq 123456789 -and $testIdentity.Path -ceq $taskRuntimeExe -and $script:testReads -eq 3 -and $script:testSleeps -eq 2) 'Missing early image paths retry before verified ownership.'
Set-TestSamples @((Test-Process $taskRuntimeExe))
$testIdentity = Wait-TaskProcessIdentity -ProcessId 42 -ExpectedStartTicks 123456789
Assert-Test ($testIdentity.Id -eq 42 -and $script:testSleeps -eq 0) 'Explicit exact identity is accepted without delay.'
Set-TestSamples @((Test-Process $taskRuntimeExe))
Reject-Test { Wait-TaskProcessIdentity -ProcessId 42 -ExpectedStartTicks 123456788 } 'creation time'
Set-TestSamples @((Test-Process ''), (Test-Process $taskRuntimeExe 123456790))
Reject-Test { Wait-TaskProcessIdentity -ProcessId 42 } 'creation time'
Set-TestSamples @((Test-Process 'C:\unrelated\Overcooked2.exe'))
Reject-Test { Wait-TaskProcessIdentity -ProcessId 42 -TimeoutMilliseconds 0 } 'image path'
Set-TestSamples @((Test-Process 'runtime\Overcooked2.exe'))
Reject-Test { Wait-TaskProcessIdentity -ProcessId 42 -TimeoutMilliseconds 0 } 'image path'
Set-TestSamples @((Test-Process $taskRuntimeExe))
Reject-Test { Wait-TaskProcessIdentity -ProcessId 42 -NotBeforeTicks 123456790 } 'creation time'
Set-TestSamples @((Test-Process $taskRuntimeExe))
Reject-Test { Wait-TaskProcessIdentity -ProcessId 42 -NotAfterTicks 123456788 } 'creation time'
Set-TestSamples @()
Reject-Test { Wait-TaskProcessIdentity -ProcessId 42 } 'exited'
Set-TestSamples @((Test-Process ''))
Reject-Test { Wait-TaskProcessIdentity -ProcessId 42 -TimeoutMilliseconds 0 } 'unavailable'
Assert-Test ($script:testReads -eq 1 -and $script:testSleeps -eq 1) 'Unavailable identity is bounded and never adopted.'
$testWrongModule = Test-Process 'C:\Windows\SysWOW64\ntdll.dll'
$testWrongModule | Add-Member -NotePropertyName KernelPath -NotePropertyValue $taskRuntimeExe
Set-TestSamples @($testWrongModule)
Assert-Test ((Wait-TaskProcessIdentity -ProcessId 42).Path -ceq $taskRuntimeExe) 'Kernel image identity ignores transient Process.Path module results.'
Set-TestSamples @((Test-Process 'C:\unrelated\Overcooked2.exe'), (Test-Process $taskRuntimeExe))
Assert-Test ((Wait-TaskProcessIdentity -ProcessId 42).Path -ceq $taskRuntimeExe -and $script:testSleeps -eq 1) 'Transient nonempty mismatches retry and ownership begins only after exact kernel path match.'
Set-TestSamples @()
Assert-Test ($null -eq (Wait-TaskProcessIdentity -ProcessId 42 -AllowExited)) 'Cleanup safely tolerates the already-owned process exiting during identity recheck.'
Write-Output "PASS: $testChecks verification identity/parser assertions; no game calls."
