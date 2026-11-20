param(
    [ValidateSet('Debug','Release')][string]$Configuration = 'Debug',
    [switch]$EmptyWorkspace,
    [string]$Pmx,
    [string]$Vmd,
    [string]$CameraVmd,
    [ValidateSet('Mono', 'IL2CPP')]
    [string]$Backend = 'Mono',
    [ValidateSet('direct', 'direct-default-ik', 'baked-reference', 'sparse', 'sparse-default-ik')]
    [string]$MotionMode = 'direct'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$outputRoot = Join-Path (Join-Path $root 'out') $Configuration.ToLowerInvariant()
$exe = Join-Path $outputRoot "POC-06C-$Backend\MmdStudioPoC.exe"
if (-not $Pmx) { $Pmx = Join-Path $root 'test\models\Gene\Gene_light.pmx' }
else { $Pmx = (Get-Item -LiteralPath $Pmx).FullName }
if (-not $Vmd) { $Vmd = Join-Path $root 'test\aespa - Whiplash\aespa - Whiplash\Whiplash [SMOOTH].vmd' }
$requiredPaths = if ($EmptyWorkspace) { @($exe) } else { @($exe, $pmx, $Vmd) }
foreach ($path in $requiredPaths) {
    if (-not (Test-Path -LiteralPath $path)) { throw "Required file missing: $path" }
}
$previewRoot = Join-Path $root 'artifacts'
if ($Configuration -eq 'Release') { $previewRoot = Join-Path $previewRoot 'release' }
New-Item -ItemType Directory -Path $previewRoot -Force | Out-Null
# This is the user-invoked interactive preview, not the hidden verification run.
$info = [System.Diagnostics.ProcessStartInfo]::new($exe)
$info.UseShellExecute = $false
$info.WorkingDirectory = $root
$arguments = @('-legacyWorkspace', '-screen-fullscreen', '0', '-screen-width', '1280', '-screen-height', '720',
    '-pocMotionMode', $MotionMode, '-logFile', (Join-Path $previewRoot 'poc-preview.log'))
if (-not $EmptyWorkspace) { $arguments += @('-pocPmx', $pmx, '-pocVmd', (Get-Item -LiteralPath $Vmd).FullName) }
if ($CameraVmd) {
    if ($EmptyWorkspace) { throw '-CameraVmd requires the model/motion preview; omit -EmptyWorkspace.' }
    $arguments += @('-pocCameraVmd', (Get-Item -LiteralPath $CameraVmd).FullName)
}
foreach ($argument in $arguments) { $info.ArgumentList.Add($argument) }
$process = [System.Diagnostics.Process]::Start($info)
Write-Output "MMD PoC preview started (PID $($process.Id)). Close the window to stop."
$process.Dispose()
