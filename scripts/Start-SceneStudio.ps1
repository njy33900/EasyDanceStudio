param(
    [ValidateSet('Debug','Release')][string]$Configuration = 'Debug',
    [ValidateSet('Mono','IL2CPP')][string]$Backend = 'Mono',
    [string]$Pmx,
    [string]$Vmd,
    [string]$Scene
)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$outputRoot = Join-Path (Join-Path $root 'out') $Configuration.ToLowerInvariant()
$exe=Join-Path $outputRoot "MVP-10-$Backend\MmdStudioPoC.exe"
if(-not(Test-Path -LiteralPath $exe)){throw "Build first: scripts/Invoke-SceneVerification.ps1 -Ui -Configuration $Configuration -Backend $Backend"}
if($Vmd -and -not $Pmx){throw '-Vmd requires -Pmx'}
$info=[System.Diagnostics.ProcessStartInfo]::new($exe)
$info.UseShellExecute=$false
$info.WorkingDirectory=$root
$previewRoot=Join-Path $root "artifacts\scene-ui\$Backend"
if($Configuration -eq 'Release'){$previewRoot=Join-Path $previewRoot 'release'}
New-Item -ItemType Directory -Path $previewRoot -Force | Out-Null
$arguments=@('-sceneUi','-screen-fullscreen','0','-screen-width','1280','-screen-height','720',
    '-logFile',(Join-Path $previewRoot 'preview.log'))
if($Pmx){$arguments+=@('-pocPmx',(Get-Item -LiteralPath $Pmx).FullName)}
if($Vmd){$arguments+=@('-pocVmd',(Get-Item -LiteralPath $Vmd).FullName)}
if($Scene){$arguments+=@('-sceneFile',(Get-Item -LiteralPath $Scene).FullName)}
foreach($argument in $arguments){$info.ArgumentList.Add($argument)}
$player=[System.Diagnostics.Process]::Start($info)
Write-Output "Scene Studio started (PID $($player.Id)). Add characters, then connect each VMD."
$player.Dispose()
