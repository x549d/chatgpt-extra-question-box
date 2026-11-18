[CmdletBinding()]
param([Parameter(Mandatory)][string]$PayloadRoot, [string]$CodexPath)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$testRoot = Join-Path $repo ('artifacts\installation-test-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
$profile = Join-Path $testRoot 'codex-profile'; $data = Join-Path $testRoot 'data'; $startup = Join-Path $testRoot 'startup'
New-Item -ItemType Directory -Path $profile -Force | Out-Null
$config = Join-Path $profile 'config.toml'
'# unrelated user configuration must remain' | Set-Content -LiteralPath $config -Encoding UTF8
$configBefore = (Get-FileHash -LiteralPath $config).Hash
$manager = Join-Path $repo 'scripts\Manage-Installation.ps1'
$common = @{ProfileRoot=$profile;DataRoot=$data;StartupRoot=$startup;SkipLaunch=$true}
if ($CodexPath) { $common.CodexPath=$CodexPath }
$count = 0
function Require([bool]$Condition,[string]$Label) { if (!$Condition) { throw ('FAIL ' + $Label) }; $script:count++; Write-Output ('PASS ' + $Label) }
& $manager -Action Check -PayloadRoot $PayloadRoot @common
& $manager -Action Install -PayloadRoot $PayloadRoot @common
Require (Test-Path -LiteralPath (Join-Path $profile 'context-notes-installation.json')) 'fresh isolated-profile installation'
Require (Test-Path -LiteralPath (Join-Path $startup 'Context Notes Native - Codex Startup.lnk')) 'startup registration in isolated directory'
$database = Join-Path $data 'history.db'
& python -c 'import sqlite3,sys; c=sqlite3.connect(sys.argv[1]); c.execute("INSERT INTO settings(key,value) VALUES(?,?)",("installation-sentinel","preserve-history")); c.commit(); c.close()' $database
function Has-HistoryMarker { $value = & python -c 'import sqlite3,sys; c=sqlite3.connect(sys.argv[1]); print(c.execute("SELECT value FROM settings WHERE key=?",("installation-sentinel",)).fetchone()[0]); c.close()' $database; return $value -eq 'preserve-history' }
& $manager -Action Install -PayloadRoot $PayloadRoot @common
Require (Has-HistoryMarker) 'idempotent reinstall preserves database'

$tampered = Join-Path $testRoot 'tampered-payload'; Copy-Item -LiteralPath $PayloadRoot -Destination $tampered -Recurse
Add-Content -LiteralPath (Join-Path $tampered 'README.md') -Value 'synthetic tamper test'
$tamperRejected = $false
try { & $manager -Action Install -PayloadRoot $tampered @common } catch { $tamperRejected = $true }
Require ($tamperRejected -and (Has-HistoryMarker)) 'tampered payload rejected before database or registration mutation'

$upgraded = Join-Path $testRoot 'upgrade-payload'; Copy-Item -LiteralPath $PayloadRoot -Destination $upgraded -Recurse
$manifestPath = Join-Path $upgraded 'codex-plugin\plugins\context-notes\.codex-plugin\plugin.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$manifest.version = '0.3.1'; $manifest | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $manifestPath -Encoding UTF8
$hashes = Get-ChildItem -LiteralPath $upgraded -Recurse -File | Where-Object { $_.Name -ne 'SHA256.json' } | ForEach-Object { @{path=$_.FullName.Substring($upgraded.Length+1);sha256=(Get-FileHash -LiteralPath $_.FullName).Hash} }
$hashes | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $upgraded 'SHA256.json') -Encoding UTF8
& python -c 'import sqlite3,sys; c=sqlite3.connect(sys.argv[1]); [c.execute(s) for s in ["DROP INDEX annotations_scope_date","DROP INDEX messages_annotation","ALTER TABLE annotations DROP COLUMN title","ALTER TABLE annotations DROP COLUMN archived","ALTER TABLE annotations DROP COLUMN deleted","PRAGMA user_version=1"]]; c.commit(); c.close()' $database
$injected = $false
try { & $manager -Action Install -PayloadRoot $upgraded -InjectFailure @common } catch { $injected = $true }
Require $injected 'injected upgrade failure is reported'
$state = Get-Content -LiteralPath (Join-Path $profile 'context-notes-installation.json') -Raw | ConvertFrom-Json
Require ($state.version -eq '0.3.0' -and (Has-HistoryMarker)) 'failed upgrade restores registration state and history'
$schema = & python -c 'import sqlite3,sys; c=sqlite3.connect(sys.argv[1]); print(c.execute("PRAGMA user_version").fetchone()[0]); c.close()' $database
Require ($schema -eq '1') 'failed upgrade restores pre-migration schema'
$retained = $null
try { & $manager -Action Install -PayloadRoot $upgraded -InjectFailure -InjectRollbackFailure @common }
catch { $retained = $_.Exception.Data['RecoveryBackupPath'] }
$retainedDatabase = if ($retained) { Join-Path $retained 'history-before.db' } else { '' }
Require ($retained -and (Test-Path -LiteralPath $retainedDatabase)) 'rollback failure preserves recovery database'
$helperDll = Join-Path $PayloadRoot 'codex-plugin\plugins\context-notes\bin\SelectionProbe.dll'
& dotnet $helperDll --history-backup $retainedDatabase $database
if ($LASTEXITCODE -ne 0) { throw 'Synthetic recovery restore failed.' }
$schema = & python -c 'import sqlite3,sys; c=sqlite3.connect(sys.argv[1]); print(c.execute("PRAGMA user_version").fetchone()[0]); c.close()' $database
Require ($schema -eq '1' -and (Has-HistoryMarker)) 'retained recovery database restores original schema and history'
& $manager -Action Install -PayloadRoot $upgraded @common
$state = Get-Content -LiteralPath (Join-Path $profile 'context-notes-installation.json') -Raw | ConvertFrom-Json
Require ($state.version -eq '0.3.1') 'successful version upgrade'
Require (Has-HistoryMarker) 'successful upgrade preserves history'
$schema = & python -c 'import sqlite3,sys; c=sqlite3.connect(sys.argv[1]); print(c.execute("PRAGMA user_version").fetchone()[0]); c.close()' $database
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
