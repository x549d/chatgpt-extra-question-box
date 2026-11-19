[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$SetupExe,
    [string]$CodexPath
)
$ErrorActionPreference = 'Stop'
$SetupExe = [IO.Path]::GetFullPath($SetupExe)
if (!(Test-Path -LiteralPath $SetupExe)) { throw "Setup executable not found: $SetupExe" }

$aclText = & icacls $SetupExe 2>$null | Out-String
if ($aclText -match 'Low Mandatory Level') {
    throw "Setup executable '$SetupExe' has a Low Mandatory Level SACL. Copy it to a Medium-integrity folder before running standard EXE tests."
}

$workRoot = Join-Path ([IO.Path]::GetTempPath()) ('ContextNotesNative-ExeTest-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $workRoot -Force | Out-Null

$count = 0
function Require([bool]$Condition, [string]$Label) {
    if (!$Condition) { throw ('FAIL standard_exe: ' + $Label) }
    $script:count++
    Write-Output ('PASS standard_exe: ' + $Label)
}

function Get-ExpectedTestKeyName([string]$StateRoot) {
    $norm = [IO.Path]::GetFullPath($StateRoot).ToLowerInvariant()
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        $bytes = [Text.Encoding]::Unicode.GetBytes($norm)
        $hex = ([BitConverter]::ToString($sha.ComputeHash($bytes)).Replace('-', '')).ToLowerInvariant()
    } finally { $sha.Dispose() }
    return 'ContextNotesNative.Test.' + $hex.Substring(0, 16) + '_is1'
}

function Invoke-PyDb([string]$Program, [string]$DatabaseFile) {
    $py = Join-Path $workRoot ('db-' + [Guid]::NewGuid().ToString('N') + '.py')
    $Program | Set-Content -LiteralPath $py -Encoding UTF8
    try {
        $out = & python $py $DatabaseFile
        if ($LASTEXITCODE -ne 0) { throw "Python SQLite probe failed on $DatabaseFile" }
        return $out
    } finally {
        if (Test-Path -LiteralPath $py) { Remove-Item -LiteralPath $py -Force }
    }
}

$uninstallRoot = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall'
$createdKeys = [Collections.Generic.List[string]]::new()

