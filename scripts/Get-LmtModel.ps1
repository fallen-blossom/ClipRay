[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$Destination = (Join-Path $PSScriptRoot '..\artifacts\models\NiuTrans-LMT-60-1.7B'),
    [switch]$Download
)

$ErrorActionPreference = 'Stop'
$revision = 'c468e3f82f71d3f5d112ebbbfcec8f0ee0bb6cdb'
$expectedSha256 = 'f32e78140294b4db8d560ca2ff280b723a5989ec63e12c4dd55459647722150b'
$modelFile = Join-Path $Destination 'model.safetensors'

if (-not $Download) {
    Write-Host "No download performed. Re-run with -Download after reviewing the pinned revision $revision."
    exit 0
}

New-Item -ItemType Directory -Force -Path $Destination | Out-Null
if (-not (Get-Command huggingface-cli -ErrorAction SilentlyContinue)) {
    throw 'huggingface-cli is required; install it separately and review its authentication/network behavior.'
}
& huggingface-cli download NiuTrans/LMT-60-1.7B --revision $revision --local-dir $Destination --include model.safetensors
if ($LASTEXITCODE -ne 0) { throw "Model download failed with exit code $LASTEXITCODE." }
$actual = (Get-FileHash -LiteralPath $modelFile -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actual -ne $expectedSha256) { throw 'Downloaded model hash does not match the pinned manifest.' }
Write-Host 'Pinned LMT source artifact verified. No files were deleted.'
