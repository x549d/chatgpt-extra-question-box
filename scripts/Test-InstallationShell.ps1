[CmdletBinding()]
param([Parameter(Mandatory)][string]$PayloadRoot, [string]$CodexPath)
$ErrorActionPreference = 'Stop'
if ($PSEdition -ne 'Core') { throw 'Run this regression test in PowerShell 7.' }
$PayloadRoot = (Resolve-Path -LiteralPath $PayloadRoot).Path
$manager = Join-Path $PayloadRoot 'scripts\Manage-Installation.ps1'
$testRoot = [IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetTempPath()) ('ContextNotesNative-ShellTest-' + [Guid]::NewGuid().ToString('N'))))
$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
if (!$testRoot.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Test directory escaped the temporary root.' }
# Spaces also exercise the native process argument boundary.
$profile = Join-Path $testRoot 'isolated profile'
$data = Join-Path $testRoot 'isolated data'
$startup = Join-Path $testRoot 'isolated startup'
$inheritedPath = (Join-Path $PSHOME 'Modules') + ';' + $env:PSModulePath

function Invoke-InheritedWindowsPowerShell([string]$Action) {
    $arguments = @('-NoProfile','-NonInteractive','-ExecutionPolicy','Bypass','-File',$manager,
        '-Action',$Action,'-PayloadRoot',$PayloadRoot,'-SkipLaunch',
        '-ProfileRoot',$profile,'-DataRoot',$data,'-StartupRoot',$startup)
    if ($CodexPath) { $arguments += @('-CodexPath',$CodexPath) }
    if ($Action -eq 'Uninstall') { $arguments += '-RemoveHistory' }
    $info = [Diagnostics.ProcessStartInfo]::new()
    $info.FileName = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    # Do not invoke powershell.exe with &, which lets PowerShell 7 sanitize
    # PSModulePath. Model Setup's intermediate native process explicitly.
    $info.Environment['PSModulePath'] = $inheritedPath
    foreach ($argument in $arguments) { [void]$info.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::Start($info)
    try {
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        if (!$process.WaitForExit(120000)) { $process.Kill($true); throw 'Isolated deployment timed out.' }
        $output = $stdout.GetAwaiter().GetResult()
        $errorText = $stderr.GetAwaiter().GetResult()
        if ($process.ExitCode -ne 0) { throw "Inherited PSModulePath $Action failed: $errorText" }
        Write-Output $output.Trim()
    } finally { $process.Dispose() }
}

try {
    Invoke-InheritedWindowsPowerShell 'Install'
    if (!(Test-Path -LiteralPath (Join-Path $profile 'context-notes-installation.json')) -or
        !(Test-Path -LiteralPath (Join-Path $data 'history.db')) -or
        !(Test-Path -LiteralPath (Join-Path $startup 'Context Notes Native - Codex Startup.lnk'))) {
        throw 'Inherited module path installation did not create the isolated registration, database and shortcut.'
    }
    Write-Output 'PASS inherited PowerShell 7 module path: Windows PowerShell 5.1 install'
    Invoke-InheritedWindowsPowerShell 'Uninstall'
    if ((Test-Path -LiteralPath (Join-Path $data 'history.db')) -or
        (Test-Path -LiteralPath (Join-Path $profile 'context-notes-installation.json')) -or
        (Test-Path -LiteralPath (Join-Path $startup 'Context Notes Native - Codex Startup.lnk'))) {
        throw 'Inherited module path uninstall did not remove the isolated application state.'
    }
    Write-Output 'PASS inherited PowerShell 7 module path: Windows PowerShell 5.1 uninstall'
} finally {
    if (Test-Path -LiteralPath $testRoot) { Remove-Item -LiteralPath $testRoot -Recurse -Force }
}
