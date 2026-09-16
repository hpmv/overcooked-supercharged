#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$Movie,
    [string]$Manifest = '',
    [ValidateSet('5','20')][string]$Mode = '5',
    [int]$Port = 17634,
    [string]$LogPrefix = '',
    [string]$Python = 'python',
    [switch]$Resume,
    [int]$AdoptProcessId = 0,
    [long]$AdoptStartTicks = 0,
    [switch]$AllowIsolatedLab,
    [switch]$PlanOnly
)

$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
. (Join-Path $PSScriptRoot 'IsolatedLab.ps1')
$taskRuntimeExe = [IO.Path]::GetFullPath((Join-Path $taskRoot 'runtime\Overcooked2.exe'))
$taskController = Join-Path $taskRoot 'controller\bin\Release\net10.0\OvercookedTAS.Controller.dll'
$taskMovie = (Resolve-Path -LiteralPath $Movie).Path
if (-not $Manifest) { $Manifest = $taskMovie + '.manifest.json' }
$taskManifest = (Resolve-Path -LiteralPath $Manifest).Path
$taskMovieHash = (Get-FileHash -LiteralPath $taskMovie -Algorithm SHA256).Hash.ToLowerInvariant()
$taskManifestHash = (Get-FileHash -LiteralPath $taskManifest -Algorithm SHA256).Hash.ToLowerInvariant()
$taskManifestData = Get-Content -LiteralPath $taskManifest -Raw | ConvertFrom-Json
if ($taskManifestData.outputFileSha256 -ne $taskMovieHash) { throw 'Movie does not match its extraction manifest.' }
if ($Resume -and -not $LogPrefix) { throw 'Resume requires the explicit existing LogPrefix.' }
if (($AdoptProcessId -ne 0 -or $AdoptStartTicks -ne 0) -and (-not $Resume -or $AdoptProcessId -le 0 -or $AdoptStartTicks -le 0)) {
    throw 'Adoption requires Resume and both explicit positive AdoptProcessId and AdoptStartTicks.'
}
if (-not $LogPrefix) { $LogPrefix = Join-Path $taskRoot ('artifacts\verify-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')) }
$taskPrefix = [IO.Path]::GetFullPath($LogPrefix)
$taskArtifacts = [IO.Path]::GetFullPath((Join-Path $taskRoot 'artifacts')) + [IO.Path]::DirectorySeparatorChar
if (-not $taskPrefix.StartsWith($taskArtifacts, [StringComparison]::OrdinalIgnoreCase)) { throw 'Verification logs must stay inside workspace artifacts.' }
$taskSummaryPath = $taskPrefix + '-summary.json'
if ((Test-Path -LiteralPath $taskSummaryPath) -and -not $Resume) { throw 'Log prefix already has a verification summary; use explicit Resume or choose a new prefix.' }
if ($Resume -and -not (Test-Path -LiteralPath $taskSummaryPath)) { throw 'Resume summary does not exist.' }
if (-not (Test-Path -LiteralPath $taskController)) { throw 'Build the .NET 10 controller before verification.' }
$taskPerProcess = if ($Mode -eq '20') { 4 } else { 1 }
$taskValidationMode = if ($Mode -eq '20') { 'probe' } else { 'highscore' }
$taskRates = @(0, 30, 60, 120, 60)
$taskSummary = [ordered]@{
    format = 'oc2-five-process-verification'; version = 1; mode = $Mode
    movie = $taskMovie; movieFileSha256 = $taskMovieHash; manifest = $taskManifest
    manifestFileSha256 = $taskManifestHash; port = $Port
    allowIsolatedLab = [bool]$AllowIsolatedLab; concurrentLabObservations = @()
    expectedProcessStarts = 5; expectedMovieRuns = 5 * $taskPerProcess
    renderRates = $taskRates; processes = @(); comparisons = @()
    executed = $false; passed = $false; classification = 'not_executed'
    runtimeExe = $taskRuntimeExe
    runtimeExeSha256 = (Get-FileHash -LiteralPath $taskRuntimeExe -Algorithm SHA256).Hash.ToLowerInvariant()
    pluginSha256 = (Get-FileHash -LiteralPath (Join-Path $taskRoot 'runtime\BepInEx\plugins\Oc2Tas.dll') -Algorithm SHA256).Hash.ToLowerInvariant()
    controllerSha256 = (Get-FileHash -LiteralPath $taskController -Algorithm SHA256).Hash.ToLowerInvariant()
    inputPolicy = 'Original movie requests preserved; prelude/render logged separately; no feedback-driven completion tail'
    processPolicy = 'Refuse pre-existing Overcooked2 except an explicitly adopted primary identity, or with AllowIsolatedLab an exact kernel-verified workspace lab/runtime image; the lab is wall-clock load evidence only and is never controlled/stopped'
}
$taskConfigHashes = [ordered]@{}
foreach ($taskConfig in @(Get-ChildItem -LiteralPath (Join-Path $taskRoot 'runtime\BepInEx\config') -File | Sort-Object Name)) {
    $taskConfigHashes[$taskConfig.Name] = (Get-FileHash -LiteralPath $taskConfig.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
}
$taskSummary.configFileSha256 = $taskConfigHashes
$taskCompleted = 0
$taskAllRuns = [Collections.Generic.List[object]]::new()
$script:taskIdentityObservations = [Collections.Generic.List[object]]::new()
$taskSummary.identityObservations = $script:taskIdentityObservations

function Save-TaskSummary {
    $taskSummary | ConvertTo-Json -Depth 80 | Set-Content -LiteralPath $taskSummaryPath -Encoding utf8
}

function Get-TaskKernelImagePath([Diagnostics.Process]$Process) {
    # Process.Path uses MainModule and can briefly name a different loaded module
    # during Mono startup. The kernel image identity does not enumerate modules.
    if ($null -eq ('Oc2TasVerification.KernelProcessImage' -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
namespace Oc2TasVerification {
    public static class KernelProcessImage {
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint access, bool inheritHandle, int processId);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetProcessTimes(IntPtr process, out long creation, out long exit, out long kernel, out long user);
        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool QueryFullProcessImageNameW(IntPtr process, uint flags, StringBuilder filename, ref uint size);
        public static string Read(int processId, long expectedStartTicks) {
            // Process.Handle asks for broader rights. Identity inspection only
            // needs PROCESS_QUERY_LIMITED_INFORMATION (Vista and later).
            IntPtr process = OpenProcess(0x1000, false, processId);
            if (process == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            try {
                long creation, exit, kernel, user;
                if (!GetProcessTimes(process, out creation, out exit, out kernel, out user)) throw new Win32Exception(Marshal.GetLastWin32Error());
                if (DateTime.FromFileTimeUtc(creation).Ticks != expectedStartTicks) throw new InvalidOperationException("Kernel handle creation time differs from the expected process identity.");
                uint size = 32768;
                var path = new StringBuilder((int)size);
                if (!QueryFullProcessImageNameW(process, 0, path, ref size)) throw new Win32Exception(Marshal.GetLastWin32Error());
                return path.ToString();
            } finally { CloseHandle(process); }
        }
    }
}
'@
    }
    return [Oc2TasVerification.KernelProcessImage]::Read($Process.Id, $Process.StartTime.ToUniversalTime().Ticks)
}

function Wait-TaskProcessIdentity {
    param([int]$ProcessId, [long]$ExpectedStartTicks = 0, [long]$NotBeforeTicks = 0,
        [long]$NotAfterTicks = [long]::MaxValue, [int]$TimeoutMilliseconds = 10000, [switch]$AllowExited)
    $taskIdentityDeadline = [DateTime]::UtcNow.AddMilliseconds($TimeoutMilliseconds)
    $taskObservedStartTicks = $ExpectedStartTicks
    $taskPreviousIdentityObservation = ''; $taskQueryError = ''
    do {
        $taskCandidate = Get-Process -Id $ProcessId -ErrorAction SilentlyContinue
        if ($null -eq $taskCandidate) {
            if ($AllowExited) { return $null }
            throw 'Expected game process exited before its identity could be verified.'
        }
        $taskCandidateTicks = 0; $taskCandidatePath = $null
        try { $taskCandidateTicks = $taskCandidate.StartTime.ToUniversalTime().Ticks; $taskCandidatePath = Get-TaskKernelImagePath $taskCandidate; $taskQueryError = '' } catch { $taskQueryError = $_.Exception.Message }
        if ($taskCandidateTicks -gt 0) {
            if ($taskObservedStartTicks -eq 0) { $taskObservedStartTicks = $taskCandidateTicks }
            if ($taskCandidateTicks -ne $taskObservedStartTicks -or $taskCandidateTicks -lt $NotBeforeTicks -or $taskCandidateTicks -gt $NotAfterTicks) {
                throw 'Process creation time does not match the explicit or newly launched identity; refusing ownership.'
            }
            if (-not [string]::IsNullOrWhiteSpace($taskCandidatePath)) {
                if ([IO.Path]::IsPathFullyQualified($taskCandidatePath) -and [string]::Equals([IO.Path]::GetFullPath($taskCandidatePath), $taskRuntimeExe, [StringComparison]::OrdinalIgnoreCase)) {
                    return [pscustomobject]@{ Id = $ProcessId; StartTicks = $taskObservedStartTicks; Path = $taskRuntimeExe; ImagePathSource = 'QueryFullProcessImageNameW' }
                }
            }
        }
        $taskObservation = "$taskCandidateTicks|$taskCandidatePath|$taskQueryError"
        if ($taskObservation -cne $taskPreviousIdentityObservation -and $null -ne $script:taskIdentityObservations) {
            $script:taskIdentityObservations.Add([ordered]@{ observedUtc = [DateTime]::UtcNow.ToString('o'); processId = $ProcessId
                creationTicks = $taskCandidateTicks; queriedImagePath = $taskCandidatePath; queryError = $taskQueryError
                source = 'QueryFullProcessImageNameW'; accepted = $false })
        }
        $taskPreviousIdentityObservation = $taskObservation
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $taskIdentityDeadline)
    throw "Process kernel image path/creation-time query remained unavailable or mismatched for $TimeoutMilliseconds ms; refusing ownership or cleanup. Last image path: '$taskCandidatePath'. $taskQueryError"
}

function Get-OwnedTaskProcess($Identity) {
    if ($null -eq $Identity) { return $null }
    $taskProcess = Get-Process -Id $Identity.Id -ErrorAction SilentlyContinue
    if ($null -eq $taskProcess) { return $null }
    if ($null -eq (Wait-TaskProcessIdentity -ProcessId $Identity.Id -ExpectedStartTicks $Identity.StartTicks -AllowExited)) { return $null }
    return $taskProcess
}

function Assert-OwnedTaskListener($Identity) {
    $taskProcess = Get-OwnedTaskProcess $Identity
    if ($null -eq $taskProcess) { throw 'The owned isolated process exited before its protocol was ready.' }
    $taskListeners = @(Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue)
    if ($taskListeners.Count -eq 0) { return $false }
    if (@($taskListeners | Where-Object { $_.OwningProcess -ne $Identity.Id }).Count -ne 0) {
        throw 'The protocol port belongs to another process; refusing to control it.'
    }
    return $true
}

function Stop-OwnedTaskProcess($Identity, [string]$Prefix) {
    $taskProcess = Get-OwnedTaskProcess $Identity
    if ($null -eq $taskProcess) { return }
    try {
        if (Assert-OwnedTaskListener $Identity) {
            & dotnet $taskController quit --port $Port --timeout 15 --compact 1> ($Prefix + '-quit.json') 2> ($Prefix + '-quit.stderr.txt')
        }
    } catch { $_.Exception.Message | Set-Content -LiteralPath ($Prefix + '-quit-error.txt') -Encoding utf8 }
    $taskProcess = Get-OwnedTaskProcess $Identity
    if ($null -ne $taskProcess -and -not $taskProcess.WaitForExit(15000)) {
        # Resolve identity again immediately before terminating this one owned PID.
        $taskProcess = Get-OwnedTaskProcess $Identity
        if ($null -ne $taskProcess) {
            Stop-Process -Id $taskProcess.Id -Force
            [void]$taskProcess.WaitForExit(10000)
        }
    }
}

if ($Resume) {
    $taskPrior = Get-Content -LiteralPath $taskSummaryPath -Raw | ConvertFrom-Json -AsHashtable
    if ([bool]$taskPrior.allowIsolatedLab -ne [bool]$AllowIsolatedLab) { throw 'Resume concurrent-lab policy differs.' }
    $taskSummary.concurrentLabObservations = @($taskPrior.concurrentLabObservations | Where-Object { $null -ne $_ })
    foreach ($taskPriorObservation in @($taskPrior.identityObservations)) {
        if ($null -ne $taskPriorObservation) { $script:taskIdentityObservations.Add($taskPriorObservation) }
    }
    foreach ($taskField in @('format','version','mode','movie','movieFileSha256','manifest','expectedProcessStarts','expectedMovieRuns',
        'runtimeExe','runtimeExeSha256','pluginSha256','controllerSha256','inputPolicy')) {
        if ([string]$taskPrior[$taskField] -cne [string]$taskSummary[$taskField]) { throw "Resume configuration or binary identity differs: $taskField" }
    }
    if (($taskPrior.renderRates | ConvertTo-Json -Compress) -cne ($taskRates | ConvertTo-Json -Compress)) { throw 'Resume render schedule differs.' }
    if ($taskPrior.Contains('port') -and $taskPrior.port -ne $Port) { throw 'Resume protocol port differs.' }
    if (-not $taskPrior.Contains('port') -and $Port -ne 17634) { throw 'Legacy summary can resume only with its original default port 17634.' }
    if ($taskPrior.Contains('manifestFileSha256') -and $taskPrior.manifestFileSha256 -ne $taskManifestHash) { throw 'Resume manifest bytes differ.' }
    if ($taskPrior.Contains('configFileSha256')) {
        if (($taskPrior.configFileSha256 | ConvertTo-Json -Compress) -cne ($taskConfigHashes | ConvertTo-Json -Compress)) { throw 'Resume BepInEx configuration files differ.' }
    }
    $taskCompleted = @($taskPrior.processes).Count
    if ($taskPrior.executed -ne $true -or $taskPrior.passed -eq $true -or $taskCompleted -lt 1 -or $taskCompleted -ge 5) {
        throw 'Resume requires one through four fully completed process entries from an interrupted series.'
    }
    $taskSeenIdentities = [Collections.Generic.HashSet[string]]::new()
    for ($taskPriorIndex = 0; $taskPriorIndex -lt $taskCompleted; $taskPriorIndex++) {
        $taskEntry = $taskPrior.processes[$taskPriorIndex]
        $taskExpectedOrdinal = $taskPriorIndex + 1
        $taskPriorPrefix = $taskPrefix + ('-process{0:D2}' -f $taskExpectedOrdinal)
        if ($taskEntry.ordinal -ne $taskExpectedOrdinal -or $taskEntry.renderRate -ne $taskRates[$taskPriorIndex]) { throw 'Completed process ordinals or render rates are inconsistent.' }
        if (-not [string]::Equals($taskEntry.identity.Path, $taskRuntimeExe, [StringComparison]::OrdinalIgnoreCase) -or $taskEntry.identity.Id -le 0 -or $taskEntry.identity.StartTicks -le 0) { throw 'Completed process has no valid isolated runtime identity.' }
        if (-not $taskSeenIdentities.Add("$($taskEntry.identity.Id):$($taskEntry.identity.StartTicks)")) { throw 'Completed fresh process identities are duplicated.' }
        $taskDiskResult = Get-Content -LiteralPath ($taskPriorPrefix + '-result.json') -Raw | ConvertFrom-Json -AsHashtable
        if (($taskDiskResult | ConvertTo-Json -Depth 80 -Compress) -cne ($taskEntry.result | ConvertTo-Json -Depth 80 -Compress)) { throw 'Completed result file differs from the stored summary evidence.' }
        $taskAudit = $taskEntry.result.movieAudit
        if ($taskEntry.result.ok -ne $true -or @($taskEntry.result.runs).Count -ne $taskPerProcess -or $taskAudit.movieFileSha256 -ne $taskMovieHash -or $taskAudit.manifestFileSha256 -ne $taskManifestHash -or $taskAudit.feedbackTailAdded -ne $false -or $taskAudit.perFrameMovie -ne $true) {
            throw 'Completed process movie/manifest audit or run count is invalid.'
        }
        if (($taskAudit.startup | ConvertTo-Json -Compress) -cne (($taskManifestData.starts[0].request | ConvertTo-Json -Compress))) {
            # Object property order has no protocol meaning; compare the native startup fields explicitly.
            foreach ($taskStartupField in @('version','command','seed','isolateRecipeRandom')) {
                if ([string]$taskAudit.startup[$taskStartupField] -cne [string]$taskManifestData.starts[0].request.$taskStartupField) { throw 'Completed native startup configuration differs from the extraction manifest.' }
            }
        }
        for ($taskRunIndex = 0; $taskRunIndex -lt $taskPerProcess; $taskRunIndex++) {
            $taskRun = $taskEntry.result.runs[$taskRunIndex]
            $taskExpectedTrace = $taskPriorPrefix + ('-run{0:D2}.jsonl.gz' -f ($taskRunIndex + 1))
            if ($taskRun.run -ne $taskRunIndex + 1 -or $taskRun.mode -ne $taskValidationMode -or $taskRun.renderRate -ne $taskRates[$taskPriorIndex] -or $taskRun.movieFileSha256 -ne $taskMovieHash -or $taskRun.executedRequestsSha256 -ne $taskAudit.dotnetCanonicalRequestsSha256 -or $taskRun.requests -ne $taskAudit.requests -or $taskRun.passedExecutionGate -ne $true -or $taskRun.nativeSetupValidatedBeforeInput -ne $true -or $taskRun.inputsNeutral -ne $true) {
                throw 'Completed run does not satisfy its original movie, configuration and execution gates.'
            }
            if (-not [string]::Equals([IO.Path]::GetFullPath($taskRun.trace), $taskExpectedTrace, [StringComparison]::OrdinalIgnoreCase) -or -not (Test-Path -LiteralPath $taskExpectedTrace) -or (Get-Item -LiteralPath $taskExpectedTrace).Length -eq 0) { throw 'Completed trace is missing or outside its exact expected evidence path.' }
            if ($taskAllRuns.Count -gt 0 -and $taskAllRuns[0].executedRequestsSha256 -ne $taskRun.executedRequestsSha256) { throw 'Prior completed movies have different executed input hashes.' }
            $taskAllRuns.Add($taskRun)
        }
    }
    $taskResumeRecord = [ordered]@{
        requestedUtc = [DateTime]::UtcNow.ToString('o'); previousSummarySha256 = (Get-FileHash -LiteralPath $taskSummaryPath -Algorithm SHA256).Hash.ToLowerInvariant()
        retainedProcessStarts = $taskCompleted; retainedMovieRuns = $taskAllRuns.Count; previousError = $taskPrior.error
        adoptedProcessId = $AdoptProcessId; adoptedStartTicks = $AdoptStartTicks
        configurationEvidence = if ($taskPrior.Contains('configFileSha256')) { 'Recorded manifest/startup, mode, render schedule, port and config-file hashes verified.' } else { 'Legacy manifest/startup, mode and render schedule verified; config-file hashes first pinned at this resume, not claimed for earlier processes.' }
    }
    $taskSummary.processes = @($taskPrior.processes)
    $taskSummary.resumeHistory = @(@($taskPrior.resumeHistory) + @($taskResumeRecord) | Where-Object { $null -ne $_ })
    $taskSummary.classification = 'execution_resuming'
}

for ($taskCheckOrdinal = $taskCompleted + 1; $taskCheckOrdinal -le 5; $taskCheckOrdinal++) {
    $taskCheckPrefix = $taskPrefix + ('-process{0:D2}' -f $taskCheckOrdinal)
    $taskEvidencePaths = @(($taskCheckPrefix + '-result.json'), ($taskCheckPrefix + '-all-calls.jsonl.gz'))
    $taskEvidencePaths += @(1..$taskPerProcess | ForEach-Object { $taskCheckPrefix + ('-run{0:D2}.jsonl.gz' -f $_) })
    foreach ($taskEvidencePath in $taskEvidencePaths) {
        if (Test-Path -LiteralPath $taskEvidencePath) { throw "Refusing to overwrite unaccounted process/run evidence: $taskEvidencePath" }
    }
}

$taskAdopted = $null
$taskPreexisting = @(Get-Process Overcooked2 -ErrorAction SilentlyContinue)
$taskPreexistingPrimary=@()
$taskAllowedLabCount=0
foreach ($taskOtherGame in $taskPreexisting) {
    if ($taskOtherGame.Id -eq $AdoptProcessId) { $taskPreexistingPrimary += $taskOtherGame; continue }
    if (-not $AllowIsolatedLab) { $taskPreexistingPrimary += $taskOtherGame; continue }
    $taskLabIdentity=Get-VerifiedIsolatedLab -ProcessId $taskOtherGame.Id -TaskRoot $taskRoot
    $taskAllowedLabCount++
    $taskSummary.concurrentLabObservations += [ordered]@{ observedUtc=[DateTime]::UtcNow.ToString('o'); stage='preflight'; identity=$taskLabIdentity }
}
if ($taskAllowedLabCount -gt 1) { throw 'AllowIsolatedLab permits at most one concurrent workspace lab.' }
$taskPreexisting=$taskPreexistingPrimary
if ($AdoptProcessId -gt 0) {
    if ($taskPreexisting.Count -ne 1 -or $taskPreexisting[0].Id -ne $AdoptProcessId) { throw 'Adoption requires exactly the explicitly named Overcooked2 PID and no other game process.' }
    $taskAdopted = Wait-TaskProcessIdentity -ProcessId $AdoptProcessId -ExpectedStartTicks $AdoptStartTicks
    if (@($taskSummary.processes | Where-Object { $_.identity.StartTicks -ge $AdoptStartTicks }).Count -gt 0) { throw 'Adopted process is not a fresh start after the retained completed processes.' }
} elseif ($taskPreexisting.Count -gt 0) {
    throw 'An Overcooked2 process already exists. Verification will not control it without explicit Resume, AdoptProcessId and AdoptStartTicks.'
}
$taskPreexistingListeners = @(Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue)
if (@($taskPreexistingListeners | Where-Object { $null -eq $taskAdopted -or $_.OwningProcess -ne $taskAdopted.Id }).Count -gt 0) { throw 'Verification port belongs to a process outside the verified ownership identity.' }

if ($PlanOnly) {
    $taskSummary.plannedCommand = @('dotnet', $taskController, 'verify-run', '--file', $taskMovie, '--manifest', $taskManifest,
        '--runs', "$taskPerProcess", '--validation-mode', $taskValidationMode, '--render-rate', '<per-process-rate>', '--prefix', '<per-process-prefix>')
    $taskSummary.completedProcessStarts = $taskCompleted
    $taskSummary.retainedMovieRuns = $taskAllRuns.Count
    $taskSummary.nextProcessOrdinal = $taskCompleted + 1
    $taskSummary.adoptedIdentity = $taskAdopted
    $taskSummary.planOnlyReadOnly = $true
    $taskSummary | ConvertTo-Json -Depth 20
    return
}

New-Item -ItemType Directory -Force -Path (Split-Path -Parent $taskPrefix) | Out-Null
if ($Resume) {
    $taskResumeBackup = $taskPrefix + ('-resume{0:D2}-previous-summary.json' -f @($taskSummary.resumeHistory).Count)
    if (Test-Path -LiteralPath $taskResumeBackup) { throw 'Resume summary backup already exists; refusing to overwrite evidence.' }
    Copy-Item -LiteralPath $taskSummaryPath -Destination $taskResumeBackup
}

$taskSummary.executed = $true
try {
    for ($taskStart = $taskCompleted + 1; $taskStart -le 5; $taskStart++) {
        $taskProcessPrefix = $taskPrefix + ('-process{0:D2}' -f $taskStart)
        $taskOwned = $null
        $taskClock = [Diagnostics.Stopwatch]::StartNew()
        try {
            foreach ($taskBinary in @(@($taskRuntimeExe, $taskSummary.runtimeExeSha256),
                @((Join-Path $taskRoot 'runtime\BepInEx\plugins\Oc2Tas.dll'), $taskSummary.pluginSha256),
                @($taskController, $taskSummary.controllerSha256))) {
                if ((Get-FileHash -LiteralPath $taskBinary[0] -Algorithm SHA256).Hash.ToLowerInvariant() -ne $taskBinary[1]) {
                    throw 'Game, plugin or controller binary changed during the verification series.'
                }
            }
            foreach ($taskConfigName in $taskConfigHashes.Keys) {
                if ((Get-FileHash -LiteralPath (Join-Path $taskRoot ('runtime\BepInEx\config\' + $taskConfigName)) -Algorithm SHA256).Hash.ToLowerInvariant() -ne $taskConfigHashes[$taskConfigName]) { throw 'BepInEx configuration changed during verification.' }
            }
            if (@(Get-ChildItem -LiteralPath (Join-Path $taskRoot 'runtime\BepInEx\config') -File).Count -ne $taskConfigHashes.Count) { throw 'BepInEx configuration file inventory changed during verification.' }
            if ($null -ne $taskAdopted -and $taskStart -eq $taskCompleted + 1) {
                $taskOwned = Wait-TaskProcessIdentity -ProcessId $taskAdopted.Id -ExpectedStartTicks $taskAdopted.StartTicks
            } else {
                $taskLaunchBefore = [DateTime]::UtcNow.Ticks
                $taskLaunched = & (Join-Path $PSScriptRoot 'Launch.ps1') -Port $Port -AllowIsolatedLab:$AllowIsolatedLab
                foreach ($taskLabIdentity in @($taskLaunched.AllowedConcurrentLab)) {
                    $taskSummary.concurrentLabObservations += [ordered]@{ observedUtc=[DateTime]::UtcNow.ToString('o'); stage='process-start'; processOrdinal=$taskStart; identity=$taskLabIdentity }
                }
                $taskLaunchAfter = [DateTime]::UtcNow.Ticks
                $taskOwned = Wait-TaskProcessIdentity -ProcessId $taskLaunched.Id -NotBeforeTicks $taskLaunchBefore -NotAfterTicks $taskLaunchAfter
            }
            $taskDeadline = [DateTime]::UtcNow.AddSeconds(90)
            while (-not (Assert-OwnedTaskListener $taskOwned)) {
                if ([DateTime]::UtcNow -gt $taskDeadline) { throw 'Owned game protocol startup timed out.' }
                Start-Sleep -Milliseconds 500
            }
            $taskArguments = @($taskController, 'verify-run', '--file', $taskMovie, '--manifest', $taskManifest,
                '--runs', "$taskPerProcess", '--validation-mode', $taskValidationMode, '--render-rate', "$($taskRates[$taskStart - 1])",
                '--prefix', $taskProcessPrefix, '--port', "$Port", '--timeout', "$(1800 * $taskPerProcess)",
                '--out', ($taskProcessPrefix + '-all-calls.jsonl.gz'))
            & dotnet @taskArguments 1> ($taskProcessPrefix + '-result.json') 2> ($taskProcessPrefix + '-stderr.txt')
            if ($LASTEXITCODE -ne 0) { throw "Native execution gate failed in process $taskStart; inspect its stderr and traces." }
            $taskResult = Get-Content -LiteralPath ($taskProcessPrefix + '-result.json') -Raw | ConvertFrom-Json
            if ($taskResult.ok -ne $true -or @($taskResult.runs).Count -ne $taskPerProcess) { throw 'Verifier returned incomplete run evidence.' }
            foreach ($taskRun in $taskResult.runs) {
                if ($taskRun.movieFileSha256 -ne $taskMovieHash -or $taskRun.passedExecutionGate -ne $true) { throw 'Movie hash or execution gate mismatch.' }
                $taskAllRuns.Add($taskRun)
            }
            $taskSummary.processes += [ordered]@{ ordinal = $taskStart; identity = $taskOwned; renderRate = $taskRates[$taskStart - 1]
                wallSeconds = $taskClock.Elapsed.TotalSeconds; result = $taskResult; }
        } finally {
            if ($null -ne $taskOwned) { Stop-OwnedTaskProcess $taskOwned $taskProcessPrefix }
            foreach ($taskLog in @(@('artifacts\player.log', '-player.log'), @('runtime\BepInEx\LogOutput.log', '-bepinex.log'))) {
                $taskLogSource = Join-Path $taskRoot $taskLog[0]
                if (Test-Path -LiteralPath $taskLogSource) { Copy-Item -LiteralPath $taskLogSource -Destination ($taskProcessPrefix + $taskLog[1]) }
            }
            Save-TaskSummary
        }
    }
    if ($taskSummary.processes.Count -ne 5 -or $taskAllRuns.Count -ne 5 * $taskPerProcess) { throw 'Fresh process/run count is incomplete.' }
    $taskBaseline = $taskAllRuns[0]
    $taskEveryEventStable = $true; $taskEveryPhysicsStable = $true; $taskEveryStrictStable = $true
    for ($taskIndex = 0; $taskIndex -lt $taskAllRuns.Count; $taskIndex++) {
        $taskRun = $taskAllRuns[$taskIndex]
        if ($taskRun.executedRequestsSha256 -ne $taskBaseline.executedRequestsSha256) { throw 'Executed input request hashes differ.' }
        $taskAnalysisPath = $taskPrefix + ('-compare{0:D2}.json' -f ($taskIndex + 1))
        & $Python (Join-Path $PSScriptRoot 'analyze_repro.py') $taskBaseline.trace $taskRun.trace --map-initial-rigidbodies --out $taskAnalysisPath 1> ($taskAnalysisPath + '.stdout.txt')
        if ($LASTEXITCODE -ne 0) { throw 'Offline reproducibility analysis failed.' }
        $taskAnalysis = Get-Content -LiteralPath $taskAnalysisPath -Raw | ConvertFrom-Json
        $taskEventsStable = $taskAnalysis.completeSampleAlignment -eq $true -and $taskAnalysis.sameRequestSequence -eq $true -and $taskAnalysis.hasGameplayEvidence -eq $true `
            -and $taskAnalysis.comparisons.'canonical.gameplayEvents'.differentSamples -eq 0 `
            -and $taskAnalysis.comparisons.nativeEventSequence.differentSamples -eq 0
        $taskPhysicsStable = $taskAnalysis.comparisons.'canonical.rawPhysicsHashes'.differentSamples -eq 0
        $taskStrictStable = $taskAnalysis.comparisons.strictState.differentSamples -eq 0
        $taskEveryEventStable = $taskEveryEventStable -and $taskEventsStable
        $taskEveryPhysicsStable = $taskEveryPhysicsStable -and $taskPhysicsStable
        $taskEveryStrictStable = $taskEveryStrictStable -and $taskStrictStable
        $taskSummary.comparisons += [ordered]@{ trace = $taskRun.trace; report = $taskAnalysisPath; eventsStable = $taskEventsStable
            recordedPhysicsStable = $taskPhysicsStable; strictStateStable = $taskStrictStable
            timingFirstDifference = $taskAnalysis.comparisons.timing.firstDivergence
            nativeEventTimingFirstDifference = $taskAnalysis.comparisons.nativeEventTiming.firstDivergence
            nativeFirstDifference = $taskAnalysis.comparisons.nativeState.firstDivergence }
    }
    $taskSummary.freshProcessStarts = 5
    $taskSummary.completedMovieRuns = $taskAllRuns.Count
    $taskSummary.recordedPhysicsStable = $taskEveryPhysicsStable
    $taskSummary.strictRecordedStateStable = $taskEveryStrictStable
    $taskSummary.classification = if (-not $taskEveryEventStable) { 'gameplay_divergence' } elseif ($taskEveryStrictStable) { 'bit_exact_recorded_state' } elseif ($taskEveryPhysicsStable) { 'exact_inputs_stable_native_events_and_recorded_physics' } else { 'exact_inputs_stable_native_events_with_physics_differences' }
    $taskSummary.passed = $taskEveryEventStable
    if (-not $taskEveryEventStable) { throw 'Native event/gameplay sequence differs across runs; see explicit comparison evidence.' }
} catch {
    $taskSummary.passed = $false
    $taskSummary.error = $_.Exception.Message
    if ($taskSummary.classification -eq 'not_executed') { $taskSummary.classification = 'execution_incomplete_or_failed' }
    throw
} finally { Save-TaskSummary }

$taskSummary | ConvertTo-Json -Depth 80
