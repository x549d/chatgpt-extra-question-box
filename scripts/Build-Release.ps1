[CmdletBinding()]
param([string]$Compiler, [switch]$SkipTests, [string]$OutputDir)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$artifacts = if ($OutputDir) { [IO.Path]::GetFullPath($OutputDir) } else { Join-Path $repo 'artifacts' }
$stage = Join-Path $artifacts ('payload-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage -Force | Out-Null
if (!$SkipTests) {
    dotnet test (Join-Path $repo 'ChatGptContextNotesNative.sln') -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Release tests failed.' }
}
$marketplace = Join-Path $stage 'codex-plugin'
New-Item -ItemType Directory -Path $marketplace -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $repo 'codex-plugin\.agents') -Destination (Join-Path $marketplace '.agents') -Recurse
$plugin = Join-Path $marketplace 'plugins\context-notes'
New-Item -ItemType Directory -Path $plugin -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $repo 'codex-plugin\plugins\context-notes\.codex-plugin') -Destination (Join-Path $plugin '.codex-plugin') -Recurse
Copy-Item -LiteralPath (Join-Path $repo 'codex-plugin\plugins\context-notes\hooks') -Destination (Join-Path $plugin 'hooks') -Recurse
$version = (Get-Content -LiteralPath (Join-Path $plugin '.codex-plugin\plugin.json') -Raw | ConvertFrom-Json).version
dotnet publish (Join-Path $repo 'src\SelectionProbe\SelectionProbe.csproj') -c Release -r win-x64 --no-self-contained -o (Join-Path $plugin 'bin')
if ($LASTEXITCODE -ne 0) { throw 'Release publish failed.' }
Get-ChildItem -LiteralPath (Join-Path $plugin 'bin') -Filter *.pdb | Remove-Item
New-Item -ItemType Directory -Path (Join-Path $stage 'scripts') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Manage-Installation.ps1') -Destination (Join-Path $stage 'scripts\Manage-Installation.ps1')
New-Item -ItemType Directory -Path (Join-Path $stage 'docs') -Force | Out-Null
foreach ($name in @('INSTALLATION.md','USER-GUIDE.md','TROUBLESHOOTING.md','SECURITY.md','SIGNING-PLAN.md','THIRD-PARTY-NOTICES.md')) {
    Copy-Item -LiteralPath (Join-Path $repo ('docs\' + $name)) -Destination (Join-Path $stage 'docs')
}
Copy-Item -LiteralPath (Join-Path $repo 'docs\RELEASE-README.md') -Destination (Join-Path $stage 'README.md')
foreach ($name in @('LICENSE','PRIVACY.md','CHANGELOG.md')) { Copy-Item -LiteralPath (Join-Path $repo $name) -Destination $stage }
$revision = (& git -c "safe.directory=$($repo.Replace('\','/'))" -C $repo rev-parse HEAD | Out-String).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Cannot record release source revision.' }
$dirty = ![string]::IsNullOrWhiteSpace((& git -c "safe.directory=$($repo.Replace('\','/'))" -C $repo status --porcelain | Out-String))
@{version=$version;channel='preview';source_commit=$revision;source_dirty=$dirty;signature='NotSigned';formal_release_ready=$false} |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $stage 'release.json') -Encoding UTF8
$hashes = Get-ChildItem -LiteralPath $stage -Recurse -File | ForEach-Object { @{path=$_.FullName.Substring($stage.Length+1);sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash} }
$hashes | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $stage 'SHA256.json') -Encoding UTF8
$portable = Join-Path $artifacts "ContextNotesNative-$version-portable.zip"
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $portable -Force
(Get-FileHash -LiteralPath $portable -Algorithm SHA256).Hash | Set-Content -LiteralPath ($portable + '.sha256') -Encoding ASCII
if (!$Compiler) { foreach ($candidate in @('<local-inno-setup>\ISCC.exe',"${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe","$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe")) { if (Test-Path -LiteralPath $candidate) { $Compiler = $candidate; break } } }
if (!$Compiler) { throw 'Inno Setup compiler missing. Install the official compiler, then pass -Compiler path\ISCC.exe.' }
$compilerAcl = & icacls $Compiler 2>$null | Out-String
$artifactsAcl = & icacls $artifacts 2>$null | Out-String
if ($compilerAcl -match 'Low Mandatory Level' -and $artifactsAcl -notmatch 'Low Mandatory Level') {
    throw "Compiler '$Compiler' carries a Low Mandatory Level SACL and cannot write to Medium-integrity output '$artifacts'. Pass -Compiler pointing to an ISCC.exe in a normal Medium-integrity folder."
}
& $Compiler "/DPayloadDir=$stage" "/DProductVersion=$version" "/O$artifacts" (Join-Path $repo 'installer\ContextNotes.iss')
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup compilation failed.' }
$setup = Join-Path $artifacts "ContextNotesNative-$version-setup.exe"
$icaclsOut = & icacls $setup 2>$null | Out-String
if ($icaclsOut -match 'Low Mandatory Level') {
    Write-Warning "Output installer '$setup' inherited a Low Mandatory Integrity Level SACL from its parent directory. Windows MIC will launch executables directly inside that folder at Low Integrity (blocking Temp/HKCU writes). Copy the EXE to a normal user directory (e.g. Downloads) or pass -OutputDir to a Medium-integrity folder before running it locally."
}
(Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash | Set-Content -LiteralPath ($setup + '.sha256') -Encoding ASCII
@($setup,$portable) | ForEach-Object { (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash + '  ' + [IO.Path]::GetFileName($_) } |
    Set-Content -LiteralPath (Join-Path $artifacts 'SHA256SUMS') -Encoding ASCII
Write-Output "PASS release_setup=$setup payload=$stage"
