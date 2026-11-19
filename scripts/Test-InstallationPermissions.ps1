[CmdletBinding()]
param([string]$InstallerPath)
$ErrorActionPreference = 'Stop'
$checks = 0
$nonce = [Guid]::NewGuid().ToString('N')
foreach ($view in @([Microsoft.Win32.RegistryView]::Registry32,[Microsoft.Win32.RegistryView]::Registry64)) {
    $root = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::CurrentUser,$view)
    $key = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\ContextNotesNative.PermissionProbe-' + $nonce + '-' + $view
    $created = $false
    try {
        $existing = $root.OpenSubKey($key)
        if ($existing) { $existing.Dispose(); throw 'Unexpected pre-existing probe; nothing removed.' }
        $probe = $root.CreateSubKey($key); $created = $true
        try { $probe.SetValue('Probe','metadata-only'); if ($probe.GetValue('Probe') -ne 'metadata-only') { throw 'Current-user registry readback failed.' } }
        finally { $probe.Dispose() }
    } finally {
        if ($created) { $root.DeleteSubKey($key,$false) }
        $root.Dispose()
    }
    $checks++; Write-Output ('PASS permissions HKCU ' + $view + ' write readback and owned-probe cleanup')
}
$programRoot = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'Programs\ContextNotesNative'))
$directory = [IO.Path]::GetFullPath((Join-Path $programRoot ('permission-probe-' + $nonce)))
if (!$directory.StartsWith($programRoot.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Probe path escaped the owned application directory.' }
$file = Join-Path $directory 'probe.txt'
$created = $false
try {
    if (Test-Path -LiteralPath $directory) { throw 'Unexpected existing probe directory; nothing removed.' }
    New-Item -ItemType Directory -Path $directory | Out-Null; $created=$true
    [IO.File]::WriteAllText($file,'metadata-only')
    if ([IO.File]::ReadAllText($file) -ne 'metadata-only') { throw 'Current-user program directory readback failed.' }
} finally {
    if ($created) {
        if (Test-Path -LiteralPath $file) { Remove-Item -LiteralPath $file }
        Remove-Item -LiteralPath $directory
    }
}
$checks++; Write-Output 'PASS permissions current-user program directory write readback and owned-probe cleanup'
$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$tempDirectory = [IO.Path]::GetFullPath((Join-Path $tempRoot ('ContextNotesNative.PermissionProbe-' + $nonce)))
if (!$tempDirectory.StartsWith($tempRoot.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Temp probe escaped the configured temporary directory.' }
$tempFile = Join-Path $tempDirectory 'probe.txt'
$created = $false
try {
    if (Test-Path -LiteralPath $tempDirectory) { throw 'Unexpected existing Temp probe; nothing removed.' }
    New-Item -ItemType Directory -Path $tempDirectory | Out-Null; $created=$true
    [IO.File]::WriteAllText($tempFile,'metadata-only')
    if ([IO.File]::ReadAllText($tempFile) -ne 'metadata-only') { throw 'Configured temporary directory readback failed.' }
} finally {
    if ($created) {
        if (Test-Path -LiteralPath $tempFile) { Remove-Item -LiteralPath $tempFile }
        Remove-Item -LiteralPath $tempDirectory
    }
}
$checks++; Write-Output 'PASS permissions configured Temp directory write readback and owned-probe cleanup'
$groups = & whoami /groups 2>$null | Out-String
if ($groups -match 'S-1-16-4096') { throw 'Current process is running at Low Mandatory Integrity Level (S-1-16-4096); Temp and HKCU writes will be denied.' }
$checks++; Write-Output 'PASS permissions current process integrity level is Medium or higher'
if ($InstallerPath) {
    $resolvedInstaller = [IO.Path]::GetFullPath($InstallerPath)
    if (!(Test-Path -LiteralPath $resolvedInstaller)) { throw "Installer not found: $resolvedInstaller" }
    $installerAcl = & icacls $resolvedInstaller 2>$null | Out-String
    if ($installerAcl -match 'Low Mandatory Level') { throw "Installer '$resolvedInstaller' has an inherited Low Mandatory Level SACL; Windows MIC will downgrade it to Low Integrity on launch." }
    $checks++; Write-Output 'PASS permissions installer executable has no Low Mandatory Level SACL'
}
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
try { $principal=[Security.Principal.WindowsPrincipal]::new($identity); $admin=$principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator) }
finally { $identity.Dispose() }
Write-Output ('PASS permission_checks=' + $checks + ' effective_admin=' + $admin + ' exe_installation_verified=false')
