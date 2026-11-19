[CmdletBinding()]
param()

# Stop Windows startup and the helper before uninstalling the plugin from Codex > Plugins.
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'Set-WindowsStartup.ps1') -Disable
$helper = Get-Process SelectionProbe -ErrorAction SilentlyContinue | Where-Object { $_.Path } | Select-Object -First 1
if ($helper) { Start-Process -FilePath $helper.Path -ArgumentList '--stop' -WindowStyle Hidden -Wait }

$codex = (Get-Command codex -ErrorAction SilentlyContinue).Source
if (-not $codex) {
    $codex = Get-ChildItem (Join-Path $env:LOCALAPPDATA 'OpenAI\Codex\bin') -Recurse -Filter codex.exe -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1 -ExpandProperty FullName
}
if ($codex) { & $codex plugin marketplace remove context-notes-native }
Write-Output 'Context Notes helper stopped and local marketplace removed. Runtime logs remain in %USERPROFILE%\.context-notes-native\logs; legacy files are preserved.'
