[CmdletBinding()]
param(
    # Skip `codex plugin marketplace add`, e.g. when the marketplace is already registered.
    [switch]$SkipMarketplace
)

# Builds the helper into the local Codex plugin and registers the plugin's local marketplace with Codex.
# Run from a normal (interactive) Windows PowerShell session of the user who uses Codex.
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repo 'src\SelectionProbe\SelectionProbe.csproj'
$marketplaceRoot = Join-Path $repo 'codex-plugin'
$pluginRoot = Join-Path $marketplaceRoot 'plugins\context-notes'
$bin = Join-Path $pluginRoot 'bin'

# 1. Stop a running helper so its files are not in use (single instance per user session).
$running = Get-Process SelectionProbe -ErrorAction SilentlyContinue
if ($running) {
    $stopper = @($running | Where-Object { $_.Path } | Select-Object -First 1)[0].Path
    if ($stopper) {
        Start-Process -FilePath $stopper -ArgumentList '--stop-watcher' -WindowStyle Hidden -Wait
        Start-Process -FilePath $stopper -ArgumentList '--stop' -WindowStyle Hidden -Wait
    }
    $deadline = [DateTime]::UtcNow.AddSeconds(10)
    while ((Get-Process SelectionProbe -ErrorAction SilentlyContinue) -and [DateTime]::UtcNow -lt $deadline) {
        Start-Sleep -Milliseconds 200
    }
    if (Get-Process SelectionProbe -ErrorAction SilentlyContinue) { throw 'The running Context Notes helper did not stop.' }
}

# 2. Publish the helper into the plugin (framework-dependent, .NET 10 Desktop Runtime required).
if (Test-Path -LiteralPath $bin) { Remove-Item -LiteralPath $bin -Recurse -Force }
dotnet publish $project -c Release --no-self-contained -p:NuGetAudit=false -o $bin --nologo
if ($LASTEXITCODE -ne 0) { throw 'Publishing the helper failed.' }
Get-ChildItem -LiteralPath $bin -Filter *.pdb | Remove-Item -Force

# 3. Register the local marketplace through the documented Codex CLI.
if (-not $SkipMarketplace) {
    $codex = (Get-Command codex -ErrorAction SilentlyContinue).Source
    if (-not $codex) {
        $codex = Get-ChildItem (Join-Path $env:LOCALAPPDATA 'OpenAI\Codex\bin') -Recurse -Filter codex.exe -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTime -Descending | Select-Object -First 1 -ExpandProperty FullName
    }
    if (-not $codex) { throw 'Codex CLI was not found. Add the marketplace manually: codex plugin marketplace add "' + $marketplaceRoot + '"' }
    $listed = & $codex plugin marketplace list 2>&1 | Out-String
    if ($listed -match 'context-notes-native') {
        Write-Output 'Marketplace context-notes-native is already registered.'
    } else {
        & $codex plugin marketplace add $marketplaceRoot
        if ($LASTEXITCODE -ne 0) { throw 'codex plugin marketplace add failed.' }
    }
}

# 4. Remove Start menu shortcuts left by the earlier, unaccepted Install-Companion.ps1 experiment.
$oldMenu = Join-Path ([Environment]::GetFolderPath('Programs')) 'Context Notes Native'
if (Test-Path -LiteralPath (Join-Path $oldMenu 'Context Notes Companion.lnk')) {
    Remove-Item -LiteralPath $oldMenu -Recurse -Force
    Write-Output 'Removed old Start menu shortcuts from the earlier experiment.'
}

Write-Output @"

Built: $bin
Next steps in the Codex desktop app:
  1. Fully quit and reopen Codex.
  2. Plugins > source "Context Notes (local)" > 划词追问 Context Notes > Install.
  3. Settings > Hooks (or "Review hooks" in the composer) > trust the Context Notes SessionStart hook.
  4. Start a new chat or reopen one; the helper starts in the background.
Diagnostics (metadata only): $env:LOCALAPPDATA\ContextNotesNative\logs
"@
