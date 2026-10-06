[CmdletBinding()]
param(
    [string] $WorkspaceRoot = (Split-Path -Parent $PSScriptRoot),
    [Parameter(Mandatory)] [string] $PackageCacheRoot,
    [string] $PublishedPayloadRoot
)

$ErrorActionPreference = 'Stop'
$WorkspaceRoot = [IO.Path]::GetFullPath($WorkspaceRoot)
$PackageCacheRoot = [IO.Path]::GetFullPath($PackageCacheRoot)
$lock = Get-Content -LiteralPath (Join-Path $WorkspaceRoot 'src\ClipRay.App\packages.lock.json') -Raw | ConvertFrom-Json
$packages = @($lock.dependencies.PSObject.Properties | ForEach-Object {
    $_.Value.PSObject.Properties | Where-Object { $_.Value.type -ne 'Project' } | ForEach-Object {
        [pscustomobject]@{ Name = $_.Name; Version = $_.Value.resolved }
    }
} | Sort-Object Name, Version -Unique)
if (-not [string]::IsNullOrWhiteSpace($PublishedPayloadRoot)) {
    $deps = Get-Content -LiteralPath (Join-Path $PublishedPayloadRoot 'ClipRay.App.deps.json') -Raw | ConvertFrom-Json
    $runtimePacks = @($deps.libraries.PSObject.Properties.Name | Where-Object { $_.StartsWith('runtimepack.') })
    if ($runtimePacks.Count -eq 0) { throw 'Published payload has no self-contained runtime pack metadata.' }
    foreach ($pack in $runtimePacks) {
        if ($pack -notmatch '^runtimepack\.([^/]+)/([^/]+)$') { throw "Invalid runtime pack metadata: $pack" }
        $packages += [pscustomobject]@{ Name = $Matches[1]; Version = $Matches[2] }
    }
}
$mit = Get-Content -LiteralPath (Join-Path $WorkspaceRoot 'licenses\ctranslate2-MIT.txt') -Raw
$mitPermission = $mit.Substring($mit.IndexOf('Permission is hereby granted'))
if (-not $mitPermission.StartsWith('Permission is hereby granted')) { throw 'MIT permission template missing.' }
$targetRoot = Join-Path $WorkspaceRoot 'licenses\nuget'
foreach ($package in $packages) {
    $packageRoot = Join-Path $PackageCacheRoot ($package.Name.ToLowerInvariant() + '\' + $package.Version)
    $nuspec = @(Get-ChildItem -LiteralPath $packageRoot -Filter '*.nuspec' -File)
    if ($nuspec.Count -ne 1) { throw "Missing or ambiguous cached package metadata: $($package.Name) $($package.Version)" }
    [xml]$metadata = Get-Content -LiteralPath $nuspec[0].FullName
    $license = $metadata.package.metadata.license
    $target = Join-Path $targetRoot ($package.Name + '-' + $package.Version)
    [void][IO.Directory]::CreateDirectory($target)
    Copy-Item -LiteralPath $nuspec[0].FullName -Destination $target
    $notices = @(Get-ChildItem -LiteralPath $packageRoot -File | Where-Object { $_.Name -match '(?i)license|notice|copying|copyright' })
    foreach ($notice in $notices) { Copy-Item -LiteralPath $notice.FullName -Destination $target }
    if ($license.type -eq 'file' -and -not (Test-Path -LiteralPath (Join-Path $target $license.InnerText))) {
        throw "Package license file was not collected: $($package.Name)"
    }
    if ($license.type -eq 'expression' -and $license.InnerText -eq 'MIT' -and $notices.Count -eq 0) {
        $copyright = [string]$metadata.package.metadata.copyright
        if ([string]::IsNullOrWhiteSpace($copyright)) { throw "MIT copyright metadata absent: $($package.Name)" }
        [IO.File]::WriteAllText((Join-Path $target 'LICENSE.txt'), "MIT License`n`n$copyright`n`n$mitPermission", [Text.UTF8Encoding]::new($false))
    } elseif ($license.type -eq 'expression' -and $license.InnerText -eq 'Apache-2.0' -and $notices.Count -eq 0) {
        Copy-Item -LiteralPath (Join-Path $WorkspaceRoot 'licenses\Apache-2.0.txt') -Destination (Join-Path $target 'LICENSE.txt')
    } elseif ($license.type -eq 'expression' -and $license.InnerText -notin @('MIT', 'Apache-2.0')) {
        throw "New license expression requires review: $($package.Name) $($license.InnerText)"
    }
    if (@(Get-ChildItem -LiteralPath $target -File | Where-Object { $_.Name -match '^LICENSE(?:\.|$)' }).Count -eq 0) {
        throw "Package has no collected license text: $($package.Name). Review its upstream license URL and retain the original document before packaging."
    }
    Write-Output "$($package.Name) $($package.Version): license metadata and notices collected"
}
