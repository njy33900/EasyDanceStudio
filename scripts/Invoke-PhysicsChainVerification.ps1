param(
    [switch]$SkipBuild,
    [switch]$IncludeRealModels,
    [switch]$RetainSolverState,
    [switch]$RetainAllSolverState,
    [switch]$ContextCompare,
    [switch]$Lifecycle,
    [ValidateSet('Mono','IL2CPP')][string]$Backend = 'Mono',
    [string]$Unity = 'C:\Program Files\Unity\Hub\Editor\6000.3.28f1\Editor\Unity.exe'
)
$ErrorActionPreference = 'Stop'
if ($RetainAllSolverState) { $RetainSolverState = $true }
$root = Split-Path $PSScriptRoot -Parent
$artifacts = Join-Path $root 'artifacts\physics-chain'
$exe = Join-Path $root 'builds\POC-06E-Mono\MmdStudioPoC.exe'
if ($ContextCompare) {
    if ($RetainSolverState) { throw 'ContextCompare must use the unmodified runtime baseline' }
    $artifacts = Join-Path $root 'artifacts\physics-context'
    $exe = Join-Path $root 'builds\POC-06F-Mono\MmdStudioPoC.exe'
}
if ($Lifecycle) {
    if ($ContextCompare -or $RetainSolverState) { throw 'Lifecycle must run separately from diagnostic experiments' }
    $artifacts = Join-Path $root "artifacts\physics-lifecycle\$Backend"
    $exe = Join-Path $root "builds\POC-06G-$Backend\MmdStudioPoC.exe"
}
elseif ($Backend -ne 'Mono') { throw 'Only Lifecycle currently supports IL2CPP in this script' }
New-Item -ItemType Directory -Path $artifacts -Force | Out-Null

function Invoke-HiddenProcess([string]$File, [string[]]$Arguments, [int]$TimeoutSeconds) {
    $info = [System.Diagnostics.ProcessStartInfo]::new($File)
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.WindowStyle = [System.Diagnostics.ProcessWindowStyle]::Hidden
    $info.WorkingDirectory = $root
    foreach ($argument in $Arguments) { $info.ArgumentList.Add($argument) }
    $process = [System.Diagnostics.Process]::Start($info)
    try {
        if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
            $process.Kill($true)
            throw "Timed out: $File"
        }
        return $process.ExitCode
    }
    finally { $process.Dispose() }
}

