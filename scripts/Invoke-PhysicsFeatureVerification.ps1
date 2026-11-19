param(
    [ValidateSet('Debug','Release')][string]$Configuration = 'Debug',
    [switch]$SkipBuild,
    [switch]$RealMotion,
    [string]$Seconds = '30',
    [ValidateSet('Mono','IL2CPP')][string]$Backend = 'Mono',
    [string]$Unity = 'C:\Program Files\Unity\Hub\Editor\6000.3.28f1\Editor\Unity.exe'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$outputRoot = Join-Path (Join-Path $root 'out') $Configuration.ToLowerInvariant()
$artifacts = Join-Path $root "artifacts\physics-features\$Backend"
$exe = Join-Path $outputRoot "POC-06H-$Backend\MmdStudioPoC.exe"
if ($Configuration -eq 'Release') { $artifacts = Join-Path $artifacts 'release' }
New-Item -ItemType Directory -Path $artifacts -Force | Out-Null
function Invoke-Hidden([string]$File, [string[]]$Arguments, [int]$TimeoutSeconds) {
    $info = [System.Diagnostics.ProcessStartInfo]::new($File)
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.WindowStyle = [System.Diagnostics.ProcessWindowStyle]::Hidden
    $info.WorkingDirectory = $root
    foreach ($argument in $Arguments) { $info.ArgumentList.Add($argument) }
    $process = [System.Diagnostics.Process]::Start($info)
    try {
        if (-not $process.WaitForExit($TimeoutSeconds * 1000)) { $process.Kill($true); throw "Timed out: $File" }
        return $process.ExitCode
    }
    finally { $process.Dispose() }
}
& python (Join-Path $PSScriptRoot 'New-PhysicsChainFixtures.py') --output (Join-Path $artifacts 'fixtures')
if ($LASTEXITCODE -ne 0) { throw 'Fixture generation failed' }
if (-not $SkipBuild) {
    $code = Invoke-Hidden $Unity @('-batchmode','-nographics','-quit','-projectPath',(Join-Path $root 'MMDStudio'),
        '-pocConfiguration', $Configuration.ToLowerInvariant(), '-executeMethod','MmdStudio.Poc.PocBuild.BuildWindows','-pocOutput',$exe,'-pocBackend',$Backend.ToLowerInvariant(),
        '-logFile',(Join-Path $artifacts 'poc-build.log')) 1800
    if ($code -ne 0) { throw "Build failed: $code" }
}
$cases = @('append-driven','ik-driven') | ForEach-Object { @{Name=$_; Path=Join-Path $artifacts "fixtures\$_.pmx"; Real=$false} }
if ($RealMotion) {
    $cases += @{Name='gene'; Path=Join-Path $root 'test\models\Gene\Gene_light.pmx'; Real=$true}
    $cases += @{Name='sandrone'; Path=Join-Path $root 'test\Sandrone_Runtime\桑多涅_目齿轮可动.pmx'; Real=$true}
}
$results = @()
foreach ($case in $cases) {
    $caseDir = Join-Path $artifacts $case.Name
    New-Item -ItemType Directory -Path $caseDir -Force | Out-Null
    $path = Join-Path $caseDir 'report.json'
    $reportFlag = if ($case.Real) { '-pocPhysicsRealMotionReport' } else { '-pocPhysicsFeatureReport' }
    $arguments = @('-batchmode','-pocPmx',$case.Path,$reportFlag,$path,'-logFile',(Join-Path $caseDir 'player.log'))
    if ($case.Real) {
        $arguments += @('-pocVmd',(Join-Path $root 'test\aespa - Whiplash\aespa - Whiplash\Whiplash [SMOOTH].vmd'),'-pocPhysicsSeconds',$Seconds)
    }
    $started = [DateTime]::UtcNow
    $code = Invoke-Hidden $exe $arguments 1800
    $report = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
    if ((Get-Item -LiteralPath $path).LastWriteTimeUtc -lt $started -or $code -ne 0 -or -not $report.success -or
        $report.backend -ne $Backend -or $report.checks -ne 3) { throw "Feature/real motion failed: $($case.Name): $($report.error)" }
    $results += $report
    $results | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $artifacts 'verification.json') -Encoding utf8
    Write-Output "$($case.Name): passed; steps=$($report.steps); replay=$($report.maximumReplayError)"
}