try {
    # 1. Fresh install via real Setup EXE into brand-new non-existent profile/data/startup
    $appDir = Join-Path $workRoot 'install-app'
    $stateDir = Join-Path $workRoot 'install-state'
    $logFresh = Join-Path $workRoot 'setup-fresh.log'
    $expectedKey = Get-ExpectedTestKeyName $stateDir
    $createdKeys.Add($expectedKey)

    $proc = Start-Process -FilePath $SetupExe -ArgumentList @(
        '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/NOICONS',
        "/DIR=$appDir",
        "/TESTROOT=$stateDir",
        "/LOG=$logFresh"
    ) -Wait -PassThru
    Require ($proc.ExitCode -eq 0) 'fresh standard EXE installation exits 0'

    $freshLogText = Get-Content -LiteralPath $logFresh -Raw
    Require ($freshLogText -match 'Context Notes temp preflight .* write=ok') 'Temp preflight write=ok logged'
    Require ($freshLogText -match 'Context Notes current-user registry preflight write=ok') 'HKCU64 preflight write=ok logged'
    Require (Test-Path -LiteralPath (Join-Path $uninstallRoot $expectedKey)) "exact isolated HKCU64 uninstall key created ($expectedKey)"
    Require (Test-Path -LiteralPath (Join-Path $appDir 'unins000.exe')) 'unins000.exe deployed'
    Require (Test-Path -LiteralPath (Join-Path $stateDir 'profile\context-notes-installation.json')) 'plugin installation state created in brand-new CODEX_HOME'
    Require (Test-Path -LiteralPath (Join-Path $stateDir 'startup\Context Notes Native - Codex Startup.lnk')) 'startup watcher shortcut created'

    $db = Join-Path $stateDir 'data\history.db'
    Require (Test-Path -LiteralPath $db) 'SQLite history.db initialized'
    $integrity = Invoke-PyDb 'import sqlite3,sys; c=sqlite3.connect(sys.argv[1]); print(c.execute("PRAGMA integrity_check").fetchone()[0], c.execute("PRAGMA user_version").fetchone()[0]); c.close()' $db
    Require ($integrity -eq 'ok 2') 'SQLite database integrity ok and schema 2'

    # Write synthetic sentinel into isolated DB
    Invoke-PyDb 'import sqlite3,sys; c=sqlite3.connect(sys.argv[1]); c.execute("INSERT INTO settings(key,value) VALUES(?,?)",("exe-sentinel","keep-me")); c.commit(); c.close()' $db | Out-Null

    # 2. Reinstall / in-place upgrade over existing installation
    $logUpgrade = Join-Path $workRoot 'setup-reinstall.log'
    $proc2 = Start-Process -FilePath $SetupExe -ArgumentList @(
        '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/NOICONS',
        "/DIR=$appDir",
        "/TESTROOT=$stateDir",
        "/LOG=$logUpgrade"
    ) -Wait -PassThru
    Require ($proc2.ExitCode -eq 0) 'reinstall/in-place upgrade via standard EXE exits 0'
    $sentinel = Invoke-PyDb 'import sqlite3,sys; c=sqlite3.connect(sys.argv[1]); print(c.execute("SELECT value FROM settings WHERE key=?",("exe-sentinel",)).fetchone()[0]); c.close()' $db
    Require ($sentinel -eq 'keep-me') 'reinstall/upgrade preserves existing SQLite history'

    # 3. Fresh install failure rollback via real Setup EXE
    $failAppDir = Join-Path $workRoot 'fail-app'
    $failStateDir = Join-Path $workRoot 'fail-state'
    $failLog = Join-Path $workRoot 'setup-fail.log'
    $failKey = Get-ExpectedTestKeyName $failStateDir
    $createdKeys.Add($failKey)

    $procFail = Start-Process -FilePath $SetupExe -ArgumentList @(
        '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/NOICONS',
        "/DIR=$failAppDir",
        "/TESTROOT=$failStateDir",
        '/INJECTFAILURE=1',
        "/LOG=$failLog"
    ) -Wait -PassThru
    Require ($procFail.ExitCode -ne 0) "injected installation failure returns non-zero exit code ($($procFail.ExitCode))"
    Require (!(Test-Path -LiteralPath (Join-Path $uninstallRoot $failKey))) 'failed installation does not leave HKCU uninstall key'
    Require (!(Test-Path -LiteralPath (Join-Path $failAppDir 'versions\0.3.0'))) 'failed installation rolls back deployed program files'
    Require (!(Test-Path -LiteralPath (Join-Path $failAppDir 'installation-context.ini'))) 'failed installation removes installation-context.ini'

    # 4. Upgrade failure rollback (downgrade DB to v1, inject failure during upgrade)
    Invoke-PyDb 'import sqlite3,sys; c=sqlite3.connect(sys.argv[1]); [c.execute(s) for s in ["DROP INDEX annotations_scope_date","DROP INDEX messages_annotation","ALTER TABLE annotations DROP COLUMN title","ALTER TABLE annotations DROP COLUMN archived","ALTER TABLE annotations DROP COLUMN deleted","PRAGMA user_version=1"]]; c.commit(); c.close()' $db | Out-Null
    $logUpgFail = Join-Path $workRoot 'setup-upgrade-fail.log'
    $procUpgFail = Start-Process -FilePath $SetupExe -ArgumentList @(
        '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/NOICONS',
        "/DIR=$appDir",
        "/TESTROOT=$stateDir",
        '/INJECTFAILURE=1',
        "/LOG=$logUpgFail"
    ) -Wait -PassThru
    Require ($procUpgFail.ExitCode -ne 0) "failed upgrade returns non-zero exit code ($($procUpgFail.ExitCode))"
    $schemaAfterFail = Invoke-PyDb 'import sqlite3,sys; c=sqlite3.connect(sys.argv[1]); print(c.execute("PRAGMA user_version").fetchone()[0], c.execute("SELECT value FROM settings WHERE key=?",("exe-sentinel",)).fetchone()[0]); c.close()' $db
    Require ($schemaAfterFail -eq '1 keep-me') 'failed upgrade restores pre-migration schema v1 and sentinel'

    # Re-run clean upgrade to verify v1 -> v2 migration via real EXE
    $logMig = Join-Path $workRoot 'setup-migrate.log'
    $procMig = Start-Process -FilePath $SetupExe -ArgumentList @(
        '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/NOICONS',
        "/DIR=$appDir",
        "/TESTROOT=$stateDir",
        "/LOG=$logMig"
    ) -Wait -PassThru
    Require ($procMig.ExitCode -eq 0) 'upgrade from v1 schema via standard EXE exits 0'
    $schemaAfterMig = Invoke-PyDb 'import sqlite3,sys; c=sqlite3.connect(sys.argv[1]); print(c.execute("PRAGMA user_version").fetchone()[0], c.execute("SELECT value FROM settings WHERE key=?",("exe-sentinel",)).fetchone()[0]); c.close()' $db
    Require ($schemaAfterMig -eq '2 keep-me' -and (Test-Path -LiteralPath ($db + '.pre-v2.db'))) 'standard EXE upgrade migrates v1->v2 with pre-migration backup and preserves history'

    # 5. Default uninstall via real unins000.exe (must preserve history by default)
    $unins = Join-Path $appDir 'unins000.exe'
    $procUnins = Start-Process -FilePath $unins -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART' -Wait -PassThru
    Require ($procUnins.ExitCode -eq 0) 'default standard uninstaller exits 0'
    Require (!(Test-Path -LiteralPath (Join-Path $uninstallRoot $expectedKey))) 'default uninstall removes HKCU uninstall key'
    Require (!(Test-Path -LiteralPath (Join-Path $stateDir 'profile\context-notes-installation.json'))) 'default uninstall removes plugin registration state'
    Require (!(Test-Path -LiteralPath (Join-Path $stateDir 'startup\Context Notes Native - Codex Startup.lnk'))) 'default uninstall removes startup shortcut'
    Require (Test-Path -LiteralPath $db) 'default uninstall preserves local history.db'
    $sentinelAfterUnins = Invoke-PyDb 'import sqlite3,sys; c=sqlite3.connect(sys.argv[1]); print(c.execute("SELECT value FROM settings WHERE key=?",("exe-sentinel",)).fetchone()[0]); c.close()' $db
    Require ($sentinelAfterUnins -eq 'keep-me') 'default uninstall preserves database content intact'

    # 6. Reinstall then explicit RemoveHistory uninstall via real unins000.exe /REMOVEHISTORY=1
    $procRe = Start-Process -FilePath $SetupExe -ArgumentList @(
        '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/NOICONS',
        "/DIR=$appDir",
        "/TESTROOT=$stateDir"
    ) -Wait -PassThru
    Require ($procRe.ExitCode -eq 0) 'reinstall after default uninstall exits 0'
    $procUninsDel = Start-Process -FilePath (Join-Path $appDir 'unins000.exe') -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/REMOVEHISTORY=1' -Wait -PassThru
    Require ($procUninsDel.ExitCode -eq 0) 'explicit remove-history uninstaller exits 0'
    Require (!(Test-Path -LiteralPath $db)) 'explicit /REMOVEHISTORY=1 removes local history.db'
    Require (!(Test-Path -LiteralPath (Join-Path $uninstallRoot $expectedKey))) 'explicit remove-history uninstall removes HKCU uninstall key'

    Write-Output "PASS standard_exe_checks=$count isolated_testroot=true clean_windows_environment=false"
} finally {
    foreach ($k in $createdKeys) {
        $kp = Join-Path $uninstallRoot $k
        if (Test-Path -LiteralPath $kp) { Remove-Item -LiteralPath $kp -Recurse -Force -ErrorAction SilentlyContinue }
    }
    if (Test-Path -LiteralPath $workRoot) { Remove-Item -LiteralPath $workRoot -Recurse -Force -ErrorAction SilentlyContinue }
}
