[CmdletBinding()]
param(
    [string] $WorkspaceRoot = (Split-Path -Parent $PSScriptRoot),
    [string] $IsccPath = 'E:\Tools\InnoSetup\6\ISCC.exe',
    [string] $OutputDirectory
)

$ErrorActionPreference = 'Stop'
$WorkspaceRoot = [IO.Path]::GetFullPath($WorkspaceRoot)
$artifactsRoot = Join-Path $WorkspaceRoot 'artifacts'
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $artifactsRoot 'installer'
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$runId = [Guid]::NewGuid().ToString('N')
$staging = Join-Path $artifactsRoot "installer-staging-recording-patch-$runId"
$project = Join-Path $WorkspaceRoot 'src\ClipRay.App\ClipRay.App.csproj'
$installerDefinition = Join-Path $WorkspaceRoot 'installer\ClipRay.RecordingDiagnosticPatch.iss'
$patchIcon = Join-Path $WorkspaceRoot 'src\ClipRay.App\Assets\ClipRay-v2.ico'

function Assert-ChildPath([string] $Path, [string] $Parent) {
    $fullPath = [IO.Path]::GetFullPath($Path)
    $fullParent = [IO.Path]::GetFullPath($Parent).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $fullPath.StartsWith($fullParent, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Generated path escaped the artifacts root: $fullPath"
    }
}

Assert-ChildPath $staging $artifactsRoot
foreach ($required in @($project, $installerDefinition, $patchIcon, $IsccPath)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
        throw "Required diagnostic patch input is missing: $required"
    }
}

try {
    New-Item -ItemType Directory -Path $OutputDirectory, $staging -Force | Out-Null
    & dotnet restore $project -r win-x64 --locked-mode | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "ClipRay win-x64 restore failed with exit code $LASTEXITCODE." }

    & dotnet publish $project -c Release -r win-x64 --self-contained true --no-restore `
        -p:DebugType=None -p:DebugSymbols=false -o $staging | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "ClipRay diagnostic patch publish failed with exit code $LASTEXITCODE." }

    $patchedDll = Join-Path $staging 'ClipRay.App.dll'
    if (-not (Test-Path -LiteralPath $patchedDll -PathType Leaf)) {
        throw "Published diagnostic assembly is missing: $patchedDll"
    }

    & $IsccPath "/DPatchedDll=$patchedDll" "/DPatchIcon=$patchIcon" "/DOutputDir=$OutputDirectory" $installerDefinition | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "Diagnostic patch compilation failed with exit code $LASTEXITCODE." }
}
finally {
    if (Test-Path -LiteralPath $staging) {
        Remove-Item -LiteralPath $staging -Recurse -Force
    }
}

exit 0
