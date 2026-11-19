[CmdletBinding()]
param(
    [ValidateSet('Check','Install','Uninstall')][string]$Action = 'Check',
    [string]$PayloadRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$ProfileRoot = $(if ($env:CODEX_HOME) { $env:CODEX_HOME } else { Join-Path $env:USERPROFILE '.codex' }),
    [string]$DataRoot = (Join-Path $env:USERPROFILE '.context-notes-native\data'),
    [string]$StartupRoot = [Environment]::GetFolderPath('Startup'),
    [string]$CodexPath,
    [switch]$SkipLaunch,
    [switch]$RemoveHistory,
    [switch]$InjectFailure,
    [switch]$InjectRollbackFailure,
    [string]$LegacyHistoryBackup
)
$ErrorActionPreference = 'Stop'
$PayloadRoot = [IO.Path]::GetFullPath($PayloadRoot)
$ProfileRoot = [IO.Path]::GetFullPath($ProfileRoot)
$DataRoot = [IO.Path]::GetFullPath($DataRoot)
$marketplace = Join-Path $PayloadRoot 'codex-plugin'
$plugin = Join-Path $marketplace 'plugins\context-notes'
$manifest = Get-Content -LiteralPath (Join-Path $plugin '.codex-plugin\plugin.json') -Raw | ConvertFrom-Json
if ($manifest.name -ne 'context-notes' -or $manifest.version -notmatch '^\d+\.\d+\.\d+$') { throw 'Invalid owned plugin manifest.' }
$dll = Join-Path $plugin 'bin\SelectionProbe.dll'
$cacheRoot = [IO.Path]::GetFullPath((Join-Path $ProfileRoot 'plugins\cache\context-notes-native\context-notes'))
$shortcut = Join-Path $StartupRoot 'Context Notes Native - Codex Startup.lnk'
$stateFile = Join-Path $ProfileRoot 'context-notes-installation.json'
if (!$CodexPath) { $command = Get-Command codex -ErrorAction SilentlyContinue; if ($command) { $CodexPath = $command.Source } }
if (!$CodexPath) { $CodexPath = Get-ChildItem (Join-Path $env:LOCALAPPDATA 'OpenAI\Codex\bin') -Recurse -Filter codex.exe -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1 -ExpandProperty FullName }
if (!$CodexPath) { throw 'Codex Desktop / CLI was not found. Install Codex Desktop first.' }
if ([Environment]::OSVersion.Version.Major -lt 10) { throw 'Windows 10 or 11 is required.' }
if (!(@(& dotnet --list-runtimes) -match '^Microsoft.WindowsDesktop.App 10\.')) { throw '.NET 10 Windows Desktop Runtime x64 is required.' }
if (!(Test-Path -LiteralPath $dll)) { throw 'Published application payload is missing.' }
$productionData = [IO.Path]::GetFullPath((Join-Path $env:USERPROFILE '.context-notes-native\data'))
$databasePath = Join-Path $DataRoot 'history.db'
if ($LegacyHistoryBackup) {
    $LegacyHistoryBackup = [IO.Path]::GetFullPath($LegacyHistoryBackup)
    if ($Action -ne 'Install' -or !(Test-Path -LiteralPath $LegacyHistoryBackup) -or (Test-Path -LiteralPath $databasePath)) {
        throw 'Legacy import requires Install, a verified existing backup and a destination database that does not exist. No original database will be overwritten.'
    }
}
if ($Action -eq 'Install' -and $DataRoot -eq $productionData -and !(Test-Path -LiteralPath $databasePath) -and !$LegacyHistoryBackup) {
    $legacy = @((Join-Path $env:LOCALAPPDATA 'ContextNotesNative\data\history.db'),(Join-Path $env:USERPROFILE 'AppData\Local\Packages\OpenAI.Codex_2p2nqsd0c76g0\LocalCache\Local\ContextNotesNative\data\history.db'))
    if (@($legacy | Where-Object { Test-Path -LiteralPath $_ }).Count) { throw 'Legacy history detected. Export and verify both desktop/package views, then pass -LegacyHistoryBackup. No files were changed.' }
}
if ($InjectRollbackFailure -and (!$SkipLaunch -or $DataRoot -eq $productionData)) { throw 'Rollback fault injection requires isolated test data and SkipLaunch.' }
if ($SkipLaunch -and $DataRoot -eq $productionData -and $Action -ne 'Check') {
    [void](& dotnet $dll --status 2>&1)
    if ($LASTEXITCODE -eq 0) { throw 'Stop the production Companion before a deployment using SkipLaunch. No files were changed.' }
}

