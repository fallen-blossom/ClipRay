$ErrorActionPreference = 'Stop'

$workspace = Split-Path -Parent $PSScriptRoot
$scriptPath = Join-Path $workspace 'scripts\Build-Installer.ps1'
$tokens = $null
$parseErrors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile(
    $scriptPath, [ref] $tokens, [ref] $parseErrors)
if ($parseErrors.Count -ne 0) {
    throw "Build-Installer.ps1 has parse errors: $($parseErrors -join '; ')"
}

# Load only the pure checks. Running Build-Installer.ps1 would publish and clean staging.
foreach ($name in @('Get-InstallerOwnedCleanupPaths', 'Assert-InstallerStagingFlavor')) {
    $definition = $ast.FindAll({
        param($node)
        $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
            $node.Name -eq $name
    }, $true) | Select-Object -First 1
    if ($null -eq $definition) { throw "Missing installer validation function: $name" }
    . ([scriptblock]::Create($definition.Extent.Text))
}

$scriptText = Get-Content -LiteralPath $scriptPath -Raw
if (-not $scriptText.Contains('Assert-InstallerStagingFlavor $staging $Flavor $WorkspaceRoot $approvedLocalModelSha256') -or
    -not $scriptText.Contains('Get-InstallerOwnedCleanupPaths $staging $sourceStaging $reusePreparedStaging')) {
    throw 'Installer does not call the tested validation and cleanup functions.'
}
if (-not $scriptText.Contains("`$includeLocalModel = `$Flavor -eq 'Full'") -or
    -not $scriptText.Contains('-p:ClipRayIncludeCudaRuntime=$($includeLocalModel.ToString().ToLowerInvariant())')) {
    throw 'Normal Lite publish must explicitly omit the optional CUDA runtime.'
}

function Assert-Equal([object] $Expected, [object] $Actual, [string] $Case) {
    if (-not [object]::Equals($Expected, $Actual)) {
        throw "$Case expected '$Expected', got '$Actual'."
    }
}

function Assert-FailsWith([scriptblock] $Action, [string] $ExpectedText, [string] $Case) {
    $message = $null
    try { & $Action | Out-Null }
    catch { $message = $_.Exception.Message }
    if ($null -eq $message -or -not $message.Contains($ExpectedText, [StringComparison]::Ordinal)) {
        throw "$Case expected failure containing '$ExpectedText', got '$message'."
    }
}

function Write-FixtureFile([string] $Root, [string] $RelativePath, [string] $Content) {
    $path = Join-Path $Root $RelativePath
    [void] [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path))
    [IO.File]::WriteAllText($path, $Content)
}

$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('clipray-installer-flavor-test-' + [Guid]::NewGuid().ToString('N'))
$sourceRoot = Join-Path $testRoot 'source'
$modelRelative = 'models\local-lmt\LMT-60-0.6B.Q4_K_M.gguf'
$cliRelative = 'tools\llama\llama-cli.exe'
$dllRelative = 'tools\llama\ggml-vulkan.dll'
$apacheRelative = 'licenses\Apache-2.0.txt'
$llamaLicenseRelative = 'licenses\llama.cpp-MIT.txt'
$modelContent = 'synthetic approved model'
$approvedHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($modelContent)))
$fullFiles = [ordered]@{
    $modelRelative = $modelContent
    $cliRelative = 'synthetic llama cli'
    $dllRelative = 'synthetic Vulkan DLL'
    $apacheRelative = 'synthetic Apache license'
    $llamaLicenseRelative = 'synthetic llama license'
}

function New-FullFixture([string] $Name, [string[]] $Omit = @()) {
    $root = Join-Path $testRoot $Name
    [void] [IO.Directory]::CreateDirectory($root)
    foreach ($relative in $fullFiles.Keys) {
        if ($relative -notin $Omit) { Write-FixtureFile $root $relative $fullFiles[$relative] }
    }
    return $root
}

