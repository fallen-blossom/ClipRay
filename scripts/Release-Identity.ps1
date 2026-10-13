# Pure helpers: callers own publishing/installation. The two root metadata files exclude their own hashes.
function Assert-ReleaseOrdinaryPath([string] $Path) {
    $full = [IO.Path]::GetFullPath($Path)
    if (-not [IO.Path]::IsPathFullyQualified($Path) -or $full.StartsWith('\\')) { throw 'Release paths must be absolute local paths.' }
    for ($part = $full; $part; $part = [IO.Path]::GetDirectoryName($part)) {
        if (Test-Path -LiteralPath $part) {
            if ((Get-Item -LiteralPath $part -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Release reparse path rejected: $part" }
        }
    }
}
function Get-ReleasePayload([string] $Root) {
    Assert-ReleaseOrdinaryPath $Root
    if (-not (Test-Path -LiteralPath $Root -PathType Container)) { throw 'Release root missing.' }
    $pending = [Collections.Generic.Stack[string]]::new(); $pending.Push($Root)
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $files = [Collections.Generic.List[object]]::new()
    while ($pending.Count -gt 0) {
        $dir = $pending.Pop()
        foreach ($item in Get-ChildItem -LiteralPath $dir -Force) {
            if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Release reparse entry rejected: $($item.FullName)" }
            $relative = [IO.Path]::GetRelativePath($Root,$item.FullName).Replace('\','/')
            if (-not $seen.Add($relative)) { throw "Release case collision: $relative" }
            if ($item.PSIsContainer) { $pending.Push($item.FullName); continue }
            if ($relative -cin @('release-manifest.json','release-identity.json')) { continue }
            if ($relative -iin @('release-manifest.json','release-identity.json')) { throw 'Metadata filename case collision.' }
            $files.Add([pscustomobject]@{ Path=$relative; InstalledPath=$(if($relative.StartsWith('licenses/',[StringComparison]::OrdinalIgnoreCase)){$relative}else{'app/'+$relative}); Size=$item.Length; Sha256=(Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash })
        }
    }
    return @($files | Sort-Object Path)
}
function Assert-ReleaseEntries($Entries) {
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach($entry in $Entries) {
        $path = [string]$entry.Path
        if (-not $path -or $path -match '^/|\\|:|(^|/)\.{1,2}(/|$)|[\x00-\x1f]|[ .](/|$)' -or $path.EndsWith('/') -or $path.Contains('//') -or
            $path -iin @('release-manifest.json','release-identity.json') -or -not $seen.Add($path) -or
            $entry.Size -isnot [ValueType] -or $entry.Size -lt 0 -or $entry.Sha256 -notmatch '^[0-9A-Fa-f]{64}$') { throw "Invalid/duplicate release entry: $path" }
        $mapped = if($path.StartsWith('licenses/',[StringComparison]::OrdinalIgnoreCase)){$path}else{'app/'+$path}
        if($entry.InstalledPath -cne $mapped) { throw "Invalid installed mapping: $path" }
    }
}
function Get-ReleaseInformationalVersion([string] $Root) {
    $assembly = Join-Path $Root 'ClipRay.App.dll'
    if (-not (Test-Path -LiteralPath $assembly -PathType Leaf)) { throw 'App assembly missing.' }
    return [Diagnostics.FileVersionInfo]::GetVersionInfo($assembly).ProductVersion
}
function Write-ReleaseIdentity([string] $Root,[string] $SourceCommit,[string] $AppInformationalVersion,[string] $Flavor,[string] $Rid='win-x64',[bool] $SelfContained=$true) {
    if($SourceCommit -notmatch '^[0-9a-f]{40}$' -or $Flavor -notin @('Lite','Full')) { throw 'Invalid release identity.' }
    if(Test-Path -LiteralPath (Join-Path $Root 'release-identity.json')) { throw 'Existing identity cannot be overwritten.' }
    if((Get-ReleaseInformationalVersion $Root) -cne $AppInformationalVersion) { throw 'Published AppInformationalVersion mismatch.' }
    $payload = @(Get-ReleasePayload $Root)
    Assert-ReleaseEntries $payload
    $manifest = @($payload | Select-Object Path,Size,Sha256)
    $manifestPath = Join-Path $Root 'release-manifest.json'
    ConvertTo-Json -InputObject $manifest -Depth 4 | Set-Content -LiteralPath $manifestPath -Encoding utf8
    $identity = [pscustomobject]@{ Schema=1; SourceCommit=$SourceCommit; AppInformationalVersion=$AppInformationalVersion; Flavor=$Flavor; Rid=$Rid; SelfContained=$SelfContained; InstallExitProtocol=1; MetadataHashExclusions=@('release-manifest.json','release-identity.json'); ManifestSha256=(Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash; Payload=$payload }
    ConvertTo-Json -InputObject $identity -Depth 6 | Set-Content -LiteralPath (Join-Path $Root 'release-identity.json') -Encoding utf8
}
function Assert-ReleaseIdentity([string] $Root,[string] $SourceCommit,[string] $AppInformationalVersion,[string] $Flavor,[string] $Rid='win-x64',[bool] $SelfContained=$true) {
    Assert-ReleaseOrdinaryPath $Root
    foreach($name in @('release-identity.json','release-manifest.json')) {
        $path=Join-Path $Root $name; Assert-ReleaseOrdinaryPath $path
        if(-not(Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing release metadata: $name" }
    }
    $identity=Get-Content -LiteralPath (Join-Path $Root 'release-identity.json') -Raw | ConvertFrom-Json
    if($identity.Schema -ne 1 -or $identity.SourceCommit -cne $SourceCommit -or $identity.AppInformationalVersion -cne $AppInformationalVersion -or
        $identity.Flavor -cne $Flavor -or $identity.Rid -cne $Rid -or $identity.SelfContained -isnot [bool] -or $identity.SelfContained -ne $SelfContained -or
        $identity.InstallExitProtocol -ne 1 -or ($identity.MetadataHashExclusions -join '|') -cne 'release-manifest.json|release-identity.json') { throw 'Prepared staging release identity mismatch.' }
    if((Get-ReleaseInformationalVersion $Root) -cne $AppInformationalVersion) { throw 'Prepared AppInformationalVersion mismatch.' }
    if($identity.ManifestSha256 -cne (Get-FileHash -LiteralPath (Join-Path $Root 'release-manifest.json') -Algorithm SHA256).Hash) { throw 'Flat manifest changed.' }
    Assert-ReleaseEntries @($identity.Payload)
    $actual=@(Get-ReleasePayload $Root)
    if($actual.Count -eq 0 -or $actual.Count -ne @($identity.Payload).Count) { throw 'Payload extra/missing files.' }
    $recorded=@($identity.Payload | Sort-Object Path)
    for($i=0;$i -lt $actual.Count;$i++) {
        foreach($field in @('Path','InstalledPath','Size','Sha256')) { if($actual[$i].$field -cne $recorded[$i].$field) { throw "Payload changed: $($actual[$i].Path)/$field" } }
    }
    $manifest=@(Get-Content -LiteralPath (Join-Path $Root 'release-manifest.json') -Raw | ConvertFrom-Json)
    if($manifest.Count -ne $actual.Count) { throw 'Flat manifest is not a full payload array.' }
    for($i=0;$i -lt $actual.Count;$i++) {
        foreach($field in @('Path','Size','Sha256')) { if($manifest[$i].$field -cne $actual[$i].$field) { throw 'Flat manifest identity mismatch.' } }
    }
    if((Get-Content -LiteralPath (Join-Path $Root 'install-exit-protocol.txt') -Raw).Trim() -cne 'ClipRay.InstallExit.1') { throw 'Install exit protocol marker missing.' }
}
function Assert-SourceArchiveRevision([string] $Archive,[string] $Version,[string] $SourceCommit) {
    if(-not(Test-Path -LiteralPath $Archive)) { return }
    Assert-ReleaseOrdinaryPath $Archive
    $zip=[IO.Compression.ZipFile]::OpenRead($Archive)
    try {
        $name="ClipRay-$Version/SOURCE-REVISION.txt"
        $entries=@($zip.Entries | Where-Object FullName -CEQ $name)
        if($entries.Count -ne 1) { throw 'Existing source archive lacks one revision record.' }
        $reader=[IO.StreamReader]::new($entries[0].Open())
        try { if($reader.ReadToEnd().Trim() -cne $SourceCommit) { throw 'Existing source archive belongs to another commit; use a new output directory.' } } finally { $reader.Dispose() }
    } finally { $zip.Dispose() }
}
