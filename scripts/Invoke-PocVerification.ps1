param(
    [ValidateSet('Debug','Release')][string]$Configuration = 'Debug',
    [string]$Unity = 'C:\Program Files\Unity\Hub\Editor\6000.3.28f1\Editor\Unity.exe',
    [switch]$SkipBuild,
    [switch]$IncludePmx,
    [switch]$IncludeVmd,
    [switch]$IncludeComparison,
    [switch]$OnlyComparison,
    [switch]$IncludeRecovery,
    [switch]$IncludeCamera,
    [switch]$OnlyCamera,
    [switch]$IncludeDeformation,
    [switch]$OnlyDeformation,
    [switch]$IncludePhysics,
    [switch]$OnlyPhysics,
    [switch]$PhysicsEndurance,
    [switch]$PhysicsReference,
    [ValidateSet('Mono', 'IL2CPP')]
    [string]$Backend = 'Mono',
    [string]$Vmd
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$outputRoot = Join-Path (Join-Path $root 'out') $Configuration.ToLowerInvariant()
$artifacts = Join-Path $root 'artifacts'
$exe = Join-Path $outputRoot 'POC-06C-Mono\MmdStudioPoC.exe'
if ($Backend -eq 'IL2CPP') {
    $artifacts = Join-Path $root 'artifacts\il2cpp'
    $exe = Join-Path $outputRoot 'POC-06C-IL2CPP\MmdStudioPoC.exe'
}
if ($PhysicsEndurance -and $PhysicsReference) { throw 'Choose PhysicsEndurance or PhysicsReference separately.' }
if ($PhysicsEndurance -or $PhysicsReference) {
    $kind = if ($PhysicsReference) { 'physics-reference' } else { 'physics-endurance' }
    $artifacts = Join-Path $root "artifacts\$kind\$Backend"
    $exe = Join-Path $outputRoot "POC-06D-$Backend\MmdStudioPoC.exe"
    $OnlyPhysics = $true
}
if ($OnlyComparison) { $IncludeComparison = $true }
if ($OnlyCamera) { $IncludeCamera = $true }
if ($OnlyDeformation) { $IncludeDeformation = $true }
if ($OnlyPhysics) { $IncludePhysics = $true }
if ($IncludeComparison) { $IncludeVmd = $true }
if ($IncludeVmd) { $IncludePmx = $true }
if ($Configuration -eq 'Release') { $artifacts = Join-Path $artifacts 'release' }
New-Item -ItemType Directory -Path $artifacts -Force | Out-Null

function Invoke-ProbeProcess([string]$File, [string[]]$Arguments, [int]$TimeoutSeconds) {
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
            throw "Process timed out: $File"
        }
        return $process.ExitCode
    }
    finally { $process.Dispose() }
}

if (-not $SkipBuild) {
    $code = Invoke-ProbeProcess $Unity @(
        '-batchmode', '-nographics', '-quit', '-projectPath', (Join-Path $root 'MMDStudio'),
        '-pocConfiguration', $Configuration.ToLowerInvariant(), '-executeMethod', 'MmdStudio.Poc.PocBuild.BuildWindows', '-pocOutput', $exe,
        '-pocBackend', $Backend.ToLowerInvariant(),
        '-logFile', (Join-Path $artifacts 'poc-build.log')
    ) 1800
    if ($code -ne 0) { throw "Unity build failed with exit $code; see $(Join-Path $artifacts 'poc-build.log')" }
}
if (-not (Test-Path -LiteralPath $exe)) { throw "Player not found: $exe" }

