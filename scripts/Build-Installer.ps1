[CmdletBinding()]
param(
    [string] $WorkspaceRoot = (Split-Path -Parent $PSScriptRoot),
    [string] $IsccPath = 'E:\Tools\InnoSetup\6\ISCC.exe',
    [string] $OutputDirectory,
    [string] $PreparedStaging,
    [ValidateSet('Lite', 'Full')]
    [string] $Flavor = 'Lite',
    [switch] $KeepStaging
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Release-Identity.ps1')
$WorkspaceRoot = [IO.Path]::GetFullPath($WorkspaceRoot)
$artifactsRoot = Join-Path $WorkspaceRoot 'artifacts'
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $artifactsRoot 'installer'
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$runId = [Guid]::NewGuid().ToString('N')
$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) "clipray-installer-$runId"
$staging = if ([string]::IsNullOrWhiteSpace($PreparedStaging)) {
    Join-Path $temporaryRoot 'payload'
} else {
    [IO.Path]::GetFullPath($PreparedStaging)
}
$reusePreparedStaging = -not [string]::IsNullOrWhiteSpace($PreparedStaging)
$sourceStaging = Join-Path $temporaryRoot 'source'
$installerDefinition = Join-Path $WorkspaceRoot 'installer\ClipRay.iss'
$installerContent = Get-Content -LiteralPath $installerDefinition -Raw
if ($installerContent -notmatch '#define AppVersion "([^"]+)"') {
    throw 'Installer definition does not declare AppVersion.'
}
$appVersion = $Matches[1]
$includeLocalModel = $Flavor -eq 'Full'
$approvedLocalModelSha256 = '743C6CDC13294B5C470EC0E89E79D64F96EF0CAB94CE1285C78DA4DC0F56BE5C'
$localModelSource = Join-Path $WorkspaceRoot 'artifacts\models\NiuTrans-LMT-60-0.6B\LMT-60-0.6B.Q4_K_M.gguf'
$sourceCommit = & git -C $WorkspaceRoot rev-parse --verify HEAD
if ($LASTEXITCODE -ne 0 -or $sourceCommit -notmatch '^[0-9a-f]{40}$') { throw 'Installer source must be a Git commit.' }
& git -C $WorkspaceRoot diff --quiet HEAD --
if ($LASTEXITCODE -ne 0) { throw 'Commit tracked changes before building an installer and its corresponding source.' }
$untrackedInputs = @(& git -C $WorkspaceRoot ls-files --others --exclude-standard)
if ($LASTEXITCODE -ne 0 -or $untrackedInputs.Count -gt 0) { throw 'Commit or move untracked inputs before building an installer from an exact source revision.' }
$informationalVersion = "$appVersion+$sourceCommit"
Assert-ReleaseOrdinaryPath $OutputDirectory
$sourceArchive = Join-Path $OutputDirectory "ClipRay-$appVersion-source.zip"
Assert-SourceArchiveRevision $sourceArchive $appVersion $sourceCommit
if ($reusePreparedStaging) { Assert-ReleaseIdentity $staging $sourceCommit $informationalVersion $Flavor }

function Assert-ChildPath([string] $Path, [string] $Parent) {
    $fullPath = [IO.Path]::GetFullPath($Path)
    $fullParent = [IO.Path]::GetFullPath($Parent).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $fullPath.StartsWith($fullParent, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Generated path escaped the artifacts root: $fullPath"
    }
}

Assert-ChildPath $staging $(if ($reusePreparedStaging) { $artifactsRoot } else { $temporaryRoot })
Assert-ChildPath $sourceStaging $temporaryRoot
$requiredInputs = @(
    (Join-Path $WorkspaceRoot 'LICENSE'),
    (Join-Path $WorkspaceRoot 'licenses\Apache-2.0.txt'),
    (Join-Path $WorkspaceRoot 'licenses\ctranslate2-MIT.txt'),
    (Join-Path $WorkspaceRoot 'artifacts\models\rapidocr\ch_PP-OCRv5_mobile_det.onnx'),
    (Join-Path $WorkspaceRoot 'artifacts\models\rapidocr\korean_PP-OCRv5_rec_mobile.onnx'),
    (Join-Path $WorkspaceRoot 'artifacts\models\rapidocr\ppocrv5_korean_dict.txt'),
    $IsccPath
)
if ($includeLocalModel) {
    $requiredInputs += @(
        (Join-Path $WorkspaceRoot 'licenses\llama.cpp-MIT.txt'),
        $localModelSource,
        (Join-Path $WorkspaceRoot 'artifacts\tools\llama-b8816-bin-win-vulkan-x64\llama-cli.exe')
    )
}
foreach ($required in $requiredInputs) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) { throw "Required installer input is missing: $required" }
}

