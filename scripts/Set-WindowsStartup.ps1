[CmdletBinding()]
param([switch]$Disable)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$manifest = Get-Content -LiteralPath (Join-Path $repo 'codex-plugin\plugins\context-notes\.codex-plugin\plugin.json') -Raw | ConvertFrom-Json
$profileRoot = if ($env:CODEX_HOME) { $env:CODEX_HOME } else { Join-Path $env:USERPROFILE '.codex' }
$cache = [IO.Path]::GetFullPath((Join-Path $profileRoot 'plugins\cache\context-notes-native\context-notes'))
$binary = Join-Path $cache ($manifest.version + '\bin\SelectionProbe.exe')
$startup = [Environment]::GetFolderPath([Environment+SpecialFolder]::Startup)
if (!$startup) { throw 'The current user has no Windows startup directory.' }
$shortcutPath = Join-Path $startup 'Context Notes Native - Codex Startup.lnk'
$shell = New-Object -ComObject WScript.Shell
$existing = $null
if (Test-Path -LiteralPath $shortcutPath) {
    $existing = $shell.CreateShortcut($shortcutPath)
    if ($existing.Arguments -ne '--watch-codex' -or
        !$existing.TargetPath.StartsWith($cache + '\', [StringComparison]::OrdinalIgnoreCase) -or
        ![IO.Path]::GetFileName($existing.TargetPath).Equals('SelectionProbe.exe', [StringComparison]::OrdinalIgnoreCase)) {
        throw 'The Windows startup shortcut is not owned by Context Notes.'
    }
}

if ($Disable) {
    if ($existing) {
        if (Test-Path -LiteralPath $existing.TargetPath -PathType Leaf) {
            Start-Process -FilePath $existing.TargetPath -ArgumentList '--stop-watcher' -WindowStyle Hidden -Wait
        }
        Remove-Item -LiteralPath $shortcutPath
    }
    Write-Output 'Context Notes Windows startup disabled.'
    return
}

if (!(Test-Path -LiteralPath $binary -PathType Leaf)) { throw 'Install the Context Notes plugin build before enabling Windows startup.' }
$assembly = [IO.Path]::ChangeExtension($binary, '.dll')
$check = @(& dotnet $assembly --watcher-status)
$checkCode = $LASTEXITCODE
if ($checkCode -notin 0, 3) { throw 'The installed build does not support Windows startup.' }

if ($existing -and $existing.TargetPath -ne $binary -and (Test-Path -LiteralPath $existing.TargetPath -PathType Leaf)) {
    Start-Process -FilePath $existing.TargetPath -ArgumentList '--stop-watcher' -WindowStyle Hidden -Wait
}

$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $binary
$shortcut.Arguments = '--watch-codex'
$shortcut.WorkingDirectory = Split-Path -Parent $binary
$shortcut.Description = 'Starts Context Notes when the signed Codex desktop app becomes active.'
$shortcut.Save()

try {
    Start-Process -FilePath $binary -ArgumentList '--watch-codex' -WindowStyle Hidden
    $deadline = [DateTime]::UtcNow.AddSeconds(6)
    do {
        Start-Sleep -Milliseconds 150
        $state = @(& dotnet $assembly --watcher-status)
        $status = $LASTEXITCODE
        if ($status -eq 0 -and $state[0] -eq 'desktop_watcher_running') {
            Write-Output 'Context Notes Windows startup enabled; watcher running.'
            return
        }
    } while ([DateTime]::UtcNow -lt $deadline)
    throw 'The startup watcher did not become ready.'
} catch {
    if (!$existing) { Remove-Item -LiteralPath $shortcutPath }
    throw
}
