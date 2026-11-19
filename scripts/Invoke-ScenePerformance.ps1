# MVP-09 integration/performance measurement (spec NFR-03/04, 8.2).
# Runs the existing scene workspace build in a 1920x1080 window: two characters with the
# full Whiplash VMD, one generated stage, basic effects; records load/save/open times,
# frame times for three graphics passes and memory. Build first with
# Invoke-SceneVerification.ps1 -Ui (same -Configuration/-Backend), or omit -SkipBuild there.
param(
    [ValidateSet('Debug','Release')][string]$Configuration = 'Release',
    [ValidateSet('Mono','IL2CPP')][string]$Backend = 'Mono'
)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$exe=Join-Path (Join-Path (Join-Path $root 'out') $Configuration.ToLowerInvariant()) "MVP-10-$Backend\MmdStudioPoC.exe"
if(-not(Test-Path -LiteralPath $exe)){throw "Build first: scripts/Invoke-SceneVerification.ps1 -Ui -Configuration $Configuration -Backend $Backend"}
$artifacts=Join-Path $root "artifacts\performance\$Backend-$($Configuration.ToLowerInvariant())"
New-Item -ItemType Directory -Path $artifacts -Force | Out-Null
& python (Join-Path $PSScriptRoot 'New-StageFixture.py') --output (Join-Path $artifacts 'fixtures')
if($LASTEXITCODE -ne 0){throw 'Stage fixture generation failed'}
$report=Join-Path $artifacts 'performance.json'
$started=[DateTime]::UtcNow
$info=[System.Diagnostics.ProcessStartInfo]::new($exe)
$info.UseShellExecute=$false
$info.WorkingDirectory=$root
foreach($argument in @('-pocScenePerfReport',$report,
    '-pocPmx',(Join-Path $root 'test\models\Gene\Gene_light.pmx'),
    '-pocSecondPmx',(Join-Path $root 'test\Sandrone_Runtime\桑多涅_目齿轮可动.pmx'),
    '-pocVmd',(Join-Path $root 'test\aespa - Whiplash\aespa - Whiplash\Whiplash [SMOOTH].vmd'),
    '-pocStagePmx',(Join-Path $artifacts 'fixtures\stage.pmx'),
    '-logFile',(Join-Path $artifacts 'performance.log'),
    '-screen-fullscreen','0','-screen-width','1920','-screen-height','1080')){$info.ArgumentList.Add($argument)}
$process=[System.Diagnostics.Process]::Start($info)
try{ if(-not $process.WaitForExit(600000)){$process.Kill($true);throw 'Performance run timed out'} $code=$process.ExitCode }finally{$process.Dispose()}
$result=Get-Content -LiteralPath $report -Raw|ConvertFrom-Json
if((Get-Item -LiteralPath $report).LastWriteTimeUtc -lt $started -or $code -ne 0 -or -not $result.success){throw "Performance run failed: $($result.error)"}
$result|Select-Object backend,configuration,gpu,screenWidth,screenHeight,importFirstCharacterSeconds,importSecondCharacterSeconds,connectMotionsSeconds,importStageSeconds,saveSeconds,openSceneSeconds|Format-List
$result.passes|Select-Object name,averageFps,medianFrameMs,p95FrameMs,p99FrameMs,worstFrameMs,onePercentLowFps,meetsThirtyFpsTarget|Format-Table -AutoSize