function Get-InstallerOwnedCleanupPaths(
    [string] $Staging,
    [string] $SourceStaging,
    [bool] $ReusePreparedStaging,
    [bool] $KeepStaging
) {
    if ($KeepStaging) { return @() }
    if ($ReusePreparedStaging) { return @($SourceStaging) }
    return @($Staging, $SourceStaging)
}

function Assert-InstallerStagingFlavor(
    [string] $Staging,
    [string] $Flavor,
    [string] $WorkspaceRoot,
    [string] $ApprovedLocalModelSha256
) {
    if ($Flavor -eq 'Lite') {
        foreach ($relative in @('models\local-lmt', 'tools\llama', 'machine-translation\gpu', 'licenses\llama.cpp-MIT.txt')) {
            if (Test-Path -LiteralPath (Join-Path $Staging $relative)) {
                throw "Lite installer staging contains Full-only payload path: $relative"
            }
        }
        foreach ($file in Get-ChildItem -LiteralPath $Staging -Recurse -File) {
            if ($file.Extension -ieq '.gguf' -or $file.Name -ieq 'llama-cli.exe' -or
                $file.Name -match '^(?i:(ggml|llama|libllama|cublas|cudart|cudnn|nvrtc|nvcuda).*\.dll)$') {
                $relative = [IO.Path]::GetRelativePath($Staging, $file.FullName)
                throw "Lite installer staging contains Full-only payload file: $relative"
            }
        }
        return
    }

    if ($Flavor -ne 'Full') { throw "Unsupported installer flavor: $Flavor" }
    $stagedModel = Join-Path $Staging 'models\local-lmt\LMT-60-0.6B.Q4_K_M.gguf'
    if (-not (Test-Path -LiteralPath $stagedModel -PathType Leaf)) {
        throw "Full installer staging is missing the approved Local LMT model: $stagedModel"
    }
    $stagedHash = (Get-FileHash -LiteralPath $stagedModel -Algorithm SHA256).Hash
    if (-not $stagedHash.Equals($ApprovedLocalModelSha256, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Full installer staging has an unexpected Local LMT model hash: $stagedModel"
    }

    $llamaSource = Join-Path $WorkspaceRoot 'artifacts\tools\llama-b8816-bin-win-vulkan-x64'
    $llamaTarget = Join-Path $Staging 'tools\llama'
    $sourceDlls = @(Get-ChildItem -LiteralPath $llamaSource -Filter '*.dll' -File)
    if ($sourceDlls.Count -eq 0) {
        throw "Full installer input has no llama runtime DLLs: $llamaSource"
    }
    foreach ($runtimeName in @('llama-cli.exe') + @($sourceDlls | ForEach-Object Name)) {
        $source = Join-Path $llamaSource $runtimeName
        $staged = Join-Path $llamaTarget $runtimeName
        if (-not (Test-Path -LiteralPath $staged -PathType Leaf)) {
            throw "Full installer staging is missing llama runtime file: tools\llama\$runtimeName"
        }
        if ((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ne
            (Get-FileHash -LiteralPath $staged -Algorithm SHA256).Hash) {
            throw "Full installer staging has an unexpected llama runtime file: tools\llama\$runtimeName"
        }
    }
    foreach ($licenseName in @('Apache-2.0.txt', 'llama.cpp-MIT.txt')) {
        $source = Join-Path (Join-Path $WorkspaceRoot 'licenses') $licenseName
        $staged = Join-Path (Join-Path $Staging 'licenses') $licenseName
        if (-not (Test-Path -LiteralPath $staged -PathType Leaf)) {
            throw "Full installer staging is missing local model license: licenses\$licenseName"
        }
        if ((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ne
            (Get-FileHash -LiteralPath $staged -Algorithm SHA256).Hash) {
            throw "Full installer staging has an outdated local model license: licenses\$licenseName"
        }
    }
}
if ($includeLocalModel -and -not $reusePreparedStaging) {
    $sourceHash = (Get-FileHash -LiteralPath $localModelSource -Algorithm SHA256).Hash
    if (-not $sourceHash.Equals($approvedLocalModelSha256, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Local LMT model hash does not match the approved 0.6B artifact: $localModelSource"
    }
}

try {
    New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
    if (-not $reusePreparedStaging) {
        New-Item -ItemType Directory -Path $staging -Force | Out-Null
        & dotnet restore (Join-Path $WorkspaceRoot 'src\ClipRay.App\ClipRay.App.csproj') `
            -r win-x64 --locked-mode | Out-Host
        if ($LASTEXITCODE -ne 0) { throw "ClipRay win-x64 restore failed with exit code $LASTEXITCODE." }
        & dotnet publish (Join-Path $WorkspaceRoot 'src\ClipRay.App\ClipRay.App.csproj') `
            -c Release -r win-x64 --self-contained true --no-restore `
            -p:DebugType=None -p:DebugSymbols=false "-p:InformationalVersion=$informationalVersion" -p:IncludeSourceRevisionInInformationalVersion=false -p:ClipRayIncludeCudaRuntime=$($includeLocalModel.ToString().ToLowerInvariant()) -o $staging | Out-Host
        if ($LASTEXITCODE -ne 0) { throw "ClipRay publish failed with exit code $LASTEXITCODE." }

        New-Item -ItemType Directory -Path (Join-Path $staging 'licenses') -Force | Out-Null
        if ($includeLocalModel) {
            $modelTarget = Join-Path $staging 'models\local-lmt'
            $llamaTarget = Join-Path $staging 'tools\llama'
            New-Item -ItemType Directory -Path $modelTarget, $llamaTarget -Force | Out-Null
            Copy-Item -LiteralPath $localModelSource -Destination $modelTarget
            Copy-Item -LiteralPath (Join-Path $WorkspaceRoot 'artifacts\tools\llama-b8816-bin-win-vulkan-x64\llama-cli.exe') -Destination $llamaTarget
            Get-ChildItem -LiteralPath (Join-Path $WorkspaceRoot 'artifacts\tools\llama-b8816-bin-win-vulkan-x64') -Filter '*.dll' -File |
                Copy-Item -Destination $llamaTarget
            Copy-Item -LiteralPath (Join-Path $WorkspaceRoot 'licenses\llama.cpp-MIT.txt') -Destination (Join-Path $staging 'licenses')
        }
        Copy-Item -LiteralPath (Join-Path $WorkspaceRoot 'LICENSE') -Destination $staging
        Copy-Item -LiteralPath (Join-Path $WorkspaceRoot 'TRADEMARKS.md') -Destination $staging
        Copy-Item -LiteralPath (Join-Path $WorkspaceRoot 'THIRD_PARTY_NOTICES.md') -Destination $staging
        Copy-Item -LiteralPath (Join-Path $WorkspaceRoot 'SOURCE.md') -Destination $staging
        Copy-Item -LiteralPath (Join-Path $WorkspaceRoot 'licenses\Apache-2.0.txt') -Destination (Join-Path $staging 'licenses')
        Copy-Item -LiteralPath (Join-Path $WorkspaceRoot 'licenses\ctranslate2-MIT.txt') -Destination (Join-Path $staging 'licenses')
        Copy-Item -LiteralPath (Join-Path $WorkspaceRoot 'licenses\small100-MIT.txt') -Destination (Join-Path $staging 'licenses')
        Get-ChildItem -LiteralPath (Join-Path $WorkspaceRoot 'licenses') -Force | Where-Object {
            $_.Name -ne 'llama.cpp-MIT.txt' -or $includeLocalModel
        } | Copy-Item -Destination (Join-Path $staging 'licenses') -Recurse -Force
    }

    foreach ($relative in @('LICENSE', 'SOURCE.md', 'THIRD_PARTY_NOTICES.md', 'licenses\ctranslate2-MIT.txt', 'licenses\inno-chinese-MIT.txt')) {
        $sourceNotice = Join-Path $WorkspaceRoot $relative
        $stagedNotice = Join-Path $staging $relative
        if (-not (Test-Path -LiteralPath $stagedNotice -PathType Leaf)) {
            throw "Installer staging is missing a third-party notice: $stagedNotice"
        }
        $sourceNoticeHash = (Get-FileHash -LiteralPath $sourceNotice -Algorithm SHA256).Hash
        $stagedNoticeHash = (Get-FileHash -LiteralPath $stagedNotice -Algorithm SHA256).Hash
        if (-not $sourceNoticeHash.Equals($stagedNoticeHash, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Installer staging has an outdated third-party notice: $stagedNotice"
        }
    }

    $payloadDeps = Get-Content -LiteralPath (Join-Path $staging 'ClipRay.App.deps.json') -Raw | ConvertFrom-Json
    foreach ($pack in @($payloadDeps.libraries.PSObject.Properties.Name | Where-Object { $_.StartsWith('runtimepack.') })) {
        if ($pack -notmatch '^runtimepack\.([^/]+)/([^/]+)$') { throw "Invalid runtime pack metadata: $pack" }
        $noticeRoot = Join-Path $staging ('licenses\nuget\' + $Matches[1] + '-' + $Matches[2])
        if (-not (Test-Path -LiteralPath $noticeRoot -PathType Container) -or
            @(Get-ChildItem -LiteralPath $noticeRoot -File | Where-Object { $_.Name -match '^LICENSE(?:\.|$)' }).Count -eq 0) {
            throw "Missing notices for actual bundled runtime: $pack. Run Collect-PackageNotices.ps1 with -PublishedPayloadRoot."
        }
        if ($pack.StartsWith('runtimepack.Microsoft.NETCore.App.') -and
            -not (Test-Path -LiteralPath (Join-Path $noticeRoot 'THIRD-PARTY-NOTICES.TXT'))) {
            throw "Missing third-party notices for actual bundled runtime: $pack"
        }
    }

    Assert-InstallerStagingFlavor $staging $Flavor $WorkspaceRoot $approvedLocalModelSha256

    $requiredTestNamedRuntimeFiles = @(
        'machine-translation/runtime/Lib/site-packages/numpy/_pytesttester.py'
    )
    $forbidden = Get-ChildItem -LiteralPath $staging -Recurse -File | Where-Object {
        $relativePath = $_.FullName.Substring($staging.TrimEnd([IO.Path]::DirectorySeparatorChar).Length + 1).Replace('\', '/')
        ($_.Extension -in @('.pdb', '.cs', '.csproj', '.sln', '.ps1') -or $_.Name -match '(?i)(test|probe|preview)') `
            -and $relativePath -notin $requiredTestNamedRuntimeFiles
    }
    if ($forbidden) { throw "Release staging contains forbidden development files: $($forbidden.FullName -join ', ')" }

    if (-not $reusePreparedStaging) {
        Set-Content -LiteralPath (Join-Path $staging 'install-exit-protocol.txt') -Value 'ClipRay.InstallExit.1' -Encoding ascii
        Write-ReleaseIdentity $staging $sourceCommit $informationalVersion $Flavor
    }
    Assert-ReleaseIdentity $staging $sourceCommit $informationalVersion $Flavor
    Assert-SourceArchiveRevision $sourceArchive $appVersion $sourceCommit

    & $IsccPath "/DStagingDir=$staging" "/DOutputDir=$OutputDirectory" "/DSetupFlavor=$Flavor" $installerDefinition | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup compilation failed with exit code $LASTEXITCODE." }

    $sourceArchive = Join-Path $OutputDirectory "ClipRay-$appVersion-source.zip"
    if (Test-Path -LiteralPath $sourceArchive) {
        $existingSource = [IO.Compression.ZipFile]::OpenRead($sourceArchive)
        try {
            $revisionEntry = $existingSource.GetEntry("ClipRay-$appVersion/SOURCE-REVISION.txt")
            if ($null -eq $revisionEntry) { throw 'Existing source archive has no revision record.' }
            $reader = [IO.StreamReader]::new($revisionEntry.Open())
            try { $existingCommit = $reader.ReadToEnd().Trim() } finally { $reader.Dispose() }
            if ($existingCommit -ne $sourceCommit) { throw 'Existing source archive belongs to another commit; use a new output directory.' }
        } finally { $existingSource.Dispose() }
    } else {
        & (Join-Path $WorkspaceRoot 'scripts\Export-PublicSource.ps1') -WorkspaceRoot $WorkspaceRoot -Revision $sourceCommit -OutputPath $sourceArchive -Version $appVersion | Out-Host
    }

    Get-ChildItem -LiteralPath $OutputDirectory -File | Where-Object { $_.Name -like 'ClipRay-*' } |
        Select-Object Name, Length, @{Name='Sha256'; Expression={(Get-FileHash -Algorithm SHA256 -LiteralPath $_.FullName).Hash}}
}
finally {
    foreach ($path in @(Get-InstallerOwnedCleanupPaths $staging $sourceStaging $reusePreparedStaging ([bool]$KeepStaging))) {
        Assert-ChildPath $path $temporaryRoot
        if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Recurse -Force }
    }
}

exit 0
