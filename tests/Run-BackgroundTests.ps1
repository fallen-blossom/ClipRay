[CmdletBinding()]
param(
    [string] $DotNet = 'dotnet',
    [switch] $SkipBuild,
    [switch] $CollectOnly,
    [switch] $FailureSentinel
)
$ErrorActionPreference = 'Stop'
if ($CollectOnly -and $FailureSentinel) { throw 'Choose one explicit mode.' }
$workspace = Split-Path -Parent $PSScriptRoot
$appProject = Join-Path $workspace 'tests\ClipRay.App.Tests\ClipRay.App.Tests.csproj'
$coreProject = Join-Path $workspace 'tests\ClipRay.Core.Tests\ClipRay.Core.Tests.csproj'
$appAssembly = Join-Path $workspace 'tests\ClipRay.App.Tests\bin\Release\net10.0-windows10.0.26100.0\ClipRay.App.Tests.dll'
$coreAssembly = Join-Path $workspace 'tests\ClipRay.Core.Tests\bin\Release\net10.0\ClipRay.Core.Tests.dll'
if (-not $SkipBuild) {
    & $DotNet build $appProject -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    if (-not $CollectOnly -and -not $FailureSentinel) {
        & $DotNet build $coreProject -c Release --no-restore
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }
}
if (-not (Test-Path -LiteralPath $appAssembly -PathType Leaf)) { throw 'Build the background test assembly first.' }
if ($CollectOnly) {
    & $DotNet $appAssembly --collect
    exit $LASTEXITCODE
}
if ($FailureSentinel) {
    & $DotNet $appAssembly --background-task-9-tests failure-sentinel
    exit $LASTEXITCODE
}
if (-not (Test-Path -LiteralPath $coreAssembly -PathType Leaf)) { throw 'Build Core.Tests first.' }
# Each stage keeps its actual runner/context in a separate process. Never invoke App.Tests without a selector.
$priorJunctionFixture = $env:CLIPRAY_TASK6_JUNCTION_FIXTURE
Push-Location -LiteralPath $workspace
try {
    foreach ($stage in 1..9) {
        if ($stage -eq 6) {
            $fixtureRoot = [IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetTempPath()) ('clipray-task6-junction-' + [guid]::NewGuid().ToString('N'))))
            $temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar)
            if ([IO.Path]::GetDirectoryName($fixtureRoot) -ne $temporaryRoot) { throw 'Fixture escaped OS-temp.' }
            $sessions = Join-Path $fixtureRoot 'sessions'
            $target = Join-Path $fixtureRoot 'target'
            $link = Join-Path $sessions ([guid]::NewGuid().ToString('N'))
            foreach ($path in @($sessions, $target, $link)) {
                if (-not [IO.Path]::GetFullPath($path).StartsWith($fixtureRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
                    throw 'Fixture child escaped its owned root.'
                }
            }
            New-Item -ItemType Directory -Path $sessions, $target | Out-Null
            New-Item -ItemType Junction -Path $link -Target $target | Out-Null
            $env:CLIPRAY_TASK6_JUNCTION_FIXTURE = $fixtureRoot
            # The actual reparse test removes only its link. Retain target bytes for test evidence.
        }
        & $DotNet $appAssembly "--background-task-$stage-tests"
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
        if ($stage -eq 7) {
            & $DotNet $appAssembly --background-task-7-revision-tests
            if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
        }
    }
    & $DotNet $coreAssembly
    exit $LASTEXITCODE
}
finally {
    $env:CLIPRAY_TASK6_JUNCTION_FIXTURE = $priorJunctionFixture
    Pop-Location
}
