[CmdletBinding()]
param([Parameter(Mandatory)][string]$PayloadRoot, [string]$CodexPath)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$PayloadRoot = (Resolve-Path -LiteralPath $PayloadRoot).Path
$testRoot = Join-Path $repo ('artifacts\installation-test-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
$profile = Join-Path $testRoot 'codex-profile'; $data = Join-Path $testRoot 'data'; $startup = Join-Path $testRoot 'startup'
New-Item -ItemType Directory -Path $profile -Force | Out-Null
$config = Join-Path $profile 'config.toml'
'# unrelated user configuration must remain' | Set-Content -LiteralPath $config -Encoding UTF8
$configBefore = (Get-FileHash -LiteralPath $config).Hash
$manager = Join-Path $PayloadRoot 'scripts\Manage-Installation.ps1'
$common = @{ProfileRoot=$profile;DataRoot=$data;StartupRoot=$startup;SkipLaunch=$true}
if ($CodexPath) { $common.CodexPath=$CodexPath }
$count = 0
function Require([bool]$Condition,[string]$Label) { if (!$Condition) { throw ('FAIL ' + $Label) }; $script:count++; Write-Output ('PASS ' + $Label) }
& $manager -Action Check -PayloadRoot $PayloadRoot @common
& $manager -Action Install -PayloadRoot $PayloadRoot @common
Require (Test-Path -LiteralPath (Join-Path $profile 'context-notes-installation.json')) 'fresh isolated-profile installation'
Require (Test-Path -LiteralPath (Join-Path $startup 'Context Notes Native - Codex Startup.lnk')) 'startup registration in isolated directory'
$database = Join-Path $data 'history.db'
function Invoke-TestDatabase([string]$Program, [string]$DatabaseFile = $database) {
    # Windows PowerShell 5.1 strips embedded quotes from native -c arguments.
    # Pass a real synthetic script file and check every native process result.
    $scriptFile = Join-Path $testRoot ('sqlite-probe-' + [Guid]::NewGuid().ToString('N') + '.py')
    $Program | Set-Content -LiteralPath $scriptFile -Encoding UTF8
    try {
        $result = & python $scriptFile $DatabaseFile
        if ($LASTEXITCODE -ne 0) { throw 'Synthetic SQLite fixture failed.' }
        return $result
    } finally { Remove-Item -LiteralPath $scriptFile }
}
Invoke-TestDatabase 'import sqlite3,sys; c=sqlite3.connect(sys.argv[1]); c.execute("INSERT INTO settings(key,value) VALUES(?,?)",("installation-sentinel","preserve-history")); c.commit(); c.close()' | Out-Null
function Has-HistoryMarker([string]$DatabaseFile = $database) { $value = Invoke-TestDatabase 'import sqlite3,sys; c=sqlite3.connect(sys.argv[1]); print(c.execute("SELECT value FROM settings WHERE key=?",("installation-sentinel",)).fetchone()[0]); c.close()' $DatabaseFile; return $value -eq 'preserve-history' }
& $manager -Action Install -PayloadRoot $PayloadRoot @common
Require (Has-HistoryMarker) 'idempotent reinstall preserves database'

# A successful CLI may write diagnostics to stderr; its stdout must remain JSON.
$realCodex = if ($CodexPath) { $CodexPath } else {
    $cmd = Get-Command codex -ErrorAction SilentlyContinue
    if ($cmd) { $cmd.Source } else { Get-ChildItem (Join-Path $env:LOCALAPPDATA 'OpenAI\Codex\bin') -Recurse -Filter codex.exe -ErrorAction Stop | Sort-Object LastWriteTime -Descending | Select-Object -First 1 -ExpandProperty FullName }
}
$shim = Join-Path $testRoot 'codex-with-diagnostic.cmd'
@('@echo off','echo synthetic plugin diagnostic 1>&2','"%CONTEXT_NOTES_TEST_CODEX%" %*','exit /b %errorlevel%') | Set-Content -LiteralPath $shim -Encoding ASCII
$oldTestCodex = $env:CONTEXT_NOTES_TEST_CODEX
try {
    $env:CONTEXT_NOTES_TEST_CODEX = $realCodex
    $noisyCommon = @{} + $common; $noisyCommon.CodexPath = $shim
    & $manager -Action Install -PayloadRoot $PayloadRoot @noisyCommon
    Require (Has-HistoryMarker) 'successful CLI stderr diagnostics do not corrupt JSON or history'
} finally { $env:CONTEXT_NOTES_TEST_CODEX = $oldTestCodex }

$legacyExport = Join-Path $testRoot 'legacy-export.db'
& dotnet (Join-Path $PayloadRoot 'codex-plugin\plugins\context-notes\bin\SelectionProbe.dll') --history-backup $database $legacyExport
if ($LASTEXITCODE -ne 0) { throw 'Legacy export fixture failed.' }
$legacyHash = (Get-FileHash -LiteralPath $legacyExport).Hash
$migrationCommon = @{} + $common; $migrationCommon.DataRoot = Join-Path $testRoot 'new-shared-data'
& $manager -Action Install -PayloadRoot $PayloadRoot -LegacyHistoryBackup $legacyExport @migrationCommon
Require (Has-HistoryMarker (Join-Path $migrationCommon.DataRoot 'history.db')) 'verified legacy backup imports into a new destination'
Require ((Get-FileHash -LiteralPath $legacyExport).Hash -eq $legacyHash -and (Has-HistoryMarker)) 'legacy import preserves both source backup and original database'

$tampered = Join-Path $testRoot 'tampered-payload'; Copy-Item -LiteralPath $PayloadRoot -Destination $tampered -Recurse
Add-Content -LiteralPath (Join-Path $tampered 'README.md') -Value 'synthetic tamper test'
$tamperRejected = $false
try { & $manager -Action Install -PayloadRoot $tampered @common } catch { $tamperRejected = $true }
Require ($tamperRejected -and (Has-HistoryMarker)) 'tampered payload rejected before database or registration mutation'

$upgraded = Join-Path $testRoot 'upgrade-payload'; Copy-Item -LiteralPath $PayloadRoot -Destination $upgraded -Recurse
$manifestPath = Join-Path $upgraded 'codex-plugin\plugins\context-notes\.codex-plugin\plugin.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$manifest.version = '0.3.1'
[IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 12), [Text.UTF8Encoding]::new($false))
$hashes = Get-ChildItem -LiteralPath $upgraded -Recurse -File | Where-Object { $_.Name -ne 'SHA256.json' } | ForEach-Object { @{path=$_.FullName.Substring($upgraded.Length+1);sha256=(Get-FileHash -LiteralPath $_.FullName).Hash} }
[IO.File]::WriteAllText((Join-Path $upgraded 'SHA256.json'), ($hashes | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
Invoke-TestDatabase 'import sqlite3,sys; c=sqlite3.connect(sys.argv[1]); [c.execute(s) for s in ["DROP INDEX annotations_scope_date","DROP INDEX messages_annotation","ALTER TABLE annotations DROP COLUMN title","ALTER TABLE annotations DROP COLUMN archived","ALTER TABLE annotations DROP COLUMN deleted","PRAGMA user_version=1"]]; c.commit(); c.close()' | Out-Null
$injected = $false
try { & $manager -Action Install -PayloadRoot $upgraded -InjectFailure @common } catch { $injected = $true }
Require $injected 'injected upgrade failure is reported'
$state = Get-Content -LiteralPath (Join-Path $profile 'context-notes-installation.json') -Raw | ConvertFrom-Json
Require ($state.version -eq '0.3.0' -and (Has-HistoryMarker)) 'failed upgrade restores registration state and history'
$schema = Invoke-TestDatabase 'import sqlite3,sys; c=sqlite3.connect(sys.argv[1]); print(c.execute("PRAGMA user_version").fetchone()[0]); c.close()'
Require ($schema -eq '1') 'failed upgrade restores pre-migration schema'
$retained = $null
try { & $manager -Action Install -PayloadRoot $upgraded -InjectFailure -InjectRollbackFailure @common }
catch { $retained = $_.Exception.Data['RecoveryBackupPath'] }
$retainedDatabase = if ($retained) { Join-Path $retained 'history-before.db' } else { '' }
Require ($retained -and (Test-Path -LiteralPath $retainedDatabase)) 'rollback failure preserves recovery database'
$helperDll = Join-Path $PayloadRoot 'codex-plugin\plugins\context-notes\bin\SelectionProbe.dll'
& dotnet $helperDll --history-backup $retainedDatabase $database
if ($LASTEXITCODE -ne 0) { throw 'Synthetic recovery restore failed.' }
$schema = Invoke-TestDatabase 'import sqlite3,sys; c=sqlite3.connect(sys.argv[1]); print(c.execute("PRAGMA user_version").fetchone()[0]); c.close()'
Require ($schema -eq '1' -and (Has-HistoryMarker)) 'retained recovery database restores original schema and history'
& $manager -Action Install -PayloadRoot $upgraded @common
$state = Get-Content -LiteralPath (Join-Path $profile 'context-notes-installation.json') -Raw | ConvertFrom-Json
Require ($state.version -eq '0.3.1') 'successful version upgrade'
Require (Has-HistoryMarker) 'successful upgrade preserves history'
$schema = Invoke-TestDatabase 'import sqlite3,sys; c=sqlite3.connect(sys.argv[1]); print(c.execute("PRAGMA user_version").fetchone()[0]); c.close()'
Require ($schema -eq '2' -and (Test-Path -LiteralPath ($database + '.pre-v2.db'))) 'upgrade migrates database with pre-migration backup'
& $manager -Action Uninstall -PayloadRoot $upgraded @common
Require ((Test-Path -LiteralPath $database) -and !(Test-Path -LiteralPath (Join-Path $profile 'context-notes-installation.json'))) 'uninstall preserves history by default'
Require (!(Test-Path -LiteralPath (Join-Path $startup 'Context Notes Native - Codex Startup.lnk'))) 'uninstall removes owned startup shortcut'
& $manager -Action Install -PayloadRoot $PayloadRoot @common
& $manager -Action Uninstall -PayloadRoot $PayloadRoot -RemoveHistory @common
Require (!(Test-Path -LiteralPath $database)) 'explicit history removal'
# Codex CLI may legitimately add only its own plugin config; the unrelated sentinel must remain.
Require ((Get-Content -LiteralPath $config -Raw).Contains('# unrelated user configuration must remain')) 'unrelated Codex configuration preserved'
Write-Output "PASS installation_checks=$count root=$testRoot clean_windows_environment=false"
