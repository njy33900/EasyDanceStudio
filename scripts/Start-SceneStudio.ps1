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
$exe=Join-Path $outputRoot "V0-$Backend\MmdStudioPoC.exe"
if(-not(Test-Path -LiteralPath $exe)){throw "Build first: scripts/Invoke-SceneVerification.ps1 -Ui -Configuration $Configuration -Backend $Backend"}
if($Vmd -and -not $Pmx){throw '-Vmd requires -Pmx'}
$info=[System.Diagnostics.ProcessStartInfo]::new($exe)
$info.UseShellExecute=$false
$info.WorkingDirectory=$root
$previewRoot=Join-Path $root "artifacts\scene-ui\$Backend"
if($Configuration -eq 'Release'){$previewRoot=Join-Path $previewRoot 'release'}
New-Item -ItemType Directory -Path $previewRoot -Force | Out-Null
# No forced window size: Unity reopens with the size and mode (window / full screen) the app had
# when it was last closed. The window is resizable; F11 toggles full screen.
$arguments=@('-sceneUi','-logFile',(Join-Path $previewRoot 'preview.log'))
if($Pmx){$arguments+=@('-pocPmx',(Get-Item -LiteralPath $Pmx).FullName)}
if($Vmd){$arguments+=@('-pocVmd',(Get-Item -LiteralPath $Vmd).FullName)}
if($Scene){$arguments+=@('-sceneFile',(Get-Item -LiteralPath $Scene).FullName)}
# ProcessStartInfo.ArgumentList exists only in PowerShell 7 (.NET Core); quote for Windows PowerShell 5.1 too.
$info.Arguments=($arguments | ForEach-Object { '"' + ($_ -replace '"','\"') + '"' }) -join ' '
$player=[System.Diagnostics.Process]::Start($info)
Write-Output "Scene Studio started (PID $($player.Id)). Add characters, then connect each VMD."
$player.Dispose()