$results = @()
$graphicsCases = if ($OnlyComparison -or $OnlyCamera -or $OnlyDeformation -or $OnlyPhysics) { @() } else { @($false, $true) }
foreach ($headless in $graphicsCases) {
    $name = if ($headless) { 'poc-player-no-graphics' } else { 'poc-player' }
    $reportPath = Join-Path $artifacts "$name.json"
    $arguments = @('-batchmode', '-screen-width', '640', '-screen-height', '360',
        '-pocSmokeReport', $reportPath, '-logFile', (Join-Path $artifacts "$name.log"))
    if ($headless) { $arguments += '-nographics' }
    $started = [DateTime]::UtcNow
    $code = Invoke-ProbeProcess $exe $arguments 60
    $file = Get-Item -LiteralPath $reportPath
    if ($file.LastWriteTimeUtc -lt $started) { throw "Stale Player report: $reportPath" }
    $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
    if ($report.scriptingBackend -ne $Backend) { throw "Wrong Player backend: $($report.scriptingBackend), expected $Backend" }
    if ($headless) {
        if ($code -ne 1 -or $report.success -or $report.graphicsDeviceType -ne 'Null') {
            throw 'No-graphics negative control did not fail as expected.'
        }
    }
    elseif ($code -ne 0 -or -not $report.success -or -not (Test-Path -LiteralPath $report.screenshot)) {
        throw "GPU Player verification failed: $($report.error)"
    }
    $results += [pscustomobject]@{ test = $name; exitCode = $code; expectedFailure = $headless; report = $reportPath }
}
if ($IncludePmx) {
    $pmx = Join-Path $root 'test\models\Gene\Gene_light.pmx'
    if (-not (Test-Path -LiteralPath $pmx)) { throw "Local test model missing: $pmx" }
    $invalid = Join-Path $artifacts 'invalid.pmx'
    [System.IO.File]::WriteAllBytes($invalid, [System.Text.Encoding]::ASCII.GetBytes('invalid pmx fixture'))
    $pmxCases = if ($OnlyComparison) { @() } else { @($false, $true) }
    foreach ($malformed in $pmxCases) {
        $name = if ($malformed) { 'pmx-player-invalid' } else { 'pmx-player' }
        $source = if ($malformed) { $invalid } else { $pmx }
        $reportPath = Join-Path $artifacts "$name.json"
        $started = [DateTime]::UtcNow
        $code = Invoke-ProbeProcess $exe @('-batchmode', '-pocPmx', $source,
            '-pocSmokeReport', $reportPath, '-logFile', (Join-Path $artifacts "$name.log")) 120
        $file = Get-Item -LiteralPath $reportPath
        if ($file.LastWriteTimeUtc -lt $started) { throw "Stale PMX report: $reportPath" }
        $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
        if ($malformed) {
            if ($code -ne 1 -or $report.success -or $report.error -notmatch 'Invalid PMX signature') {
                throw 'Malformed PMX was not rejected as expected.'
            }
        }
        elseif ($code -ne 0 -or -not $report.success -or $report.model.bones -lt 1 -or
            $report.model.blendShapes -lt 1 -or $report.model.textureSlots -ne $report.model.loadedTextures) {
            throw "External PMX import failed: $($report.error)"
        }
        $results += [pscustomobject]@{test=$name; exitCode=$code; expectedFailure=$malformed; report=$reportPath}
    }
    if (-not (Test-Path -LiteralPath (Join-Path (Split-Path $exe) 'MmdStudioPoC_Data\Plugins\x86_64\UMTNativePlugin.dll'))) {
        throw 'Windows native plugin is missing from the Player build.'
    }
}
if ($IncludeVmd) {
    if (-not $Vmd) { $Vmd = Join-Path $root 'test\aespa - Whiplash\aespa - Whiplash\Whiplash [SMOOTH].vmd' }
    $Vmd = (Get-Item -LiteralPath $Vmd).FullName
    $invalidVmd = Join-Path $artifacts 'invalid.vmd'
    [System.IO.File]::WriteAllBytes($invalidVmd, [byte[]]::new(64))
    $cameraVmd = Join-Path $root 'test\aespa - Whiplash\aespa - Whiplash\Camera [SMOOTH].vmd'
    $cases = @(
        @{Name='vmd-player'; Source=$Vmd; Failure=$false; Error=''},
        @{Name='vmd-player-invalid'; Source=$invalidVmd; Failure=$true; Error='Invalid VMD signature'},
        @{Name='vmd-player-camera-only'; Source=$cameraVmd; Failure=$true; Error='character VMD'}
    )
    if ($OnlyComparison) { $cases = @($cases[0]) }
    if ($IncludeComparison) {
        $cases += @{Name='vmd-player-default-ik'; Source=$Vmd; Failure=$false; Error=''; Mode='sparse-default-ik'}
        $cases += @{Name='vmd-player-baked-reference'; Source=$Vmd; Failure=$false; Error=''; Mode='baked-reference'}
        $cases += @{Name='vmd-player-direct'; Source=$Vmd; Failure=$false; Error=''; Mode='direct'}
        $cases += @{Name='vmd-player-direct-default-ik'; Source=$Vmd; Failure=$false; Error=''; Mode='direct-default-ik'}
    }
    foreach ($case in $cases) {
        $reportPath = Join-Path $artifacts "$($case.Name).json"
        $started = [DateTime]::UtcNow
        $arguments = @('-batchmode', '-pocPmx', $pmx, '-pocVmd', $case.Source,
            '-pocSmokeReport', $reportPath, '-logFile', (Join-Path $artifacts "$($case.Name).log"))
        if ($case.Mode) { $arguments += @('-pocMotionMode', $case.Mode) }
        $code = Invoke-ProbeProcess $exe $arguments 300
        $file = Get-Item -LiteralPath $reportPath
        if ($file.LastWriteTimeUtc -lt $started) { throw "Stale VMD report: $reportPath" }
        $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
        if ($case.Failure) {
            if ($code -ne 1 -or $report.success -or $report.error -notmatch $case.Error) {
                throw "VMD failure was not detected as expected: $($case.Name)"
            }
        }
        elseif ($code -ne 0 -or -not $report.success -or $report.motion.changedBones -lt 5 -or
            -not $report.motion.pausePassed -or -not $report.motion.loopPassed -or -not $report.motion.seekRepeatPassed) {
            throw "External VMD playback failed: $($report.error)"
        }
        $results += [pscustomobject]@{test=$case.Name; exitCode=$code; expectedFailure=$case.Failure; report=$reportPath}
    }
}
if ($IncludeComparison) {
    & python (Join-Path $PSScriptRoot 'compare_vmd_modes.py') --artifacts $artifacts --vmd $Vmd --require-direct-accuracy
    if ($LASTEXITCODE -ne 0) { throw 'VMD comparison analysis could not be completed.' }
}
if ($IncludeRecovery) {
    $recoveryPmx = Join-Path $root 'test\models\Gene\Gene_light.pmx'
    $recoveryVmd = if ($Vmd) { $Vmd } else { Join-Path $root 'test\aespa - Whiplash\aespa - Whiplash\Whiplash [SMOOTH].vmd' }
    $recoveryReport = Join-Path $artifacts 'poc-recovery.json'
    $started = [DateTime]::UtcNow
    $code = Invoke-ProbeProcess $exe @('-batchmode', '-pocPmx', $recoveryPmx, '-pocVmd', $recoveryVmd,
        '-pocRecoveryReport', $recoveryReport, '-logFile', (Join-Path $artifacts 'poc-recovery.log')) 180
    $file = Get-Item -LiteralPath $recoveryReport
    $report = Get-Content -LiteralPath $recoveryReport -Raw | ConvertFrom-Json
    if ($file.LastWriteTimeUtc -lt $started -or $code -ne 0 -or -not $report.success) {
        throw 'Import recovery/resource lifetime verification failed; see artifacts/poc-recovery.json/.log'
    }
    $results += [pscustomobject]@{test='import-recovery'; exitCode=$code; expectedFailure=$false; report=$recoveryReport}
}
if ($IncludeCamera) {
    $cameraReport = Join-Path $artifacts 'poc-camera.json'
    $cameraSource = Join-Path $root 'test\aespa - Whiplash\aespa - Whiplash\Camera [SMOOTH].vmd'
    $cameraModel = Join-Path $root 'test\models\Gene\Gene_light.pmx'
    $cameraMotion = Join-Path $root 'test\aespa - Whiplash\aespa - Whiplash\Whiplash [SMOOTH].vmd'
    $started = [DateTime]::UtcNow
    $code = Invoke-ProbeProcess $exe @('-batchmode', '-pocPmx', $cameraModel, '-pocVmd', $cameraMotion,
        '-pocCameraVmd', $cameraSource, '-pocCameraReport', $cameraReport,
        '-logFile', (Join-Path $artifacts 'poc-camera.log')) 180
    $file = Get-Item -LiteralPath $cameraReport
    $report = Get-Content -LiteralPath $cameraReport -Raw | ConvertFrom-Json
    if ($file.LastWriteTimeUtc -lt $started -or $code -ne 0 -or -not $report.success) {
        throw "Camera verification failed: $($report.error)"
    }
    & python (Join-Path $PSScriptRoot 'compare_vmd_camera.py') --report $cameraReport --vmd $cameraSource
    if ($LASTEXITCODE -ne 0) { throw 'Camera raw-key acceptance failed.' }
    $results += [pscustomobject]@{test='camera-runtime'; exitCode=$code; expectedFailure=$false; report=$cameraReport}
    # Mutate copies only. These validate explicit rejection of unsupported
    # projection and signed-distance inputs before allocating the camera rig.
    $cameraBytes = [System.IO.File]::ReadAllBytes($cameraSource)
    $cameraOffset = 50
    foreach ($recordSize in @(111, 23)) {
        $recordCount = [BitConverter]::ToUInt32($cameraBytes, $cameraOffset)
        $cameraOffset += 4 + $recordCount * $recordSize
    }
    $cameraOffset += 4
    foreach ($kind in @('projection', 'positive-distance')) {
        $fixture = [byte[]]$cameraBytes.Clone()
        if ($kind -eq 'projection') { $fixture[$cameraOffset + 60] = 1 }
        else { [BitConverter]::GetBytes([float]10).CopyTo($fixture, $cameraOffset + 4) }
        $fixturePath = Join-Path $artifacts "camera-$kind.vmd"
        [System.IO.File]::WriteAllBytes($fixturePath, $fixture)
        $failureReport = Join-Path $artifacts "camera-$kind.json"
        $started = [DateTime]::UtcNow
        $code = Invoke-ProbeProcess $exe @('-batchmode', '-pocPmx', $cameraModel, '-pocVmd', $cameraMotion,
            '-pocCameraVmd', $fixturePath, '-pocCameraReport', $failureReport,
            '-logFile', (Join-Path $artifacts "camera-$kind.log")) 180
        $file = Get-Item -LiteralPath $failureReport
        $report = Get-Content -LiteralPath $failureReport -Raw | ConvertFrom-Json
        if ($file.LastWriteTimeUtc -lt $started -or $code -ne 1 -or $report.success -or
            $report.error -notmatch 'projection toggle / positive distance') {
            throw "Unsupported camera input was not rejected: $kind"
        }
        $results += [pscustomobject]@{test="camera-$kind"; exitCode=$code; expectedFailure=$true; report=$failureReport}
    }
}
if ($IncludeDeformation) {
    $deformReport = Join-Path $artifacts 'poc-deformation.json'
    $started = [DateTime]::UtcNow
    $code = Invoke-ProbeProcess $exe @('-batchmode', '-pocPmx', (Join-Path $root 'test\models\Gene\Gene_light.pmx'),
        '-pocDeformationReport', $deformReport, '-logFile', (Join-Path $artifacts 'poc-deformation.log')) 180
    $file = Get-Item -LiteralPath $deformReport
    $report = Get-Content -LiteralPath $deformReport -Raw | ConvertFrom-Json
    if ($file.LastWriteTimeUtc -lt $started -or $code -ne 0 -or -not $report.success) {
        throw "Deformation verification failed: $($report.error)"
    }
    $results += [pscustomobject]@{test='morph-ik-runtime'; exitCode=$code; expectedFailure=$false; report=$deformReport}
}
if ($IncludePhysics) {
    # Preserve passed non-physics evidence even if experimental physics fails.
    if ($results.Count) { $results | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $artifacts 'poc-core-verification.json') -Encoding utf8 }
    $physicsFailures = @()
    $models = @(@{ Name = 'gene'; Path = Join-Path $root 'test\models\Gene\Gene_light.pmx' })
    $sandrone = Join-Path $root 'test\Sandrone_Runtime\桑多涅_目齿轮可动.pmx'
    if (Test-Path -LiteralPath $sandrone) { $models += @{ Name = 'sandrone'; Path = $sandrone } }
    foreach ($modelCase in $models) {
        $reportPrefix = if ($PhysicsReference) { 'poc-physics-reference' } else { 'poc-physics' }
        $physicsReport = Join-Path $artifacts "$reportPrefix-$($modelCase.Name).json"
        $started = [DateTime]::UtcNow
        $physicsArgument = if ($PhysicsReference) { '-pocPhysicsReferenceReport' } elseif ($PhysicsEndurance) { '-pocPhysicsEnduranceReport' } else { '-pocPhysicsReport' }
        $timeout = if ($PhysicsEndurance) { 1200 } else { 180 }
        $code = Invoke-ProbeProcess $exe @('-batchmode', '-pocPmx', $modelCase.Path,
            '-pocVmd', (Join-Path $root 'test\aespa - Whiplash\aespa - Whiplash\Whiplash [SMOOTH].vmd'),
            $physicsArgument, $physicsReport, '-logFile', (Join-Path $artifacts "poc-physics-$($modelCase.Name).log")) $timeout
        $file = Get-Item -LiteralPath $physicsReport
        $report = Get-Content -LiteralPath $physicsReport -Raw | ConvertFrom-Json
        if ($file.LastWriteTimeUtc -lt $started -or $code -ne 0 -or -not $report.success -or $report.backend -ne $Backend) {
            $physicsFailures += "Fixed-step physics failed for $($modelCase.Name): $($report.error)"
        }
        $results += [pscustomobject]@{test="physics-$($modelCase.Name)"; exitCode=$code; expectedFailure=$false; passed=$report.success; parityPassed=$report.parityPassed; report=$physicsReport}
    }
    $results | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $artifacts 'poc-physics-verification.json') -Encoding utf8
    if ($physicsFailures.Count) { throw ($physicsFailures -join "`n") }
}
$verificationFile = if ($OnlyPhysics) { 'poc-physics-verification.json' } elseif ($OnlyDeformation) { 'poc-deformation-verification.json' } elseif ($OnlyCamera) { 'poc-camera-verification.json' } elseif ($OnlyComparison) { 'poc-comparison-verification.json' } else { 'poc-verification.json' }
$results | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $artifacts $verificationFile) -Encoding utf8
$results | Format-Table -AutoSize
