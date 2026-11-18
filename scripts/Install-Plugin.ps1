[CmdletBinding()]
param(
    # Skip `codex plugin marketplace add`, e.g. when the marketplace is already registered.
    [switch]$SkipMarketplace,
    [string]$ReleasePayload
)

# Builds the helper into the local Codex plugin, syncs the local plugin cache, and relaunches the companion.
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repo 'src\SelectionProbe\SelectionProbe.csproj'
$marketplaceRoot = Join-Path $repo 'codex-plugin'
$pluginRoot = Join-Path $marketplaceRoot 'plugins\context-notes'
$bin = Join-Path $pluginRoot 'bin'

# 1. Publish the helper into the plugin directory first.
$staging = Join-Path ([IO.Path]::GetTempPath()) ('ctx-notes-stage-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $staging -Force | Out-Null
try {
    if ($ReleasePayload) {
        $ReleasePayload = [IO.Path]::GetFullPath($ReleasePayload)
        $payloadManifest = Get-Content -LiteralPath (Join-Path $ReleasePayload 'codex-plugin\plugins\context-notes\.codex-plugin\plugin.json') -Raw | ConvertFrom-Json
        $projectManifest = Get-Content -LiteralPath (Join-Path $pluginRoot '.codex-plugin\plugin.json') -Raw | ConvertFrom-Json
        if ($payloadManifest.name -ne 'context-notes' -or $payloadManifest.version -ne $projectManifest.version) { throw 'Release payload ownership or version mismatch.' }
        $assemblyVersion = [Reflection.AssemblyName]::GetAssemblyName((Join-Path $ReleasePayload 'codex-plugin\plugins\context-notes\bin\SelectionProbe.dll')).Version
        if ((@($assemblyVersion.Major,$assemblyVersion.Minor,$assemblyVersion.Build) -join '.') -ne $payloadManifest.version) { throw 'Release assembly version does not match the plugin manifest.' }
        foreach ($entry in (Get-Content -LiteralPath (Join-Path $ReleasePayload 'SHA256.json') -Raw | ConvertFrom-Json)) {
            $full = [IO.Path]::GetFullPath((Join-Path $ReleasePayload $entry.path))
            if (!$full.StartsWith($ReleasePayload.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase) -or
                (Get-FileHash -LiteralPath $full -Algorithm SHA256).Hash -ne $entry.sha256) { throw 'Release payload integrity check failed.' }
        }
        Copy-Item -Path (Join-Path $ReleasePayload 'codex-plugin\plugins\context-notes\bin\*') -Destination $staging -Force
    } else {
        dotnet publish $project -c Release --no-self-contained -p:NuGetAudit=false -o $staging --nologo
        if ($LASTEXITCODE -ne 0) { throw 'Publishing the helper failed.' }
    }
    Get-ChildItem -LiteralPath $staging -Filter *.pdb | Remove-Item -Force

    $stagedDll = Join-Path $staging 'SelectionProbe.dll'
    $watcherStatus = & dotnet $stagedDll --watcher-status 2>&1 | Out-String
    $watcherRunning = ($LASTEXITCODE -eq 0 -and $watcherStatus -match 'desktop_watcher_running')

    # 2. Stop the running companion so it picks up the new build. Keep the interactive watcher running if active.
    [void](& dotnet $stagedDll --stop)
    $deadline = [DateTime]::UtcNow.AddSeconds(10)
    $compStopped = $false
    do {
        $compStatus = & dotnet $stagedDll --status 2>&1 | Out-String
        if ($LASTEXITCODE -eq 3) { $compStopped = $true; break }
        Start-Sleep -Milliseconds 150
    } while ([DateTime]::UtcNow -lt $deadline)
    if (!$compStopped) { throw 'Companion has not stopped; deployment files were not changed.' }

    if (!(Test-Path -LiteralPath $bin)) { New-Item -ItemType Directory -Path $bin -Force | Out-Null }
    Copy-Item -Path (Join-Path $staging '*') -Destination $bin -Force

    # 3. Sync the active Codex plugin cache directory if present.
    $manifest = Get-Content -LiteralPath (Join-Path $pluginRoot '.codex-plugin\plugin.json') -Raw | ConvertFrom-Json
    $profileRoot = if ($env:CODEX_HOME) { $env:CODEX_HOME } else { Join-Path $env:USERPROFILE '.codex' }
    $cachedBin = Join-Path $profileRoot ("plugins\cache\context-notes-native\context-notes\" + $manifest.version + "\bin")
    if (Test-Path -LiteralPath $cachedBin) {
        Get-ChildItem -LiteralPath $cachedBin -Filter '*.bak.*' -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue
        foreach ($file in Get-ChildItem -LiteralPath $staging -File) {
            $dest = Join-Path $cachedBin $file.Name
            try {
                Copy-Item -LiteralPath $file.FullName -Destination $dest -Force -ErrorAction Stop
            } catch {
                $bak = "$dest.bak." + [Guid]::NewGuid().ToString('N').Substring(0, 6)
                Move-Item -LiteralPath $dest -Destination $bak -Force
                Copy-Item -LiteralPath $file.FullName -Destination $dest -Force
            }
        }
    }

    if ($watcherRunning) {
        [void](& dotnet $stagedDll --relaunch-companion)
    }
} finally {
    $fullStage = [IO.Path]::GetFullPath($staging)
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (!$fullStage.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -or ![IO.Path]::GetFileName($fullStage).StartsWith('ctx-notes-stage-')) { throw 'Refusing staging cleanup outside the owned temporary directory.' }
    Remove-Item -LiteralPath $fullStage -Recurse -Force -ErrorAction SilentlyContinue
}

# 4. Register the local marketplace through the documented Codex CLI if requested.
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

Write-Output @"

Built & deployed: $bin
Diagnostics (metadata only): $env:LOCALAPPDATA\ContextNotesNative\logs
"@