function Invoke-Codex([string[]]$Arguments) {
    # Keep native diagnostics out of the JSON response. Windows PowerShell 5.1
    # otherwise turns stderr into terminating NativeCommandError records.
    $savedPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $result = & $CodexPath @Arguments 2>$null | Out-String
        $code = $LASTEXITCODE
    } finally { $ErrorActionPreference = $savedPreference }
    if ($code -ne 0) { throw ('Codex plugin command failed: ' + ($Arguments -join ' ')) }
    return $result
}
function Invoke-Helper([string[]]$Arguments, [int[]]$Allowed = @(0)) {
    $result = & dotnet $dll @Arguments 2>&1 | Out-String
    if ($LASTEXITCODE -notin $Allowed) { throw ('Context Notes command failed: ' + $Arguments[0]) }
    return $result
}
function Assert-Child([string]$Child, [string]$Parent) {
    $full = [IO.Path]::GetFullPath($Child); $basePath = [IO.Path]::GetFullPath($Parent).TrimEnd('\') + '\'
    if (!$full.StartsWith($basePath, [StringComparison]::OrdinalIgnoreCase)) { throw 'Refusing filesystem action outside the owned directory.' }
    return $full
}
function Stop-OwnedProcesses {
    if ($SkipLaunch) { return }
    [void](Invoke-Helper @('--stop-watcher') @(0,3)); [void](Invoke-Helper @('--stop') @(0,3))
    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    do {
        $running = @(Get-CimInstance Win32_Process -Filter "name='SelectionProbe.exe'" | Where-Object {
            $_.ExecutablePath -and ($_.ExecutablePath.StartsWith($cacheRoot + '\', [StringComparison]::OrdinalIgnoreCase) -or $_.ExecutablePath.StartsWith($plugin + '\', [StringComparison]::OrdinalIgnoreCase))
        })
        if (!$running.Count) { return }
        Start-Sleep -Milliseconds 200
    } while ([DateTime]::UtcNow -lt $deadline)
    throw 'Existing Companion or watcher has not stopped; no files were overwritten.'
}
if ($Action -eq 'Install') {
    $hashFile = Join-Path $PayloadRoot 'SHA256.json'
    if (!(Test-Path -LiteralPath $hashFile)) { throw 'Release payload hash manifest is missing.' }
    foreach ($entry in (Get-Content -LiteralPath $hashFile -Raw | ConvertFrom-Json)) {
        $sourceFile = Assert-Child (Join-Path $PayloadRoot $entry.path) $PayloadRoot
        if ($entry.sha256 -notmatch '^[A-Fa-f0-9]{64}$' -or (Get-FileHash -LiteralPath $sourceFile -Algorithm SHA256).Hash -ne $entry.sha256) { throw 'Release payload integrity check failed before deployment.' }
    }
}
function Set-Shortcut([string]$Binary) {
    New-Item -ItemType Directory -Path $StartupRoot -Force | Out-Null
    if (Test-Path -LiteralPath $shortcut) {
        $existing = (New-Object -ComObject WScript.Shell).CreateShortcut($shortcut)
        if ($existing.Arguments -ne '--watch-codex' -or !$existing.TargetPath.StartsWith($cacheRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Startup shortcut is not owned by Context Notes.' }
    }
    $link = (New-Object -ComObject WScript.Shell).CreateShortcut($shortcut)
    $link.TargetPath = $Binary; $link.Arguments = '--watch-codex'; $link.WorkingDirectory = Split-Path -Parent $Binary
    $link.Description = 'Context Notes: start only with verified Codex Desktop'; $link.Save()
}
$previousCodexHome = $env:CODEX_HOME
$env:CODEX_HOME = $ProfileRoot
try {
    if ($Action -eq 'Check') { Write-Output ('PASS prerequisites version=' + $manifest.version); return }
    if ($Action -eq 'Install' -and !(Test-Path -LiteralPath $ProfileRoot)) {
        New-Item -ItemType Directory -Path $ProfileRoot -Force | Out-Null
    }
    $sources = if (Test-Path -LiteralPath $ProfileRoot) { (Invoke-Codex @('plugin','marketplace','list','--json') | ConvertFrom-Json).marketplaces } else { @() }
    $previous = $sources | Where-Object { $_.name -eq 'context-notes-native' } | Select-Object -First 1
    if ($Action -eq 'Uninstall') {
        Stop-OwnedProcesses
        if ($previous) {
            [void](Invoke-Codex @('plugin','remove','context-notes@context-notes-native','--json'))
            [void](Invoke-Codex @('plugin','marketplace','remove','context-notes-native'))
        }
        if (Test-Path -LiteralPath $shortcut) {
            $link = (New-Object -ComObject WScript.Shell).CreateShortcut($shortcut)
            if ($link.TargetPath.StartsWith($cacheRoot + '\', [StringComparison]::OrdinalIgnoreCase) -and $link.Arguments -eq '--watch-codex') { Remove-Item -LiteralPath $shortcut }
        }
        if (Test-Path -LiteralPath $stateFile) { Remove-Item -LiteralPath $stateFile }
        if ($RemoveHistory) {
            $expectedData = [IO.Path]::GetFullPath((Join-Path $env:USERPROFILE '.context-notes-native\data'))
            if ($DataRoot -ne $expectedData -and !$SkipLaunch) { throw 'Refusing removal outside the current user history directory.' }
            # Delete only known application database files, never an arbitrary recursive data tree.
            if (Test-Path -LiteralPath $DataRoot) { Get-ChildItem -LiteralPath $DataRoot -File | Where-Object { $_.Name -match '^history\.db(?:-wal|-shm|\.backup\.db|\.pre-v\d+\.db)?$' } | ForEach-Object { Remove-Item -LiteralPath (Assert-Child $_.FullName $DataRoot) } }
        }
        Write-Output ('PASS uninstall history_preserved=' + (!$RemoveHistory)); return
    }
    if (Test-Path -LiteralPath $stateFile) {
        $installed = Get-Content -LiteralPath $stateFile -Raw | ConvertFrom-Json
        if ([version]$installed.version -gt [version]$manifest.version) { throw 'A newer version is already installed; downgrade refused.' }
    }
    Stop-OwnedProcesses
    $rollbackRoot = if ($DataRoot -eq $productionData) { Join-Path $env:USERPROFILE '.context-notes-native\backups' } else { Join-Path (Split-Path -Parent $DataRoot) 'backups' }
    New-Item -ItemType Directory -Path $rollbackRoot -Force | Out-Null
    $rollback = Join-Path $rollbackRoot ('install-recovery-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $rollback -Force | Out-Null
    $rollbackSafeToRemove = $false
    $database = Join-Path $DataRoot 'history.db'
    $hadDatabase = Test-Path -LiteralPath $database
    $databaseBackup = Join-Path $rollback 'history-before.db'
    $hadShortcut = Test-Path -LiteralPath $shortcut
    $hadState = Test-Path -LiteralPath $stateFile
    $hadCache = Test-Path -LiteralPath $cacheRoot
    if ($hadDatabase) { [void](Invoke-Helper @('--history-backup',$database,$databaseBackup)) }
    if ($hadCache) { Copy-Item -LiteralPath $cacheRoot -Destination (Join-Path $rollback 'cache') -Recurse }
    if (Test-Path -LiteralPath $shortcut) { Copy-Item -LiteralPath $shortcut -Destination (Join-Path $rollback 'startup.lnk') }
    if (Test-Path -LiteralPath $stateFile) { Copy-Item -LiteralPath $stateFile -Destination (Join-Path $rollback 'state.json') }
    $changedSource = $false
    try {
        if ($LegacyHistoryBackup) { [void](Invoke-Helper @('--history-backup',$LegacyHistoryBackup,$database)) }
        [void](Invoke-Helper @('--history-check', (Join-Path $DataRoot 'history.db')))
        if ($previous -and $previous.root -ne $marketplace) { [void](Invoke-Codex @('plugin','marketplace','remove','context-notes-native')); $changedSource = $true }
        if (!$previous -or $changedSource) { [void](Invoke-Codex @('plugin','marketplace','add',$marketplace)) }
        [void](Invoke-Codex @('plugin','add','context-notes@context-notes-native','--json'))
        $cachedPlugin = Join-Path $cacheRoot $manifest.version
        # Codex may reuse the same version cache. A verified preview rebuild still
        # needs fresh files; all owned processes were stopped above for this path.
        foreach ($file in Get-ChildItem -LiteralPath $plugin -Recurse -File -Force) {
            $relative = $file.FullName.Substring($plugin.Length).TrimStart('\')
            $dest = Assert-Child (Join-Path $cachedPlugin $relative) $cacheRoot
            New-Item -ItemType Directory -Path (Split-Path -Parent $dest) -Force | Out-Null
            Copy-Item -LiteralPath $file.FullName -Destination $dest -Force
        }
        foreach ($file in Get-ChildItem -LiteralPath $plugin -Recurse -File) {
            $relative = $file.FullName.Substring($plugin.Length).TrimStart('\'); $dest = Join-Path $cachedPlugin $relative
            if (!(Test-Path -LiteralPath $dest) -or (Get-FileHash -LiteralPath $file.FullName).Hash -ne (Get-FileHash -LiteralPath $dest).Hash) { throw 'Deployed plugin hash mismatch.' }
        }
        $hooks = Get-Content -LiteralPath (Join-Path $cachedPlugin 'hooks\hooks.json') -Raw | ConvertFrom-Json
        if (!$hooks.hooks.SessionStart) { throw 'Installed SessionStart hook missing.' }
        if ($InjectFailure) { throw 'Injected installation failure for rollback test.' }
        $binary = Join-Path $cachedPlugin 'bin\SelectionProbe.exe'; Set-Shortcut $binary
        if (!$SkipLaunch) {
            Start-Process -FilePath $binary -ArgumentList '--launch-watcher' -WindowStyle Hidden -Wait
            $watcherReady = $false
            $watcherDeadline = [DateTime]::UtcNow.AddSeconds(5)
            do {
                [void](& dotnet $dll --watcher-status 2>&1)
                if ($LASTEXITCODE -eq 0) { $watcherReady = $true; break }
                Start-Sleep -Milliseconds 200
            } while ([DateTime]::UtcNow -lt $watcherDeadline)
            if (!$watcherReady) { throw 'Context Notes command failed: --watcher-status' }
            [void](Invoke-Helper @('--relaunch-companion'))
        }
        @{version=$manifest.version;marketplace=$marketplace;cache=$cachedPlugin;data_root=$DataRoot} | ConvertTo-Json | Set-Content -LiteralPath $stateFile -Encoding UTF8
        $rollbackSafeToRemove = $true
        Write-Output ('PASS installation version=' + $manifest.version + ' hashes_verified=true hook_manifest_verified=true desktop_selection_requires_ui_check=true')
    } catch {
        $failure = $_
        try {
        if ($changedSource -or !$previous) {
            try { [void](Invoke-Codex @('plugin','remove','context-notes@context-notes-native','--json')) } catch { }
            try { [void](Invoke-Codex @('plugin','marketplace','remove','context-notes-native')) } catch { }
            if ($previous) { [void](Invoke-Codex @('plugin','marketplace','add',$previous.root)); [void](Invoke-Codex @('plugin','add','context-notes@context-notes-native','--json')) }
        }
        if ($InjectRollbackFailure) { throw 'Injected rollback failure in isolated installation test.' }
        if ($hadDatabase) { [void](Invoke-Helper @('--history-backup',$databaseBackup,$database)) }
        if ($hadCache) {
            New-Item -ItemType Directory -Path $cacheRoot -Force | Out-Null
            Copy-Item -Path (Join-Path $rollback 'cache\*') -Destination $cacheRoot -Recurse -Force
        }
        if (!$hadShortcut -and (Test-Path -LiteralPath $shortcut)) { Remove-Item -LiteralPath $shortcut }
        if (!$hadState -and (Test-Path -LiteralPath $stateFile)) { Remove-Item -LiteralPath $stateFile }
        if (Test-Path -LiteralPath (Join-Path $rollback 'startup.lnk')) { Copy-Item -LiteralPath (Join-Path $rollback 'startup.lnk') -Destination $shortcut -Force }
        if (Test-Path -LiteralPath (Join-Path $rollback 'state.json')) { Copy-Item -LiteralPath (Join-Path $rollback 'state.json') -Destination $stateFile -Force }
        if (!$SkipLaunch -and $previous) { Start-Process -FilePath (Join-Path $previous.root 'plugins\context-notes\bin\SelectionProbe.exe') -ArgumentList '--launch-watcher' -WindowStyle Hidden }
        $rollbackSafeToRemove = $true
        } catch {
            $recoveryError = [IO.IOException]::new("Installation rollback failed. Recovery backup preserved at $rollback", $_.Exception)
            $recoveryError.Data['RecoveryBackupPath'] = $rollback
            throw $recoveryError
        }
        throw $failure
    } finally {
        $ownedRollback = Assert-Child $rollback $rollbackRoot
        if ($rollbackSafeToRemove -and (Test-Path -LiteralPath $ownedRollback)) { Remove-Item -LiteralPath $ownedRollback -Recurse -Force }
        elseif (Test-Path -LiteralPath $ownedRollback) { Write-Warning "Recovery backup retained: $ownedRollback" }
    }
} finally { $env:CODEX_HOME = $previousCodexHome }
