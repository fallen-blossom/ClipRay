[CmdletBinding()]
param(
    [string] $WorkspaceRoot = (Split-Path -Parent $PSScriptRoot),
    [string] $PublishDirectory,
    [string] $DesktopDirectory = [Environment]::GetFolderPath('Desktop'),
    [switch] $SkipPublish
)

$ErrorActionPreference = 'Stop'
$WorkspaceRoot = [IO.Path]::GetFullPath($WorkspaceRoot)
if ([string]::IsNullOrWhiteSpace($PublishDirectory)) {
    $PublishDirectory = Join-Path $WorkspaceRoot 'artifacts\app'
}
$PublishDirectory = [IO.Path]::GetFullPath($PublishDirectory)
$DesktopDirectory = [IO.Path]::GetFullPath($DesktopDirectory)

function Copy-LocalLmtPayload([string] $DestinationRoot) {
    $modelSource = Join-Path $WorkspaceRoot 'artifacts\models\NiuTrans-LMT-60-0.6B\LMT-60-0.6B.Q4_K_M.gguf'
    $llamaSource = Join-Path $WorkspaceRoot 'artifacts\tools\llama-b8816-bin-win-vulkan-x64'
    $modelDestination = Join-Path $DestinationRoot 'models\local-lmt'
    $llamaDestination = Join-Path $DestinationRoot 'tools\llama'
    $licenseSource = Join-Path $WorkspaceRoot 'licenses\llama.cpp-MIT.txt'

    foreach ($required in @($modelSource, (Join-Path $llamaSource 'llama-cli.exe'), $licenseSource)) {
        if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
            throw "Required Local LMT publish input is missing: $required"
        }
    }

    $modelHash = (Get-FileHash -LiteralPath $modelSource -Algorithm SHA256).Hash
    if (-not $modelHash.Equals('743C6CDC13294B5C470EC0E89E79D64F96EF0CAB94CE1285C78DA4DC0F56BE5C', [StringComparison]::OrdinalIgnoreCase)) {
        throw "Local LMT model hash does not match the approved 0.6B artifact: $modelSource"
    }

    New-Item -ItemType Directory -Path $modelDestination, $llamaDestination, (Join-Path $DestinationRoot 'licenses') -Force | Out-Null
    Copy-Item -LiteralPath $modelSource -Destination $modelDestination -Force
    Copy-Item -LiteralPath (Join-Path $llamaSource 'llama-cli.exe') -Destination $llamaDestination -Force
    Get-ChildItem -LiteralPath $llamaSource -Filter '*.dll' -File | Copy-Item -Destination $llamaDestination -Force
    Copy-Item -LiteralPath $licenseSource -Destination (Join-Path $DestinationRoot 'licenses') -Force
}

if (-not $SkipPublish) {
    & dotnet publish (Join-Path $WorkspaceRoot 'src\ClipRay.App\ClipRay.App.csproj') `
        -c Release --self-contained true --no-restore -o $PublishDirectory | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "ClipRay publish failed with exit code $LASTEXITCODE."
    }

    Copy-LocalLmtPayload $PublishDirectory
}

$targetPath = Join-Path $PublishDirectory 'ClipRay.App.exe'
$iconPath = Join-Path $PublishDirectory 'Assets\ClipRay.ico'
foreach ($requiredPath in @($targetPath, $iconPath)) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "Required publish output is missing: $requiredPath"
    }
}

New-Item -ItemType Directory -Path $DesktopDirectory -Force | Out-Null
$shell = New-Object -ComObject WScript.Shell
$baseName = [string]::Concat('ClipRay ', [char]0x622A, [char]0x5149)
$shortcutPath = Join-Path $DesktopDirectory ($baseName + '.lnk')
$suffix = 2

while (Test-Path -LiteralPath $shortcutPath -PathType Leaf) {
    $belongsToWorkspace = $false
    try {
        $existing = $shell.CreateShortcut($shortcutPath)
        $existingTarget = $existing.TargetPath
        if (-not [string]::IsNullOrWhiteSpace($existingTarget)) {
            $existingFullPath = [IO.Path]::GetFullPath($existingTarget)
            $workspacePrefix = $WorkspaceRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
            $belongsToWorkspace = $existingFullPath.StartsWith($workspacePrefix, [StringComparison]::OrdinalIgnoreCase) -and
                [IO.Path]::GetFileName($existingFullPath).Equals('ClipRay.App.exe', [StringComparison]::OrdinalIgnoreCase)
        }
    }
    catch {
        # Unreadable or non-filesystem links are never safe to overwrite.
        $belongsToWorkspace = $false
    }

    if ($belongsToWorkspace) {
        break
    }

    $shortcutPath = Join-Path $DesktopDirectory ("$baseName ($suffix).lnk")
    $suffix++
}

$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $targetPath
$shortcut.WorkingDirectory = $PublishDirectory
$shortcut.Description = [string]::Concat(
    $baseName,
    ' - ',
    [char]0x672C,
    [char]0x5730,
    [char]0x622A,
    [char]0x56FE,
    [char]0x5DE5,
    [char]0x5177)
$shortcut.IconLocation = "$iconPath,0"
$shortcut.Save()

Write-Output $shortcutPath
