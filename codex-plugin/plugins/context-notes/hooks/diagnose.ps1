$ErrorActionPreference = 'Stop'
$started = [DateTimeOffset]::Now.ToString('o')
$eventName = 'unknown'
$source = 'unknown'
$code = 0
try {
    $reader = [IO.StreamReader]::new([Console]::OpenStandardInput())
    $pending = $reader.ReadToEndAsync()
    if (!$pending.Wait(500)) {
        $code = 124
    } else {
        $payload = ConvertFrom-Json -InputObject $pending.GetAwaiter().GetResult()
        if ($payload.hook_event_name -is [string] -and $payload.hook_event_name -ceq 'SessionStart') {
            $eventName = 'SessionStart'
            if ($payload.source -is [string] -and
                @('startup', 'resume', 'clear', 'compact', 'fork') -ccontains $payload.source) {
                $source = $payload.source
            }
        }
        if ($eventName -eq 'unknown' -or $source -eq 'unknown') { $code = 2 }
    }
} catch {
    $code = 2
}

try {
    $directory = Join-Path $env:LOCALAPPDATA 'ContextNotesNative\logs'
    [void][IO.Directory]::CreateDirectory($directory)
    $file = Join-Path $directory 'hook-diagnostic.log'
    $entry = [ordered]@{
        timestamp = $started
        hook_event_name = $eventName
        source = $source
        command_started = $true
        command_exit_code = $code
    } | ConvertTo-Json -Compress
    for ($attempt = 1; $attempt -le 4; $attempt++) {
        try {
            [IO.File]::AppendAllText($file, $entry + [Environment]::NewLine)
            break
        } catch {
            if ($attempt -eq 4 -or $_.Exception.InnerException -isnot [IO.IOException]) { throw }
            Start-Sleep -Milliseconds 25
        }
    }
} catch {
    exit 3
}
exit $code