& python (Join-Path $PSScriptRoot 'New-PhysicsChainFixtures.py') --output (Join-Path $artifacts 'fixtures')
if ($LASTEXITCODE -ne 0) { throw 'Fixture generation failed' }
if (-not $SkipBuild) {
    $code = Invoke-HiddenProcess $Unity @('-batchmode', '-nographics', '-quit',
        '-projectPath', (Join-Path $root 'MMDStudio'), '-executeMethod', 'MmdStudio.Poc.PocBuild.BuildWindows',
        '-pocOutput', $exe, '-pocBackend', $Backend.ToLowerInvariant(), '-logFile', (Join-Path $artifacts 'poc-build.log')) 1800
    if ($code -ne 0) { throw "Build failed: $code" }
}
if (-not (Test-Path -LiteralPath $exe)) { throw "Player missing: $exe" }
$models = @('free','joint','append','ik') | ForEach-Object {
    @{ Name = $_; Path = Join-Path $artifacts "fixtures\$_.pmx" }
}
if ($IncludeRealModels) {
    $models += @{ Name = 'gene'; Path = Join-Path $root 'test\models\Gene\Gene_light.pmx' }
    $models += @{ Name = 'sandrone'; Path = Join-Path $root 'test\Sandrone_Runtime\桑多涅_目齿轮可动.pmx' }
}
$results = @()
foreach ($model in $models) {
    $kind = if ($RetainAllSolverState) { 'retain-all-state' } elseif ($RetainSolverState) { 'retain-state' } else { 'traced' }
    $caseDir = Join-Path $artifacts "$kind\$($model.Name)"
    New-Item -ItemType Directory -Path $caseDir -Force | Out-Null
    $path = Join-Path $caseDir 'report.json'
    $started = [DateTime]::UtcNow
    $arguments = if ($Lifecycle) {
        @('-batchmode', '-pocPmx', $model.Path, '-pocPhysicsLifecycleReport', $path, '-logFile', (Join-Path $caseDir 'player.log'))
    } else {
        @('-batchmode', '-pocPmx', $model.Path, '-pocPhysicsReferenceReport', $path, '-pocPhysicsPhaseTrace', '-logFile', (Join-Path $caseDir 'player.log'))
    }
    if ($RetainSolverState) { $arguments += '-pocPhysicsRetainSolverState' }
    if ($RetainAllSolverState) { $arguments += '-pocPhysicsRetainAllSolverState' }
    if ($ContextCompare) { $arguments += '-pocPhysicsContextCompare' }
    $code = Invoke-HiddenProcess $exe $arguments 300
    $report = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
    if ($Lifecycle) {
        if ((Get-Item -LiteralPath $path).LastWriteTimeUtc -lt $started -or $code -ne 0 -or
            -not $report.success -or $report.backend -ne $Backend -or $report.checks.Count -lt 20) {
            throw "Physics lifecycle failed for $($model.Name): $($report.error)"
        }
        $results += [pscustomobject]@{model=$model.Name; backend=$report.backend; passed=$report.success; checks=$report.checks.Count;
            comparisons=$report.comparisons; maximumPositionError=$report.maximumPositionError; maximumRotationError=$report.maximumRotationError; report=$path}
        continue
    }
    if ((Get-Item -LiteralPath $path).LastWriteTimeUtc -lt $started -or $code -ne 0 -or
        -not $report.success -or $report.backend -ne 'Mono' -or -not $report.phaseTrace.equivalent) {
        throw "Trace execution/equivalence failed for $($model.Name): $($report.error)"
    }
    foreach ($count in @('solveCalls','prePhysicsCalls','physicsWrapperCalls','postPhysicsCalls')) {
        if ($report.phaseTrace.$count -ne 91) { throw "Unexpected $count for $($model.Name)" }
    }
    if ($ContextCompare -and -not $report.contextComparison.converterReplayParity) {
        throw "Standalone converter trace failed official output comparison: $($model.Name)"
    }
    if ($ContextCompare -and ($report.contextComparison.configurationDifferences -ne 0 -or
        $report.contextComparison.physicalSelectionDifferences -ne 0 -or
        -not $report.contextComparison.sameInputsAndHistory.parityPassed)) {
        throw "Matched-input/history control failed for $($model.Name)"
    }
    $row = [pscustomobject]@{ model=$model.Name; executionPassed=$report.success;
        traceEquivalent=$report.phaseTrace.equivalent; referenceParity=$report.parityPassed;
        firstDivergentFrame=$report.firstDivergentFrame;
        maximumPositionError=$report.maximumLocalPositionError;
        maximumRotationError=$report.maximumLocalRotationError; report=$path }
    if ($ContextCompare) {
        $row | Add-Member -NotePropertyName configurationDifferences -NotePropertyValue $report.contextComparison.configurationDifferences
        $row | Add-Member -NotePropertyName standaloneParity -NotePropertyValue $report.contextComparison.converterReplayParity
        $row | Add-Member -NotePropertyName sameInputs -NotePropertyValue $report.contextComparison.sameInputs
        $row | Add-Member -NotePropertyName sameInputsAndHistory -NotePropertyValue $report.contextComparison.sameInputsAndHistory
    }
    $results += $row
}
$summary = if ($RetainAllSolverState) { 'retain-all-state-verification.json' } elseif ($RetainSolverState) { 'retain-state-verification.json' } else { 'verification.json' }
$results | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $artifacts $summary) -Encoding utf8
$results | Format-Table -AutoSize
# Reference parity is diagnostic data, not the execution exit status.
