[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)] [string]$ModelDirectory,
    [Parameter(Mandatory)] [string]$LlamaRoot,
    [string]$OutputDirectory = $ModelDirectory,
    [switch]$Convert
)

$ErrorActionPreference = 'Stop'
$source = Join-Path $ModelDirectory 'model.safetensors'
$bf16 = Join-Path $OutputDirectory 'LMT-60-1.7B-BF16.gguf'
$q4 = Join-Path $OutputDirectory 'LMT-60-1.7B-Q4_K_M.gguf'

if (-not $Convert) {
    Write-Host 'No conversion performed. Re-run with -Convert after checking free space and the pinned llama.cpp revision.'
    exit 0
}
if (-not (Test-Path -LiteralPath $source)) { throw 'Pinned model.safetensors is missing.' }
if ((Test-Path -LiteralPath $bf16) -or (Test-Path -LiteralPath $q4)) { throw 'Refusing to overwrite an existing GGUF artifact.' }

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
& python (Join-Path $LlamaRoot 'convert_hf_to_gguf.py') $ModelDirectory --outfile $bf16 --outtype bf16
if ($LASTEXITCODE -ne 0) { throw "BF16 conversion failed with exit code $LASTEXITCODE." }
& (Join-Path $LlamaRoot 'llama-quantize.exe') $bf16 $q4 Q4_K_M
if ($LASTEXITCODE -ne 0) { throw "Q4 quantization failed with exit code $LASTEXITCODE." }
Get-FileHash -LiteralPath $bf16,$q4 -Algorithm SHA256 | Format-Table -AutoSize
Write-Host 'Conversion completed without deleting or replacing any input artifact.'
