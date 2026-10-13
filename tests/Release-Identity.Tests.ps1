$ErrorActionPreference='Stop'
$workspace=Split-Path -Parent $PSScriptRoot
# Load only function declarations, never a publisher, installer, cleanup, or patch script body.
$helper=Join-Path $workspace 'scripts\Release-Identity.ps1'
$tokens=$null;$parseErrors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile($helper,[ref]$tokens,[ref]$parseErrors)
if($parseErrors.Count){throw $parseErrors[0]}
foreach($function in $ast.FindAll({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst]},$false)) {
    Invoke-Expression $function.Extent.Text
}
$realVersionReader=${function:Get-ReleaseInformationalVersion}
$script:fixtureVersion='1.0.6+'+('a'*40)
function Get-ReleaseInformationalVersion([string] $Root){return $script:fixtureVersion}
$commit='a'*40
$testRoot=Join-Path ([IO.Path]::GetTempPath()) ('clipray-release-identity-'+[Guid]::NewGuid().ToString('N'))
function New-Payload([string] $Name){
    $root=Join-Path $testRoot $Name
    New-Item -ItemType Directory -Path (Join-Path $root 'licenses') -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $root 'ClipRay.App.dll') -Value 'fake assembly, not executable' -Encoding ascii
    Set-Content -LiteralPath (Join-Path $root 'ClipRay.App.exe') -Value 'fake app, not executable' -Encoding ascii
    Set-Content -LiteralPath (Join-Path $root 'licenses\notice.txt') -Value 'fake notice' -Encoding ascii
    Set-Content -LiteralPath (Join-Path $root 'install-exit-protocol.txt') -Value 'ClipRay.InstallExit.1' -Encoding ascii
    Write-ReleaseIdentity $root $commit $script:fixtureVersion Lite
    return $root
}
function Must-Reject([scriptblock] $Action,[string] $Name){
    $failed=$false;try{& $Action}catch{$failed=$true}
    if(-not $failed){throw "Expected rejection: $Name"}
    Write-Output "PASS identity/$Name"
}
try {
    $valid=New-Payload 'valid'
    Assert-ReleaseIdentity $valid $commit $script:fixtureVersion Lite
    $metadata=Get-Content -LiteralPath (Join-Path $valid 'release-identity.json') -Raw | ConvertFrom-Json
    if(@($metadata.Payload | Where-Object {$_.Path -eq 'licenses/notice.txt' -and $_.InstalledPath -eq 'licenses/notice.txt'}).Count -ne 1 -or
       @($metadata.Payload | Where-Object {$_.Path -eq 'ClipRay.App.dll' -and $_.InstalledPath -eq 'app/ClipRay.App.dll'}).Count -ne 1){throw 'Installed licenses/app mapping failed.'}
    Write-Output 'PASS identity/valid-full-inventory-and-installed-mapping'
    foreach($field in @('commit','version','flavor','rid','self-contained')) {
        Must-Reject {
            Assert-ReleaseIdentity $valid $(if($field -eq 'commit'){'b'*40}else{$commit}) `
                $(if($field -eq 'version'){'1.0.2'}else{$script:fixtureVersion}) `
                $(if($field -eq 'flavor'){'Full'}else{'Lite'}) $(if($field -eq 'rid'){'win-arm64'}else{'win-x64'}) ($field -ne 'self-contained')
        } $field
    }
    foreach($change in @('missing-identity','missing-file','extra-file','tamper','manifest','duplicate','case-collision','traversal','mapped-path','state-schema')) {
        $root=New-Payload $change;$identityPath=Join-Path $root 'release-identity.json'
        $record=Get-Content -LiteralPath $identityPath -Raw | ConvertFrom-Json
        switch($change){
            'missing-identity'{Remove-Item -LiteralPath $identityPath}
            'missing-file'{Remove-Item -LiteralPath (Join-Path $root 'ClipRay.App.dll')}
            'extra-file'{Set-Content -LiteralPath (Join-Path $root 'extra.dll') -Value 'extra'}
            'tamper'{Add-Content -LiteralPath (Join-Path $root 'ClipRay.App.dll') -Value 'tampered'}
            'manifest'{Set-Content -LiteralPath (Join-Path $root 'release-manifest.json') -Value '[]'}
            'duplicate'{$record.Payload+=@($record.Payload[0]);$record|ConvertTo-Json -Depth 6|Set-Content -LiteralPath $identityPath}
            'case-collision'{$duplicate=$record.Payload[0]|Select-Object *;$duplicate.Path=$duplicate.Path.ToUpperInvariant();$record.Payload+=@($duplicate);$record|ConvertTo-Json -Depth 6|Set-Content -LiteralPath $identityPath}
            'traversal'{$record.Payload[0].Path='../outside';$record|ConvertTo-Json -Depth 6|Set-Content -LiteralPath $identityPath}
            'mapped-path'{$record.Payload[0].InstalledPath='app/licenses/notice.txt';$record|ConvertTo-Json -Depth 6|Set-Content -LiteralPath $identityPath}
            'state-schema'{$record.Schema=0;$record|ConvertTo-Json -Depth 6|Set-Content -LiteralPath $identityPath}
        }
        $before=Get-ChildItem -LiteralPath $root -File -Recurse|ForEach-Object{[pscustomobject]@{Path=$_.FullName;Hash=(Get-FileHash -LiteralPath $_.FullName).Hash}}
        Must-Reject {Assert-ReleaseIdentity $root $commit $script:fixtureVersion Lite} $change
        foreach($item in $before){if((Get-FileHash -LiteralPath $item.Path).Hash -ne $item.Hash){throw 'Rejected staging was rewritten.'}}
    }
    $full=Join-Path $testRoot 'full';New-Item -ItemType Directory -Path $full|Out-Null
    Copy-Item -Path (Join-Path $valid '*') -Destination $full -Recurse
    Remove-Item -LiteralPath (Join-Path $full 'release-identity.json'),(Join-Path $full 'release-manifest.json')
    Write-ReleaseIdentity $full $commit $script:fixtureVersion Full
    Assert-ReleaseIdentity $full $commit $script:fixtureVersion Full
    Write-Output 'PASS identity/explicit-full'
    function Check-ReparsePolicy([string] $Shadow) {
        function Get-Item([string] $LiteralPath,[switch] $Force) {
            if($LiteralPath -eq $Shadow){return [pscustomobject]@{Attributes=[IO.FileAttributes]::Directory -bor [IO.FileAttributes]::ReparsePoint}}
            Microsoft.PowerShell.Management\Get-Item -LiteralPath $LiteralPath -Force:$Force
        }
        Must-Reject {Assert-ReleaseIdentity $valid $commit $script:fixtureVersion Lite} 'reparse-root-or-ancestor-fake-metadata'
    }
    Check-ReparsePolicy $valid
    Check-ReparsePolicy $testRoot
    # Real static PE reader checked separately; fake fixtures do not establish actual publishing.
    $actualVersion=& $realVersionReader (Join-Path $workspace 'src\ClipRay.App\bin\Release\net10.0-windows10.0.26100.0\win-x64')
    if(-not $actualVersion.StartsWith('1.0.6+')){throw 'Static PE informational-version reader failed.'}
    Write-Output "PASS identity/static-PE-reader ($actualVersion)"
    $zipPath=Join-Path $testRoot 'old-source.zip';$zip=[IO.Compression.ZipFile]::Open($zipPath,[IO.Compression.ZipArchiveMode]::Create)
    try{$entry=$zip.CreateEntry('ClipRay-1.0.6/SOURCE-REVISION.txt');$writer=[IO.StreamWriter]::new($entry.Open());try{$writer.Write('b'*40)}finally{$writer.Dispose()}}finally{$zip.Dispose()}
    $zipHash=(Get-FileHash -LiteralPath $zipPath).Hash
    Must-Reject {Assert-SourceArchiveRevision $zipPath '1.0.6' $commit} 'source-revision-before-overwrite'
    if((Get-FileHash -LiteralPath $zipPath).Hash -ne $zipHash){throw 'Source guard changed the archive.'}
    # AST/source oracles prove ordering, not executions of these entry points.
    foreach($relative in @('scripts\Build-Installer.ps1','scripts\Publish-Local.ps1','scripts\Build-RecordingDiagnosticPatch.ps1','scripts\Export-PublicSource.ps1','tests\Publish-Local.Tests.ps1')){
        $errors=$null;$unused=$null;[void][Management.Automation.Language.Parser]::ParseFile((Join-Path $workspace $relative),[ref]$unused,[ref]$errors)
        if($errors.Count){throw "$relative parse failure: $($errors[0])"}
    }
    $build=Get-Content -LiteralPath (Join-Path $workspace 'scripts\Build-Installer.ps1') -Raw
    if($build.IndexOf('Assert-ReleaseIdentity $staging') -gt $build.IndexOf('try {') -or $build.IndexOf('Assert-SourceArchiveRevision') -gt $build.IndexOf('& $IsccPath')){throw 'Staging/source preflight is too late.'}
    $patchAst=[Management.Automation.Language.Parser]::ParseFile((Join-Path $workspace 'scripts\Build-RecordingDiagnosticPatch.ps1'),[ref]$tokens,[ref]$parseErrors)
    if($patchAst.EndBlock.Statements[1] -isnot [Management.Automation.Language.ThrowStatementAst]){throw 'Old patch is not rejected before work.'}
    $inno=Get-Content -LiteralPath (Join-Path $workspace 'installer\ClipRay.iss') -Raw
    if($build -notmatch '\$Flavor = ''Lite''' -or $inno -notmatch '#define SetupFlavor "Lite"' -or $inno -match '(?i)taskkill|/IM|TerminateProcess'){throw 'Installer defaults/exit safety regressed.'}
    Write-Output 'PASS identity/AST-entry-order-and-lite-defaults'
    Write-Output 'PASS Release-Identity isolated fake payload and source-order checks'
} finally {
    if(Test-Path -LiteralPath $testRoot){
        $prefix=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')+'\'
        if(-not [IO.Path]::GetFullPath($testRoot).StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase)){throw 'Unsafe fixture cleanup.'}
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}
