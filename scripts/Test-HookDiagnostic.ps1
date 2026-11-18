$ErrorActionPreference = 'Stop'
$plugin = Join-Path (Split-Path -Parent $PSScriptRoot) 'codex-plugin\plugins\context-notes'
$manifest = Get-Content -LiteralPath (Join-Path $plugin 'hooks\diagnostic-only.json') -Raw | ConvertFrom-Json
$group = $manifest.hooks.SessionStart[0]
$handler = $group.hooks[0]
$cases = @(
    @{ Input = '{"hook_event_name":"SessionStart","source":"startup","session_id":"fixture-metadata","transcript_path":"fixture-metadata"}'; Source = 'startup'; Event = 'SessionStart'; Exit = 0 },
    @{ Input = '{"hook_event_name":"SessionStart","source":"resume","content":"fixture-metadata"}'; Source = 'resume'; Event = 'SessionStart'; Exit = 0 },
    @{ Input = '{"hook_event_name":"SessionStart","source":"fixture-metadata"}'; Source = 'unknown'; Event = 'SessionStart'; Exit = 2 },
    @{ Input = '{"hook_event_name":"fixture-metadata","source":"resume"}'; Source = 'unknown'; Event = 'unknown'; Exit = 2 },
    @{ Input = 'not JSON fixture-metadata'; Source = 'unknown'; Event = 'unknown'; Exit = 2 },
    @{ Input = ''; Source = 'unknown'; Event = 'unknown'; Exit = 124; KeepInputOpen = $true }
)

function Invoke-Fixture([string]$payload, [string]$localData, [switch]$KeepInputOpen) {
    $executable = Join-Path $env:SystemRoot 'System32\cmd.exe'
    $arguments = $handler.commandWindows.Substring('cmd.exe '.Length)
    $options = [Diagnostics.ProcessStartInfo]::new($executable, $arguments)
    $options.WorkingDirectory = $localData | Split-Path -Parent
    $options.UseShellExecute = $false
    $options.CreateNoWindow = $true
    $options.RedirectStandardInput = $true
    $options.RedirectStandardOutput = $true
    $options.RedirectStandardError = $true
    $options.EnvironmentVariables['LOCALAPPDATA'] = $localData
    $options.EnvironmentVariables['PLUGIN_ROOT'] = $fixturePlugin

    $process = [Diagnostics.Process]::Start($options)
    try {
        $process.StandardInput.Write($payload)
        if (!$KeepInputOpen) { $process.StandardInput.Close() }
        if (!$process.WaitForExit(5000)) {
            $process.Kill()
            throw 'Diagnostic fixture exceeded its timeout.'
        }
        if ($process.StandardOutput.ReadToEnd() -or $process.StandardError.ReadToEnd()) {
            throw 'Diagnostic emitted console output.'
        }
        return $process.ExitCode
    } finally {
        $process.Dispose()
    }
}

if ('startup' -cnotmatch $group.matcher -or 'resume' -cnotmatch $group.matcher -or
    'compact' -cmatch $group.matcher -or !$handler.async) {
    throw 'Expected a background diagnostic for startup and resume only.'
}
Write-Output 'PASS diagnostic matcher and background execution'

$temp = Join-Path ([IO.Path]::GetTempPath()) ('Context Notes Hook ' + [Guid]::NewGuid().ToString('N'))
$fixturePlugin = Join-Path $temp 'installed plugin'
$fixtureHooks = Join-Path $fixturePlugin 'hooks'
$localData = Join-Path $temp 'profile'
$blocked = Join-Path $temp 'file-instead-of-profile'
$log = Join-Path $localData 'ContextNotesNative\logs\hook-diagnostic.log'
try {
    [void][IO.Directory]::CreateDirectory($fixtureHooks)
    foreach ($file in 'diagnose.cmd', 'diagnose.ps1') {
        Copy-Item -LiteralPath (Join-Path $plugin ('hooks\' + $file)) -Destination $fixtureHooks
    }
    foreach ($case in $cases) {
        $actual = Invoke-Fixture $case.Input $localData -KeepInputOpen:($case.KeepInputOpen -eq $true)
        if ($actual -ne $case.Exit) { throw "Expected exit $($case.Exit) for $($case.Source), returned $actual." }
        $record = Get-Content -LiteralPath $log -Tail 1 | ConvertFrom-Json
        if ($record.source -cne $case.Source -or $record.hook_event_name -cne $case.Event -or
            !$record.command_started -or $record.command_exit_code -ne $case.Exit -or
            @($record.PSObject.Properties).Count -ne 5 -or ![DateTimeOffset]::Parse($record.timestamp)) {
            throw 'Unexpected diagnostic record.'
        }
        Write-Output ('PASS diagnostic event=' + $case.Event + ' source=' + $case.Source + ' exit=' + $case.Exit)
    }
    if ((Get-Content -LiteralPath $log -Raw).Contains('fixture-metadata')) { throw 'Fixture payload leaked into the log.' }
    Write-Output 'PASS unrelated payload fields omitted'

    [IO.File]::WriteAllText($blocked, '')
    if ((Invoke-Fixture $cases[0].Input $blocked) -ne 3) { throw 'Expected an explicit diagnostic failure with an invalid log directory.' }
    Write-Output 'PASS diagnostic log write failure returns a nonzero exit'
} finally {
    foreach ($file in $log, $blocked, (Join-Path $fixtureHooks 'diagnose.cmd'), (Join-Path $fixtureHooks 'diagnose.ps1')) {
        if (Test-Path -LiteralPath $file) { Remove-Item -LiteralPath $file }
    }
    foreach ($directory in (Split-Path $log -Parent), (Join-Path $localData 'ContextNotesNative'), $localData, $fixtureHooks, $fixturePlugin, $temp) {
        if (Test-Path -LiteralPath $directory) { [IO.Directory]::Delete($directory) }
    }
}
