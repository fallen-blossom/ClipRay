[CmdletBinding()]
param(
    [string] $WorkspaceRoot = (Split-Path -Parent $PSScriptRoot),
    [string] $Revision = 'HEAD',
    [Parameter(Mandatory)] [string] $OutputPath,
    [string] $Version = '1.0.6'
)

$ErrorActionPreference = 'Stop'
$WorkspaceRoot = [IO.Path]::GetFullPath($WorkspaceRoot)
$OutputPath = [IO.Path]::GetFullPath($OutputPath)
if ($Version -notmatch '^\d+\.\d+\.\d+(?:[-.][A-Za-z0-9]+)*$') { throw 'Invalid source package version.' }
if (Test-Path -LiteralPath $OutputPath) { throw 'Source output already exists; choose a new output path.' }

$commit = & git -C $WorkspaceRoot rev-parse --verify "$Revision^{commit}"
if ($LASTEXITCODE -ne 0 -or $commit -notmatch '^[0-9a-f]{40}$') { throw 'Source revision is not a Git commit.' }
$selection = & git -C $WorkspaceRoot show "${commit}:scripts/public-source-paths.txt"
if ($LASTEXITCODE -ne 0) { throw 'The selected commit has no public source selection.' }
$paths = @($selection | Where-Object { $_.Trim() -ne '' -and -not $_.TrimStart().StartsWith('#') } | ForEach-Object { $_.Trim() })
if ($paths.Count -eq 0 -or @($paths | Select-Object -Unique).Count -ne $paths.Count) {
    throw 'Public source selection is empty or has duplicates.'
}
foreach ($path in $paths) {
    if ($path -match '(^/|\\|:|(^|/)\.\.(/|$)|[\*\?\[\]])') { throw "Invalid source selection path: $path" }
    $treeEntry = & git -C $WorkspaceRoot ls-tree $commit -- $path
    if ($LASTEXITCODE -ne 0 -or -not $treeEntry) { throw "Selected source path is absent from commit: $path" }
}
$selectedFiles = @(& git -c core.quotepath=false -C $WorkspaceRoot ls-tree -r --name-only $commit -- @paths)
if ($LASTEXITCODE -ne 0 -or $selectedFiles.Count -eq 0) { throw 'Could not expand the exact public source selection.' }
foreach($relative in $selectedFiles) {
    if ($relative -match '(^|/)(\.git|__pycache__|bin|obj)(/|$)' -or
        $relative -match '^(screenshots|diagnostics)(/|$)' -or $relative -match '^docs/(reviews|testing|superpowers)/' -or
        $relative -match '(^|/)\.env($|\.)|\.(pyc|log)$' -or $relative -match '^scripts/(.*Diagnostic.*|ClipboardOwnerMonitor|analyze_.*)') {
        throw "Forbidden file selected for public source archive: $relative"
    }
}

[void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($OutputPath))
& git -C $WorkspaceRoot archive --format=zip "--prefix=ClipRay-$Version/" "--output=$OutputPath" $commit -- @paths
if ($LASTEXITCODE -ne 0) { throw 'Git source archive failed.' }
$archive = [IO.Compression.ZipFile]::Open($OutputPath, [IO.Compression.ZipArchiveMode]::Update)
try {
    $entry = $archive.CreateEntry("ClipRay-$Version/SOURCE-REVISION.txt")
    $writer = [IO.StreamWriter]::new($entry.Open(), [Text.UTF8Encoding]::new($false))
    try { $writer.WriteLine($commit) } finally { $writer.Dispose() }
    foreach ($item in $archive.Entries) {
        $relative = $item.FullName.Substring("ClipRay-$Version/".Length)
        if ($relative -match '(^|/)(\.git|__pycache__|bin|obj)(/|$)' -or
            $relative -match '^(screenshots|diagnostics)(/|$)' -or
            $relative -match '^docs/(reviews|testing|superpowers)/' -or
            $relative -match '(^|/)\.env($|\.)|\.(pyc|log)$') {
            throw "Forbidden file in public source archive: $relative"
        }
    }
} finally { $archive.Dispose() }
Write-Output ([pscustomobject]@{ Commit = $commit; Path = $OutputPath; Sha256 = (Get-FileHash -LiteralPath $OutputPath -Algorithm SHA256).Hash })
