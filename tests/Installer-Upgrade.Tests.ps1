[CmdletBinding()]
param([string] $IsccPath = 'E:\Tools\InnoSetup\6\ISCC.exe')
$ErrorActionPreference = 'Stop'
$workspace = Split-Path -Parent $PSScriptRoot
$id = [Guid]::NewGuid().ToString().ToUpperInvariant()
$root = Join-Path ([IO.Path]::GetTempPath()) ('clipray-upgrade-fixture-' + $id)
$payload = Join-Path $root 'payload'
$installed = Join-Path $root 'installed'
$uninstallKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{' + $id + '}_is1'
$fixtureKey = 'Software\ClipRayInstallerFixture\' + $id
$mutexName = 'Local\ClipRayUpgrade-' + $id
[IO.Directory]::CreateDirectory($payload) | Out-Null
[IO.Directory]::CreateDirectory((Join-Path $payload 'Assets')) | Out-Null
[IO.Directory]::CreateDirectory((Join-Path $payload 'licenses')) | Out-Null
[IO.Directory]::CreateDirectory((Join-Path $root 'Languages')) | Out-Null
Copy-Item -LiteralPath (Join-Path $workspace 'src\ClipRay.App\Assets\ClipRay-v2.ico') -Destination (Join-Path $payload 'Assets\ClipRay-v2.ico')
Copy-Item -LiteralPath (Join-Path $workspace 'LICENSE') -Destination (Join-Path $payload 'LICENSE')
Copy-Item -LiteralPath (Join-Path $workspace 'LICENSE') -Destination (Join-Path $payload 'licenses\fixture.txt')
Copy-Item -LiteralPath (Join-Path $workspace 'installer\Languages\ChineseSimplified.isl') -Destination (Join-Path $root 'Languages\ChineseSimplified.isl')
$definition = [IO.File]::ReadAllText((Join-Path $workspace 'installer\ClipRay.iss'))
$definition = $definition.Replace('C5C7914B-7566-4912-A9E0-4D09E724BDA1', $id)
$definition = $definition.Replace('Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"', 'Subkey: "' + $fixtureKey + '\Run"')
$definition = $definition.Replace('ValueName: "ClipRay"', 'ValueName: "FixtureStartup"')
$definition = $definition.Replace('DefaultGroupName=ClipRay 截光', 'DefaultGroupName=ClipRay Fixture ' + $id)
$definition = [regex]::Replace($definition, '(?ms)^\[Icons\].*?(?=^\[Registry\])', '')
[IO.File]::WriteAllText((Join-Path $root 'fixture.iss'), $definition, [Text.UTF8Encoding]::new($true))

function Check([bool] $Condition, [string] $Message) { if (-not $Condition) { throw $Message } }
function Build-Fixture([string] $Version) {
    # Deliberately non-executable file: legacy detection must not invoke it.
    [IO.File]::WriteAllText((Join-Path $payload 'ClipRay.App.exe'), $Version)
    $output = Join-Path $root $Version
    & $IsccPath "/DStagingDir=$payload" "/DOutputDir=$output" '/DSetupFlavor=Fixture' (Join-Path $root 'fixture.iss') *> (Join-Path $root ($Version + '-compile.log'))
    Check ($LASTEXITCODE -eq 0) ('Fixture Inno compilation failed: ' + $Version)
    return Join-Path $output 'ClipRay-Fixture-Setup-1.0.6-win-x64.exe'
}
function Run-Fixture([string] $Setup, [string] $Destination, [string] $Case) {
    $args = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/SP-', '/NORESTART', '/LANG=en', '/TASKS=startup,cleanup',
        ('/DIR="' + $Destination + '"'), ('/LOG="' + (Join-Path $root ($Case + '.log')) + '"'))
    # Fresh setup must not select maintenance tasks; upgrade intentionally asks for them.
    if ($Case -eq 'fresh') { $args[5] = '/TASKS=""' }
    $process = Start-Process -FilePath $Setup -ArgumentList $args -WindowStyle Hidden -PassThru -Wait
    return $process.ExitCode
}
function Check-LockReleased([string] $Case) {
    $mutex = $null
    $found = [Threading.Mutex]::TryOpenExisting($mutexName, [ref] $mutex)
    if ($mutex) { $mutex.Dispose() }
    Check (-not $found) ('Upgrade mutex leaked after ' + $Case)
}

