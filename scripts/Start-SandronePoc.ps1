param(
    [ValidateSet('Debug','Release')][string]$Configuration = 'Debug',
    [string]$Vmd,
    [ValidateSet('Mono','IL2CPP')][string]$Backend = 'Mono'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$model = Join-Path $root 'test\Sandrone_Runtime\桑多涅_目齿轮可动.pmx'
if (-not (Test-Path -LiteralPath $model)) {
    throw 'Prepared local model missing. See docs/SANDRONE_TEST.md; keep textures beside the PMX.'
}
$options = @{ Pmx = $model; Backend = $Backend; Configuration = $Configuration }
if ($Vmd) { $options.Vmd = $Vmd }
& (Join-Path $PSScriptRoot 'Start-MmdPoc.ps1') @options
