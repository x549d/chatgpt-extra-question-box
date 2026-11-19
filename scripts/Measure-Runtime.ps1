[CmdletBinding()]
param(
    [ValidateRange(1,120)][int]$DurationMinutes = 60,
    [ValidateRange(5,60)][int]$IntervalSeconds = 30,
    [string]$OutputRoot,
    [string]$BaselineDatabase,
    [string]$DatabasePath = (Join-Path $env:USERPROFILE '.context-notes-native\data\history.db')
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
if (!$OutputRoot) { $OutputRoot = Join-Path $repo ('artifacts\runtime-' + (Get-Date -Format yyyyMMdd-HHmmss)) }
$OutputRoot = [IO.Path]::GetFullPath($OutputRoot)
New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null
$database = [IO.Path]::GetFullPath($DatabasePath)
if (!(Test-Path -LiteralPath $database)) { throw 'Production database missing; observer will not create it.' }
$sampleFile = Join-Path $OutputRoot 'samples.jsonl'
if (Test-Path -LiteralPath $sampleFile) { throw 'An observation already exists here; choose a new output directory.' }
$cache = [IO.Path]::GetFullPath((Join-Path $env:USERPROFILE '.codex\plugins\cache\context-notes-native\context-notes'))
$previous = @{}
$logicalProcessors = [Environment]::ProcessorCount
$watch = [Diagnostics.Stopwatch]::StartNew()
$startedUtc = [DateTimeOffset]::UtcNow.ToString('o')
$samples = 0
function Test-CompanionProcess($Item) {
    $line = $Item.CommandLine.Trim()
    return $line -match '(?:^|\s)--companion(?:\s|$)' -or $line -eq $Item.ExecutablePath -or $line -eq ('"' + $Item.ExecutablePath + '"')
}
Write-Output ('OBSERVATION started_utc=' + [DateTimeOffset]::UtcNow.ToString('o') + ' minutes=' + $DurationMinutes + ' output=' + $OutputRoot)
do {
    $at = [DateTimeOffset]::UtcNow
    $all = @(Get-CimInstance Win32_Process -Filter "name='SelectionProbe.exe' OR name='codex.exe'")
    $owned = @($all | Where-Object { $_.ExecutablePath -and $_.ExecutablePath.StartsWith($cache + '\', [StringComparison]::OrdinalIgnoreCase) })
    $companionIds = @($owned | Where-Object { Test-CompanionProcess $_ } | ForEach-Object { [int]$_.ProcessId })
    $children = @($all | Where-Object { $_.Name -eq 'codex.exe' -and [int]$_.ParentProcessId -in $companionIds -and $_.CommandLine -match '(?:^|\s)app-server(?:\s|$)' })
    $rows = @()
    foreach ($item in @($owned) + @($children)) {
        $process = Get-Process -Id $item.ProcessId -ErrorAction SilentlyContinue
        if (!$process) { continue }
        $role = if ($item.Name -eq 'SelectionProbe.exe' -and (Test-CompanionProcess $item)) {'Companion'} elseif ($item.CommandLine -match '--watch-codex') {'Watcher'} elseif ($item.CommandLine -match '--uia-worker') {'UIAWorker'} elseif ($item.Name -eq 'codex.exe') {'AppServer'} else {'OtherOwned'}
        $key = [string]$item.ProcessId + ':' + $item.CreationDate.ToUniversalTime().Ticks
        $cpu = $process.TotalProcessorTime.TotalSeconds
        $percent = $null
        if ($previous.ContainsKey($key)) {
            $seconds = ($at - $previous[$key].At).TotalSeconds
            if ($seconds -gt 0) { $percent = [math]::Round(100 * ($cpu - $previous[$key].CPU) / $seconds / $logicalProcessors, 4) }
        }
        $previous[$key] = @{ At=$at; CPU=$cpu }
        $rows += @{ pid=[int]$item.ProcessId; parent=[int]$item.ParentProcessId; role=$role; session=[int]$item.SessionId; started_utc=$item.CreationDate.ToUniversalTime().ToString('o'); working_set_bytes=$process.WorkingSet64; private_bytes=$process.PrivateMemorySize64; cpu_seconds=$cpu; cpu_percent_machine=$percent; handles=$process.HandleCount; threads=$process.Threads.Count }
        $process.Dispose()
    }
    $sizes = @{}
    foreach ($suffix in @('','-wal','-shm')) { $file=Get-Item -LiteralPath ($database+$suffix) -ErrorAction SilentlyContinue; $sizes['history.db'+$suffix] = if($file){$file.Length}else{0} }
    $sample = @{ at_utc=$at.ToString('o'); elapsed_seconds=[math]::Round($watch.Elapsed.TotalSeconds,3); logical_processors=$logicalProcessors; processes=@($rows); database_bytes=$sizes; observation_only=$true }
    $sample | ConvertTo-Json -Depth 6 -Compress | Add-Content -LiteralPath $sampleFile -Encoding UTF8
    $samples++
    if ($samples -eq 1 -or $samples % 10 -eq 0) { Write-Output ('OBSERVATION samples=' + $samples + ' elapsed_minutes=' + [math]::Round($watch.Elapsed.TotalMinutes,2) + ' companion=' + @($rows | Where-Object {$_.role -eq 'Companion'}).Count + ' watcher=' + @($rows | Where-Object {$_.role -eq 'Watcher'}).Count + ' app_server=' + $children.Count) }
    if ($watch.Elapsed.TotalMinutes -ge $DurationMinutes) { break }
    Start-Sleep -Seconds ([math]::Min($IntervalSeconds,[math]::Max(1,[math]::Ceiling($DurationMinutes*60-$watch.Elapsed.TotalSeconds))))
} while ($true)
$dbResult = @'
import json,sqlite3,sys
c=sqlite3.connect('file:'+sys.argv[1]+'?mode=ro',uri=True)
r={'integrity':c.execute('PRAGMA integrity_check').fetchone()[0], 'schema':c.execute('PRAGMA user_version').fetchone()[0], 'annotations':c.execute('SELECT COUNT(*) FROM annotations').fetchone()[0], 'messages':c.execute('SELECT COUNT(*) FROM messages').fetchone()[0]}
if len(sys.argv)>2 and sys.argv[2]:
    c.execute('ATTACH DATABASE ? AS baseline',('file:'+sys.argv[2]+'?mode=ro',))
    r['baseline_message_difference']=c.execute('SELECT COUNT(*) FROM (SELECT * FROM baseline.messages EXCEPT SELECT * FROM main.messages)').fetchone()[0]
    r['baseline_note_identity_difference']=c.execute('SELECT COUNT(*) FROM baseline.annotations b LEFT JOIN main.annotations a ON a.id=b.id WHERE a.id IS NULL OR a.thread_id IS NOT b.thread_id OR a.account_scope IS NOT b.account_scope OR a.selected_text IS NOT b.selected_text').fetchone()[0]
c.close()
print(json.dumps(r))
'@ | python - $database $BaselineDatabase
if ($LASTEXITCODE -ne 0) { throw 'Read-only database verification failed.' }
$summary = @{ started_utc=$startedUtc; ended_utc=[DateTimeOffset]::UtcNow.ToString('o'); elapsed_seconds=$watch.Elapsed.TotalSeconds; samples=$samples; database=($dbResult | ConvertFrom-Json); observation_only=$true; active_ui_workload_verified=$false }
$summary | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $OutputRoot 'summary.json') -Encoding UTF8
Write-Output ('OBSERVATION completed samples=' + $samples + ' elapsed_minutes=' + [math]::Round($watch.Elapsed.TotalMinutes,2) + ' integrity=' + $summary.database.integrity + ' active_ui_workload_verified=false')
