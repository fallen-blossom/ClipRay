$ErrorActionPreference = 'Stop'

$workspace = Split-Path -Parent $PSScriptRoot
$scriptPath = Join-Path $workspace 'scripts\Publish-Local.ps1'
$testRoot = Join-Path $workspace ('artifacts\shortcut-test-' + [Guid]::NewGuid().ToString('N'))
$desktop = Join-Path $testRoot 'Desktop'
$publish = Join-Path $testRoot 'app'
$displayName = [string]::Concat('ClipRay ', [char]0x622A, [char]0x5149)
$description = [string]::Concat(
    $displayName,
    ' - ',
    [char]0x672C,
    [char]0x5730,
    [char]0x622A,
    [char]0x56FE,
    [char]0x5DE5,
    [char]0x5177)

try {
    New-Item -ItemType Directory -Path $desktop, (Join-Path $publish 'Assets') -Force | Out-Null
    New-Item -ItemType File -Path (Join-Path $publish 'ClipRay.App.exe') -Force | Out-Null
    New-Item -ItemType File -Path (Join-Path $publish 'Assets\ClipRay.ico') -Force | Out-Null

    $shortcutPath = & $scriptPath -SkipPublish -DesktopDirectory $desktop -PublishDirectory $publish
    $shortcut = (New-Object -ComObject WScript.Shell).CreateShortcut($shortcutPath)

    if ($shortcut.TargetPath -ne (Join-Path $publish 'ClipRay.App.exe')) {
        throw "Unexpected TargetPath: $($shortcut.TargetPath)"
    }
    if ($shortcut.WorkingDirectory -ne $publish) {
        throw "Unexpected WorkingDirectory: $($shortcut.WorkingDirectory)"
    }
    if ($shortcut.IconLocation -notlike "$(Join-Path $publish 'Assets\ClipRay.ico'),*") {
        throw "Unexpected IconLocation: $($shortcut.IconLocation)"
    }
    if ([IO.Path]::GetFileName($shortcutPath) -ne ($displayName + '.lnk')) {
        throw "Unexpected Unicode shortcut name: $shortcutPath"
    }
    if ($shortcut.Description -ne $description) {
        throw "Unexpected Unicode description: $($shortcut.Description)"
    }

    Set-Content -LiteralPath (Join-Path $desktop ($displayName + '.lnk')) -Encoding Ascii -Value 'not a shortcut'

    $secondShortcutPath = & $scriptPath -SkipPublish -DesktopDirectory $desktop -PublishDirectory $publish
    if ([IO.Path]::GetFileName($secondShortcutPath) -ne ($displayName + ' (2).lnk')) {
        throw "Expected numbered shortcut for corrupt existing link, got: $secondShortcutPath"
    }

    $otherDesktop = Join-Path $testRoot 'OtherDesktop'
    New-Item -ItemType Directory -Path $otherDesktop -Force | Out-Null
    $otherTarget = Join-Path $testRoot 'Other.exe'
    New-Item -ItemType File -Path $otherTarget -Force | Out-Null
    $occupied = (New-Object -ComObject WScript.Shell).CreateShortcut((Join-Path $otherDesktop ($displayName + '.lnk')))
    $occupied.TargetPath = $otherTarget
    $occupied.Save()

    $unrelatedShortcutPath = & $scriptPath -SkipPublish -DesktopDirectory $otherDesktop -PublishDirectory $publish
    if ([IO.Path]::GetFileName($unrelatedShortcutPath) -ne ($displayName + ' (2).lnk')) {
        throw "Expected numbered shortcut for unrelated target, got: $unrelatedShortcutPath"
    }

    Write-Output 'PASS Publish-Local shortcut properties and collision safety'
}
finally {
    if (Test-Path -LiteralPath $testRoot) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}
