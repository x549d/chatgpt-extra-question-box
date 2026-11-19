[CmdletBinding()]
param([Parameter(Mandatory)][string]$PayloadRoot, [string]$ArtifactDir)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$PayloadRoot = [IO.Path]::GetFullPath($PayloadRoot)
$artifactRoot = if ($ArtifactDir) { [IO.Path]::GetFullPath($ArtifactDir) } else { Join-Path $repo 'artifacts' }
$pluginManifest = Get-Content -LiteralPath (Join-Path $PayloadRoot 'codex-plugin\plugins\context-notes\.codex-plugin\plugin.json') -Raw | ConvertFrom-Json
$version = $pluginManifest.version
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
    $required = @('README.md','LICENSE','PRIVACY.md','CHANGELOG.md','release.json','SHA256.json','scripts/Manage-Installation.ps1','docs/THIRD-PARTY-NOTICES.md','codex-plugin/.agents/plugins/marketplace.json','codex-plugin/plugins/context-notes/.codex-plugin/plugin.json')
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
    $validStage = $true
    foreach ($file in $manifest) {
        $full = [IO.Path]::GetFullPath((Join-Path $PayloadRoot $file.path))
        if (!$full.StartsWith($PayloadRoot.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase)) { $validStage=$false; continue }
        if ((Get-FileHash -LiteralPath $full).Hash -ne $file.sha256) { $validStage=$false }
    }
    Require $validStage 'staging files also match the archive manifest'
} finally { $zip.Dispose() }
$signature = Get-AuthenticodeSignature -LiteralPath $setup
Require ($signature.Status -eq 'NotSigned') 'unsigned preview is explicitly identified'
$runtimeSignatures = @('SelectionProbe.exe','SelectionProbe.dll') | ForEach-Object { Get-AuthenticodeSignature -LiteralPath (Join-Path $PayloadRoot ('codex-plugin\plugins\context-notes\bin\'+$_)) }
Require (@($runtimeSignatures | Where-Object {$_.Status -ne 'NotSigned'}).Count -eq 0) 'runtime unsigned status is explicitly identified'
$licenseText = Get-Content -LiteralPath (Join-Path $PayloadRoot 'LICENSE') -Raw
Require ($licenseText.TrimStart().StartsWith('MIT License') -and $pluginManifest.license -eq 'MIT') 'MIT license matches root LICENSE and plugin manifest'
$release = Get-Content -LiteralPath (Join-Path $PayloadRoot 'release.json') -Raw | ConvertFrom-Json
Require ($release.version -eq $version -and $release.license -eq 'MIT' -and $release.channel -eq 'public-unsigned' -and !$release.source_dirty -and $release.signature -eq 'NotSigned' -and $release.source_commit -match '^[0-9a-f]{40}$') 'clean public-unsigned provenance and MIT metadata recorded'
$revision = (& git -c "safe.directory=$($repo.Replace('\','/'))" -C $repo rev-parse --verify ($release.source_commit+'^{commit}') 2>$null | Out-String).Trim()
Require ($LASTEXITCODE -eq 0 -and $revision -eq $release.source_commit) 'recorded source commit exists in repository'
Write-Output "PASS release_checks=$checks license=MIT channel=public-unsigned signature=NotSigned"