try {
    $first = Build-Fixture 'fixture-v1'
    Check ((Run-Fixture $first $installed 'fresh') -eq 0) 'Fresh fixture installation failed'
    Check ([IO.File]::ReadAllText((Join-Path $installed 'app\ClipRay.App.exe')) -eq 'fixture-v1') 'Fresh payload changed'
    Check-LockReleased 'fresh'
    $second = Build-Fixture 'fixture-v2'
    $run = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($fixtureKey + '\Run')
    try { $run.SetValue('FixtureStartup', 'fixture-user-disabled-choice') } finally { $run.Dispose() }
    $sentinels = @('settings-fixture.json', 'credential-fixture.txt', 'Outputs\Screenshots\kept.txt',
        'Outputs\Recordings\kept.txt', 'app\old-model-fixture.keep')
    foreach ($relative in $sentinels) {
        $path = Join-Path $installed $relative
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path)) | Out-Null
        [IO.File]::WriteAllText($path, 'synthetic retained data: ' + $relative)
    }
    $before = @{}
    foreach ($relative in $sentinels) { $before[$relative] = (Get-FileHash -LiteralPath (Join-Path $installed $relative)).Hash }
    $locked = [IO.FileStream]::new((Join-Path $installed 'app\ClipRay.App.exe'), [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    try { Check ((Run-Fixture $second $installed 'locked') -ne 0) 'Locked legacy executable was overwritten' }
    finally { $locked.Dispose() }
    Check ([IO.File]::ReadAllText((Join-Path $installed 'app\ClipRay.App.exe')) -eq 'fixture-v1') 'Failed upgrade changed old program'
    Check-LockReleased 'locked failure'
    $wrongFolderExit = Run-Fixture $second (Join-Path $root 'wrong-folder') 'wrong-folder'
    # Setup may normalize /DIR back to the installed folder before this check.
    # Rejection and safe normalization both preserve the same-directory contract.
    Check (($wrongFolderExit -eq 0) -or ([IO.File]::ReadAllText((Join-Path $installed 'app\ClipRay.App.exe')) -eq 'fixture-v1')) 'Failed migration changed old program'
    Check (-not [IO.File]::Exists((Join-Path $root 'wrong-folder\app\ClipRay.App.exe'))) 'Rejected migration wrote a new program'
    Check ((Run-Fixture $second $installed 'upgrade') -eq 0) 'Stopped legacy fixture could not upgrade without uninstall'
    Check ([IO.File]::ReadAllText((Join-Path $installed 'app\ClipRay.App.exe')) -eq 'fixture-v2') 'Upgrade did not replace the program'
    foreach ($relative in $sentinels) { Check ((Get-FileHash -LiteralPath (Join-Path $installed $relative)).Hash -eq $before[$relative]) ('Upgrade changed retained data: ' + $relative) }
    $run = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($fixtureKey + '\Run')
    try { Check ($run.GetValue('FixtureStartup') -eq 'fixture-user-disabled-choice') 'Upgrade overwrote startup preference' }
    finally { $run.Dispose() }
    Check-LockReleased 'successful upgrade'
    $log = [IO.File]::ReadAllText((Join-Path $root 'upgrade.log'))
    Check (-not $log.Contains('Windows scheduled cleanup could not be enabled')) 'Upgrade invoked cleanup configuration'
    Write-Output 'PASS actual isolated Inno fresh install, locked legacy rejection, same-directory upgrade, retained fixtures/startup and mutex release'
    Write-Output ('Fixture retained at ' + $root)
}
finally {
    # Delete only the two registry namespaces created by this unique synthetic identity.
    # No production AppId, Run entry, scheduled task or credentials are involved.
    [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKeyTree($fixtureKey, $false)
    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($uninstallKey)
    $owned = $false
    if ($key) {
        try { $owned = $key.GetValue('InstallLocation').TrimEnd('\') -eq $installed.TrimEnd('\') }
        finally { $key.Dispose() }
    }
    if ($owned) { [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKeyTree($uninstallKey, $false) }
}
