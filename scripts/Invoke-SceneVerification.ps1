param(
    [ValidateSet('Debug','Release')][string]$Configuration = 'Debug',
    [switch]$SkipBuild,
    [switch]$Ui,
    [ValidateSet('Mono','IL2CPP')][string]$Backend = 'Mono',
    [string]$Unity = 'C:\Program Files\Unity\Hub\Editor\6000.3.28f1\Editor\Unity.exe'
)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$outputRoot = Join-Path (Join-Path $root 'out') $Configuration.ToLowerInvariant()
$artifacts=Join-Path $root "artifacts\scene\$Backend"
$exe=Join-Path $outputRoot "MVP-01-$Backend\MmdStudioPoC.exe"
if($Ui){
    $artifacts=Join-Path $root "artifacts\scene-ui\$Backend"
    $exe=Join-Path $outputRoot "V0-$Backend\MmdStudioPoC.exe"
}
if ($Configuration -eq 'Release') { $artifacts = Join-Path $artifacts 'release' }
New-Item -ItemType Directory -Path $artifacts -Force | Out-Null
function Invoke-Hidden([string]$File,[string[]]$Arguments,[int]$TimeoutSeconds) {
    $info=[System.Diagnostics.ProcessStartInfo]::new($File)
    $info.UseShellExecute=$false
    $info.CreateNoWindow=$true
    $info.WindowStyle=[System.Diagnostics.ProcessWindowStyle]::Hidden
    if($Ui -and $File -eq $exe){$info.WindowStyle=[System.Diagnostics.ProcessWindowStyle]::Normal}
    $info.WorkingDirectory=$root
    foreach($argument in $Arguments){$info.ArgumentList.Add($argument)}
    $process=[System.Diagnostics.Process]::Start($info)
    try {
        if(-not $process.WaitForExit($TimeoutSeconds*1000)){$process.Kill($true);throw "Timed out: $File"}
        return $process.ExitCode
    }finally{$process.Dispose()}
}
if(-not $SkipBuild){
    $code=Invoke-Hidden $Unity @('-batchmode','-nographics','-quit','-projectPath',(Join-Path $root 'MMDStudio'),
        '-pocConfiguration', $Configuration.ToLowerInvariant(), '-executeMethod','MmdStudio.Poc.PocBuild.BuildWindows','-pocOutput',$exe,'-pocBackend',$Backend.ToLowerInvariant(),
        '-logFile',(Join-Path $artifacts 'poc-build.log')) 1800
    if($code -ne 0){throw "Build failed: $code"}
}
$path=Join-Path $artifacts $(if($Ui){'scene-ui.json'}else{'scene.json'})
$stageFixtures=Join-Path $artifacts 'fixtures'
if($Ui){& python (Join-Path $PSScriptRoot 'New-StageFixture.py') --output $stageFixtures; if($LASTEXITCODE -ne 0){throw 'Stage fixture generation failed'}}
$reportFlag=if($Ui){'-pocSceneUiReport'}else{'-pocSceneReport'}
$started=[DateTime]::UtcNow
$arguments=@('-pocPmx',(Join-Path $root 'test\models\Gene\Gene_light.pmx'),
    '-pocSecondPmx',(Join-Path $root 'test\Sandrone_Runtime\桑多涅_目齿轮可动.pmx'),
    '-pocVmd',(Join-Path $root 'test\aespa - Whiplash\aespa - Whiplash\Whiplash [SMOOTH].vmd'),
    $reportFlag,$path,'-logFile',(Join-Path $artifacts 'scene.log'))
# Unity ScreenCapture does not produce the UI/backbuffer image in batch mode.
if(-not $Ui){$arguments=@('-batchmode')+$arguments}
else{$arguments+=@('-pocStagePmx',(Join-Path $stageFixtures 'stage.pmx'),'-screen-fullscreen','0','-screen-width','1280','-screen-height','720')}
$code=Invoke-Hidden $exe $arguments 300
$report=Get-Content -LiteralPath $path -Raw|ConvertFrom-Json
if((Get-Item -LiteralPath $path).LastWriteTimeUtc -lt $started -or $code -ne 0 -or -not $report.success -or
    $report.backend -ne $Backend -or $report.checks.Count -lt $(if($Ui){130}else{26})){throw "Scene verification failed: $($report.error)"}
if($Ui){
    $report|Select-Object success,backend,@{Name='checks';Expression={$_.checks.Count}},screenshot|Format-List
    # Restart check: a fresh Player process opens the .mmdscene saved by the UI run.
    $restorePath=Join-Path $artifacts 'scene-restore.json'
    $restoreStarted=[DateTime]::UtcNow
    $code=Invoke-Hidden $exe @('-pocSceneRestoreReport',$restorePath,'-sceneFile',(Join-Path $artifacts 'project\scene.mmdscene'),
        '-logFile',(Join-Path $artifacts 'scene-restore.log'),'-screen-fullscreen','0','-screen-width','1280','-screen-height','720') 300
    $restore=Get-Content -LiteralPath $restorePath -Raw|ConvertFrom-Json
    if((Get-Item -LiteralPath $restorePath).LastWriteTimeUtc -lt $restoreStarted -or $code -ne 0 -or -not $restore.success -or
        $restore.backend -ne $Backend -or $restore.checks.Count -lt 9){throw "Scene restore verification failed: $($restore.error)"}
    $restore|Select-Object success,backend,@{Name='checks';Expression={$_.checks.Count}},objects,screenshot|Format-List
}
else{$report|Select-Object success,backend,@{Name='checks';Expression={$_.checks.Count}},maximumReplayError,realtimeSeconds,screenshot|Format-List}