[void] [IO.Directory]::CreateDirectory($testRoot)
try {
    Write-FixtureFile $sourceRoot 'artifacts\tools\llama-b8816-bin-win-vulkan-x64\llama-cli.exe' $fullFiles[$cliRelative]
    Write-FixtureFile $sourceRoot 'artifacts\tools\llama-b8816-bin-win-vulkan-x64\ggml-vulkan.dll' $fullFiles[$dllRelative]
    Write-FixtureFile $sourceRoot $apacheRelative $fullFiles[$apacheRelative]
    Write-FixtureFile $sourceRoot $llamaLicenseRelative $fullFiles[$llamaLicenseRelative]

    $generated = Join-Path $testRoot 'generated-output'
    $prepared = Join-Path $testRoot 'caller-owned-input'
    $source = Join-Path $testRoot 'source-output'
    $ordinaryCleanup = @(Get-InstallerOwnedCleanupPaths $generated $source $false $false)
    Assert-Equal 2 $ordinaryCleanup.Count 'Generated cleanup count'
    Assert-Equal $generated $ordinaryCleanup[0] 'Generated staging cleanup'
    Assert-Equal $source $ordinaryCleanup[1] 'Source staging cleanup'
    $preparedCleanup = @(Get-InstallerOwnedCleanupPaths $prepared $source $true $false)
    Assert-Equal 1 $preparedCleanup.Count 'Prepared cleanup count'
    Assert-Equal $source $preparedCleanup[0] 'Prepared staging must be preserved'
    Assert-Equal 0 @(Get-InstallerOwnedCleanupPaths $prepared $source $true $true).Count 'KeepStaging cleanup count'
    Assert-Equal 0 @(Get-InstallerOwnedCleanupPaths $generated $source $false $true).Count 'Keep generated staging cleanup count'

    $lite = Join-Path $testRoot 'lite-machine'
    Write-FixtureFile $lite 'machine-translation\models\translate-en_zh-1_9\model\model.bin' 'synthetic machine model'
    Assert-InstallerStagingFlavor $lite 'Lite' $sourceRoot $approvedHash
    foreach ($case in @(
        @('lite-model-folder', 'models\local-lmt\model.txt', 'Full-only payload path: models\local-lmt'),
        @('lite-llama-folder', 'tools\llama\notes.txt', 'Full-only payload path: tools\llama'),
        @('lite-cuda-folder', 'machine-translation\gpu\runtime-manifest.json', 'Full-only payload path: machine-translation\gpu'),
        @('lite-llama-license', 'licenses\llama.cpp-MIT.txt', 'Full-only payload path: licenses\llama.cpp-MIT.txt'),
        @('lite-stray-model', 'other\surprise.gguf', 'Full-only payload file: other\surprise.gguf'),
        @('lite-stray-cli', 'other\llama-cli.exe', 'Full-only payload file: other\llama-cli.exe'),
        @('lite-stray-llama-dll', 'other\ggml-vulkan.dll', 'Full-only payload file: other\ggml-vulkan.dll'),
        @('lite-stray-dll', 'other\cublas64_12.dll', 'Full-only payload file: other\cublas64_12.dll')
    )) {
        $root = Join-Path $testRoot $case[0]
        Write-FixtureFile $root $case[1] 'synthetic Full-only payload'
        Assert-FailsWith { Assert-InstallerStagingFlavor $root 'Lite' $sourceRoot $approvedHash } $case[2] $case[0]
    }

    $full = New-FullFixture 'full-valid'
    Assert-InstallerStagingFlavor $full 'Full' $sourceRoot $approvedHash
    foreach ($case in @(
        @('full-no-model', $modelRelative, 'missing the approved Local LMT model'),
        @('full-no-cli', $cliRelative, 'missing llama runtime file: tools\llama\llama-cli.exe'),
        @('full-no-dll', $dllRelative, 'missing llama runtime file: tools\llama\ggml-vulkan.dll'),
        @('full-no-apache', $apacheRelative, 'missing local model license: licenses\Apache-2.0.txt'),
        @('full-no-llama-license', $llamaLicenseRelative, 'missing local model license: licenses\llama.cpp-MIT.txt')
    )) {
        $root = New-FullFixture $case[0] @($case[1])
        Assert-FailsWith { Assert-InstallerStagingFlavor $root 'Full' $sourceRoot $approvedHash } $case[2] $case[0]
    }
    $badModel = New-FullFixture 'full-bad-model'
    Write-FixtureFile $badModel $modelRelative 'wrong model'
    Assert-FailsWith { Assert-InstallerStagingFlavor $badModel 'Full' $sourceRoot $approvedHash } 'unexpected Local LMT model hash' 'Full model hash'
    $badRuntime = New-FullFixture 'full-bad-runtime'
    Write-FixtureFile $badRuntime $dllRelative 'wrong DLL'
    Assert-FailsWith { Assert-InstallerStagingFlavor $badRuntime 'Full' $sourceRoot $approvedHash } 'unexpected llama runtime file: tools\llama\ggml-vulkan.dll' 'Full runtime hash'
    $badLicense = New-FullFixture 'full-bad-license'
    Write-FixtureFile $badLicense $llamaLicenseRelative 'wrong license'
    Assert-FailsWith { Assert-InstallerStagingFlavor $badLicense 'Full' $sourceRoot $approvedHash } 'outdated local model license: licenses\llama.cpp-MIT.txt' 'Full license hash'
    $noDllSource = Join-Path $testRoot 'source-no-llama-dll'
    Write-FixtureFile $noDllSource 'artifacts\tools\llama-b8816-bin-win-vulkan-x64\llama-cli.exe' $fullFiles[$cliRelative]
    Assert-FailsWith { Assert-InstallerStagingFlavor $full 'Full' $noDllSource $approvedHash } 'input has no llama runtime DLLs' 'Full source DLL inventory'

    Write-Output 'PASS installer flavor staging and caller-owned cleanup checks'
}
finally {
    $resolvedTestRoot = [IO.Path]::GetFullPath($testRoot)
    $tempPrefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $resolvedTestRoot.StartsWith($tempPrefix, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($resolvedTestRoot) -notmatch '^clipray-installer-flavor-test-[0-9a-f]{32}$') {
        throw "Refusing to remove unexpected test fixture path: $resolvedTestRoot"
    }
    if (Test-Path -LiteralPath $resolvedTestRoot) { Remove-Item -LiteralPath $resolvedTestRoot -Recurse -Force }
}
