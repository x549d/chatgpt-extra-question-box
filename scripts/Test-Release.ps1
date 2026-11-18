[CmdletBinding()]
param([Parameter(Mandatory)][string]$PayloadRoot)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$PayloadRoot = [IO.Path]::GetFullPath($PayloadRoot)
$artifactRoot = Join-Path $repo 'artifacts'
$version = (Get-Content -LiteralPath (Join-Path $PayloadRoot 'codex-plugin\plugins\context-notes\.codex-plugin\plugin.json') -Raw | ConvertFrom-Json).version
$checks = 0
function Require([bool]$Condition, [string]$Name) { if (!$Condition) { throw ('FAIL release ' + $Name) }; $script:checks++; Write-Output ('PASS release ' + $Name) }
$setup = Join-Path $artifactRoot "ContextNotesNative-$version-setup.exe"
$portable = Join-Path $artifactRoot "ContextNotesNative-$version-portable.zip"
Require ((Get-Item -LiteralPath $setup).Length -gt 0 -and (Get-Item -LiteralPath $portable).Length -gt 0) 'versioned artifacts exist'
$sumLines = @(Get-Content -LiteralPath (Join-Path $artifactRoot 'SHA256SUMS'))
$validSums = $sumLines.Count -eq 2
foreach ($line in $sumLines) {
    if ($line -notmatch '^([A-Fa-f0-9]{64})  ([^\\/]+)$') { $validSums = $false; continue }
    $expected=$Matches[1]; $name=$Matches[2]
    if ((Get-FileHash -LiteralPath (Join-Path $artifactRoot $name)).Hash -ne $expected) { $validSums=$false }
}
Require $validSums 'SHA256SUMS matches both artifacts'
$assembly = [Reflection.AssemblyName]::GetAssemblyName((Join-Path $PayloadRoot 'codex-plugin\plugins\context-notes\bin\SelectionProbe.dll')).Version
Require ((@($assembly.Major,$assembly.Minor,$assembly.Build) -join '.') -eq $version) 'assembly and manifest version match'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::OpenRead($portable)
try {
    $entries = @($zip.Entries | Where-Object { $_.Name })
    $names = @($entries | ForEach-Object { $_.FullName.Replace('\','/') })
    $required = @('README.md','LICENSE','PRIVACY.md','CHANGELOG.md','release.json','SHA256.json','scripts/Manage-Installation.ps1','codex-plugin/.agents/plugins/marketplace.json','codex-plugin/plugins/context-notes/.codex-plugin/plugin.json')
    Require (@($required | Where-Object { $_ -notin $names }).Count -eq 0) 'portable contains registration and user documents'
    Require (@($names | Where-Object { $_ -match '(^/|^[A-Za-z]:|(^|/)\.\.(/|$))' }).Count -eq 0) 'archive paths stay within extraction root'
    Require (@($names | Where-Object { $_ -match '(?i)(\.(db|sqlite|log|pdb|cs|xaml|trx)(-|\.|$)|(^|/)(obj|src|tests|TestResults)/|PHASE-[0-9]|PROJECT-SPEC|auth\.json|config\.toml)' }).Count -eq 0) 'no user history credentials or development files'
    $manifest = Get-Content -LiteralPath (Join-Path $PayloadRoot 'SHA256.json') -Raw | ConvertFrom-Json
    $validPayload = $true
    foreach ($file in $manifest) {
        $entry = $entries | Where-Object { $_.FullName.Replace('\','/') -eq $file.path.Replace('\','/') } | Select-Object -First 1
        if (!$entry) { $validPayload=$false; continue }
        $stream=$entry.Open(); $sha=[Security.Cryptography.SHA256]::Create()
        try { $hash=[BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','') } finally { $sha.Dispose(); $stream.Dispose() }
        if ($hash -ne $file.sha256) { $validPayload=$false }
    }
    Require ($validPayload -and $manifest.Count -eq $entries.Count - 1) 'every portable payload file matches manifest'
} finally { $zip.Dispose() }
$signature = Get-AuthenticodeSignature -LiteralPath $setup
Require ($signature.Status -eq 'NotSigned') 'unsigned preview is explicitly identified'
$release = Get-Content -LiteralPath (Join-Path $PayloadRoot 'release.json') -Raw | ConvertFrom-Json
Require ($release.version -eq $version -and $release.channel -eq 'preview' -and !$release.source_dirty -and !$release.formal_release_ready -and $release.source_commit -match '^[0-9a-f]{40}$') 'clean preview provenance and release gate recorded'
Write-Output "PASS release_checks=$checks formal_release_ready=false signature=NotSigned"
