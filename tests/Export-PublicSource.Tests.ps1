$ErrorActionPreference = 'Stop'
$exporter = Join-Path (Split-Path -Parent $PSScriptRoot) 'scripts\Export-PublicSource.ps1'
$root = Join-Path ([IO.Path]::GetTempPath()) ('clipray-public-source-test-' + [Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory((Join-Path $root 'scripts'))
[void][IO.Directory]::CreateDirectory((Join-Path $root 'src'))
[void][IO.Directory]::CreateDirectory((Join-Path $root 'src\Diagnostics'))
[void][IO.Directory]::CreateDirectory((Join-Path $root 'docs\testing'))
[IO.File]::WriteAllText((Join-Path $root 'scripts\public-source-paths.txt'), "README.md`nsrc`nscripts/public-source-paths.txt`n")
[IO.File]::WriteAllText((Join-Path $root 'README.md'), 'committed source')
[IO.File]::WriteAllText((Join-Path $root 'src\example.cs'), 'class Example {}')
[IO.File]::WriteAllText((Join-Path $root 'src\Diagnostics\Trace.cs'), 'class Trace {}')
[IO.File]::WriteAllText((Join-Path $root 'docs\testing\private.md'), 'synthetic private record')
& git -C $root init --quiet
& git -C $root add -- README.md src/example.cs src/Diagnostics/Trace.cs scripts/public-source-paths.txt docs/testing/private.md
& git -C $root -c user.name=Fixture -c user.email=fixture@example.invalid commit --quiet -m 'Synthetic source fixture'
if ($LASTEXITCODE -ne 0) { throw 'Fixture commit failed.' }
[IO.File]::WriteAllText((Join-Path $root 'src\untracked.txt'), 'must not publish')
[IO.File]::WriteAllText((Join-Path $root 'README.md'), 'dirty working copy must not replace committed source')
$zip = Join-Path $root 'export.zip'
& $exporter -WorkspaceRoot $root -OutputPath $zip | Out-Null
$archive = [IO.Compression.ZipFile]::OpenRead($zip)
try {
    $names = @($archive.Entries.FullName)
    if ($names -contains 'ClipRay-1.0.6/src/untracked.txt' -or $names -contains 'ClipRay-1.0.6/docs/testing/private.md') {
        throw 'Source archive leaked untracked or nonselected data.'
    }
    $reader = [IO.StreamReader]::new($archive.GetEntry('ClipRay-1.0.6/README.md').Open())
    try { if ($reader.ReadToEnd() -ne 'committed source') { throw 'Export used dirty working copy.' } } finally { $reader.Dispose() }
    if ($null -eq $archive.GetEntry('ClipRay-1.0.6/SOURCE-REVISION.txt')) { throw 'Export omitted its revision.' }
    if ($null -eq $archive.GetEntry('ClipRay-1.0.6/src/Diagnostics/Trace.cs')) { throw 'Export rejected diagnostic source code.' }
} finally { $archive.Dispose() }
$refused = $false
try { & $exporter -WorkspaceRoot $root -OutputPath $zip | Out-Null } catch { $refused = $_.Exception.Message -like '*already exists*' }
if (-not $refused) { throw 'Export overwrote an existing archive.' }
Write-Output "PASS committed source identity, tracked private exclusion, untracked exclusion, revision record, overwrite refusal. Fixture retained at $root"
