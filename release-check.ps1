# Release check: builds the app and runs every automatic check on a clean copy, away from your own
# settings. Run it before tagging a release:
#
#     powershell -ExecutionPolicy Bypass -File .\release-check.ps1
#
# It stops at the first failure. The reports are kept in the folder it prints, to read or attach.

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$work = Join-Path ([IO.Path]::GetTempPath()) ('afd-release-check-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $work | Out-Null
$failed = $false
function Step([string]$name, [scriptblock]$check) {
    Write-Host ('  ' + $name.PadRight(34)) -NoNewline
    try {
        $note = & $check
        Write-Host ('OK   ' + $note) -ForegroundColor Green
    } catch {
        Write-Host ('FAIL ' + $_.Exception.Message) -ForegroundColor Red
        $script:failed = $true
    }
}

Write-Host "Almost Fast Dash release check  ($work)"

Step 'Build (Release)' {
    $output = & dotnet build (Join-Path $root 'AlmostFastDash.csproj') -c Release 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0 -or $output -notmatch 'Build succeeded') { throw ($output -split "`n" | Select-String ' error ' | Select-Object -First 3 | Out-String).Trim() }
    Copy-Item (Join-Path $root 'bin\Release\net48\AlmostFastDash.exe') $work
    'one exe, ' + [math]::Round((Get-Item (Join-Path $work 'AlmostFastDash.exe')).Length / 1KB) + ' KB'
}
if ($failed) { exit 1 }
$exe = Join-Path $work 'AlmostFastDash.exe'

Step 'Designer and light self-tests' {
    $report = Join-Path $work 'selftest.txt'
    Start-Process $exe -ArgumentList '--designer-selftest', $report -WorkingDirectory $work -Wait
    $summary = Get-Content $report | Select-String 'CHECKS (PASSED|FAILED)|CRASHED' | Select-Object -First 1
    if ($summary -eq $null -or $summary.Line -notmatch '^ALL \d+ CHECKS PASSED') {
        throw ((Get-Content $report | Select-String '^FAIL|CRASHED' | Select-Object -First 5 | Out-String).Trim())
    }
    $summary.Line
}

Step 'Templates render' {
    $folder = Join-Path $work 'renders'
    Start-Process $exe -ArgumentList '--render-templates', $folder -WorkingDirectory $work -Wait
    $count = (Get-ChildItem $folder -Filter *.png -ErrorAction SilentlyContinue).Count
    if ($count -lt 30) { throw "only $count images" }
    "$count images"
}

Step 'Light frames' {
    $report = Join-Path $work 'light-frames.txt'
    Start-Process $exe -ArgumentList '--light-frames', $report -WorkingDirectory $work -Wait
    if (-not (Test-Path $report) -or -not (Select-String -Path $report -Pattern 'Shift point' -Quiet)) { throw 'no report' }
    ((Select-String -Path $report -Pattern '^== ').Count).ToString() + ' dashboards through a rev sweep and the preview states'
}

# The newest finished recording: one iRacing is still writing (you're in a session) is locked, so it's skipped.
function Test-Finished($file) {
    try { $stream = [IO.File]::Open($file.FullName, 'Open', 'Read', 'Read'); $stream.Dispose(); return $true } catch { return $false }
}
$recording = Get-ChildItem (Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'iRacing\telemetry') -Filter *.ibt -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending | Where-Object { Test-Finished $_ } | Select-Object -First 1
if ($recording) {
    Step 'Replay of a real recording' {
        $report = Join-Path $work 'replay.txt'
        # Quoted: recordings are named with spaces.
        Start-Process $exe -ArgumentList '--replay-ibt', ('"' + $recording.FullName + '"'), ('"' + $report + '"') -WorkingDirectory $work -Wait
        if (-not (Test-Path $report) -or (Get-Item $report).Length -lt 200) { throw 'no report' }
        $recording.Name
    }
} else {
    Write-Host '  Replay of a real recording        skipped (no .ibt in Documents\iRacing\telemetry)' -ForegroundColor DarkGray
}

Write-Host ''
if ($failed) { Write-Host 'Release check FAILED. Reports are in the folder above.' -ForegroundColor Red; exit 1 }
Write-Host 'Release check passed.' -ForegroundColor Green
exit 0
