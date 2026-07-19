#Requires -Version 7
<#
.SYNOPSIS
    Hands-off demo driver for the Video Voiceover sample: plays the showcase
    video and switches the on-device voiceover between languages at natural
    pauses, so you can screen-record a polished multilingual demo untouched.

.DESCRIPTION
    This is a "demo maker". It builds and launches the (unpackaged) WinUI app,
    waits for every voice to warm up, gives you a countdown to start your screen
    recorder (Win+Shift+R, Xbox Game Bar, OBS, ...), then presses Play and drives
    the language switches over UI Automation.

    The switches are timed to land in the silent gaps between sentences, so a
    voice never changes mid-sentence. The app itself also refuses to interrupt an
    in-flight utterance, so even if a switch is triggered a little early or late
    it still lands cleanly on the next sentence boundary.

    Nothing here fakes the audio: the app produces the real on-device voiceover
    live. Capture it with any recorder that grabs system audio.

.EXAMPLE
    ./demo-drive.ps1
    Build, launch, 8-second countdown, then play the clip and change the voiceover
    language on every sentence (English -> Chinese -> French -> Spanish -> ...).

.EXAMPLE
    ./demo-drive.ps1 -Countdown 3 -SkipBuild
    Re-run quickly against an already-built binary with a short countdown.

.EXAMPLE
    ./demo-drive.ps1 -WaitForKey
    Pause after warm-up and wait for you to press a key (instead of a countdown)
    before playback begins.
#>
[CmdletBinding()]
param(
    # Seconds to count down after warm-up so you can start recording. Ignored when -WaitForKey is set.
    [int]$Countdown = 8,
    # Wait for a key press instead of counting down.
    [switch]$WaitForKey,
    # Skip the build step and use the existing binary.
    [switch]$SkipBuild,
    # Leave the app running when the arc finishes (default: leave it open).
    [switch]$CloseWhenDone,
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$csproj = Join-Path $here 'WindowsNaturalVoices.VideoVoiceover.csproj'

# Host RID (this project must be built for a concrete architecture).
$arch = [System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString().ToLowerInvariant()
$rid = "win-$arch"
$tfm = 'net10.0-windows10.0.26100.0'
$binDir = Join-Path $here "bin\$Configuration\$tfm\$rid"
$exe = Join-Path $binDir 'WindowsNaturalVoices.VideoVoiceover.exe'
$log = Join-Path $binDir 'voiceover.log'

# ── The choreography ──────────────────────────────────────────────────────────
# Switch the voiceover language on every sentence. The app plays one sentence at
# a time, waiting for each on-device utterance to synthesize, and always applies a
# language change on the next sentence boundary (never mid-sentence). So we simply
# cycle through these voices, one step per sentence the app speaks.
$StartLanguage = 'English'
$LanguageCycle = @('English', 'Chinese', 'French', 'Spanish')
$MaxRunSeconds = 360   # hard stop; the clip is ~2 min but per-sentence synthesis adds wall time.

function Info($m)  { Write-Host "  $m" -ForegroundColor Cyan }
function Good($m)  { Write-Host "  $m" -ForegroundColor Green }
function Warn($m)  { Write-Host "  $m" -ForegroundColor Yellow }

# Bring the app window to the foreground WITHOUT moving or resizing it, so it
# stays exactly where you positioned it for a fixed-frame capture (e.g. inside a
# PowerPoint slide). We never maximize or move the window.
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class Win {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
}
'@ -ErrorAction SilentlyContinue

function Focus-App([int]$ProcId) {
    try {
        $p = Get-Process -Id $ProcId -ErrorAction Stop
        for ($i = 0; $i -lt 20 -and $p.MainWindowHandle -eq 0; $i++) { Start-Sleep -Milliseconds 300; $p.Refresh() }
        if ($p.MainWindowHandle -ne 0) {
            # Activate only. No ShowWindow/Move/Resize, so the window keeps its
            # current size and position for the capture.
            [Win]::SetForegroundWindow($p.MainWindowHandle) | Out-Null
        }
    } catch { }
}

# Select a language by (re-)opening the ComboBox and clicking a freshly-found
# item. ComboBoxItem RuntimeIds change every time the dropdown re-opens, so we
# never reuse a selector captured earlier.
function Select-Language([int]$ProcId, [string]$Match) {
    for ($attempt = 0; $attempt -lt 4; $attempt++) {
        winapp ui invoke 'LanguageComboBox' -a $ProcId 2>$null | Out-Null
        Start-Sleep -Milliseconds 1200
        $hit = winapp ui search $Match -a $ProcId --json 2>$null | ConvertFrom-Json
        $sel = ($hit.matches | Where-Object { $_.className -eq 'ComboBoxItem' } | Select-Object -First 1).selector
        if ($sel) {
            winapp ui invoke $sel -a $ProcId 2>$null | Out-Null
            if ($LASTEXITCODE -eq 0) { Start-Sleep -Milliseconds 600; return $true }
        }
        # Nothing enumerated this attempt: close the dropdown and retry.
        winapp ui invoke 'LanguageComboBox' -a $ProcId 2>$null | Out-Null
        Start-Sleep -Milliseconds 600
    }
    return $false
}

Write-Host "`n=== Video Voiceover demo maker ===" -ForegroundColor White

# ── 1. Build ────────────────────────────────────────────────────────────────
if (-not $SkipBuild) {
    Info "Building ($rid, $Configuration)..."
    dotnet build $csproj -c $Configuration -r $rid --nologo | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Build failed. Fix errors and re-run, or use -SkipBuild." }
    Good "Build succeeded."
}
if (-not (Test-Path $exe)) { throw "App not found at $exe. Build first (drop -SkipBuild)." }

# Truncate the log so our verification only sees this run, and confirm it worked;
# a stale log would make us miscount sentences and fire the wrong switches.
if (Test-Path $log) {
    Clear-Content $log -ErrorAction Stop
    if ((Get-Item $log).Length -ne 0) { throw "Could not clear the old log ($log). Close any running instance and retry." }
}

# ── 2. Launch ─────────────────────────────────────────────────────────────────
Info "Launching the app..."
$proc = Start-Process -FilePath $exe -PassThru
$appPid = $proc.Id
Good "Running (PID $appPid)."

try {
    # ── 3. Wait for the UI and voices to be ready ────────────────────────────
    Info "Waiting for the window..."
    winapp ui wait-for 'LanguageComboBox' -a $appPid -t 30000 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "The app window never came up." }
    Focus-App $appPid

    Info "Warming up the on-device voices (first load takes a moment)..."
    $ready = $false
    for ($i = 0; $i -lt 60; $i++) {
        winapp ui wait-for 'StatusText' -a $appPid --value 'Ready.' --contains -t 1000 2>$null | Out-Null
        if ($LASTEXITCODE -eq 0) { $ready = $true; break }
        Start-Sleep 1
    }
    if (-not $ready) { throw "Voices never reported ready. See $log." }
    $readyLine = (Select-String -Path $log -Pattern 'PreloadVoicesAsync .*ready=\[[^\]]*\]' | Select-Object -Last 1).Line
    Good "Voices ready. $readyLine"

    # Captions on (default), and pick the opening language before we hit Play.
    winapp ui wait-for 'ShowCaptionsToggle' -a $appPid --value 'On' -t 2000 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) { winapp ui invoke 'ShowCaptionsToggle' -a $appPid 2>$null | Out-Null }

    Info "Selecting the opening language: $StartLanguage"
    if (-not (Select-Language $appPid $StartLanguage)) { throw "Could not select $StartLanguage." }
    winapp ui wait-for 'ActiveVoiceText' -a $appPid --value $StartLanguage --contains -t 8000 2>$null | Out-Null
    Focus-App $appPid

    # ── 4. Countdown so you can start recording ──────────────────────────────
    Write-Host ""
    Warn "READY TO RECORD."
    Warn "Start your screen recorder now (Win+Shift+R, Xbox Game Bar, or OBS)."
    Warn "System audio must be captured. The window is left at its current size and position."
    if ($WaitForKey) {
        Write-Host "  Press any key to begin playback..." -ForegroundColor Yellow
        [void][System.Console]::ReadKey($true)
    } else {
        for ($s = $Countdown; $s -gt 0; $s--) {
            Write-Host "  Playback in $s..." -ForegroundColor Yellow
            Start-Sleep 1
        }
    }

    # ── 5. Play, then switch language on every sentence ───────────────────────
    Focus-App $appPid
    winapp ui invoke 'PlayPauseButton' -a $appPid 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Could not press Play (UI Automation invoke failed)." }
    $playStart = Get-Date
    Good "Playing. Voiceover is live in $StartLanguage."

    # The app logs an "Utterance start" line each time it begins a sentence. We
    # react to each one by selecting the next language in the cycle, which the app
    # applies on the following sentence boundary. So sentence N+1 speaks the next
    # voice. No timing math: the app waits for synthesis, we just follow along.
    $activeLanguage = $StartLanguage
    $seen = 0
    $lastUtterance = Get-Date
    $reason = 'timeout'
    while ($true) {
        if (-not (Get-Process -Id $appPid -ErrorAction SilentlyContinue)) { $reason = 'exited'; break }
        if (((Get-Date) - $playStart).TotalSeconds -ge $MaxRunSeconds) { $reason = 'timeout'; break }

        $starts = @(Select-String -Path $log -Pattern 'Utterance start idx=\d+' -ErrorAction SilentlyContinue)
        while ($seen -lt $starts.Count) {
            $seen++
            $lastUtterance = Get-Date
            $next = $LanguageCycle[$seen % $LanguageCycle.Count]
            if ($next -ne $activeLanguage) {
                Info ("Sentence {0}: switching voiceover to {1}" -f $seen, $next)
                if (Select-Language $appPid $next) {
                    $activeLanguage = $next
                    Good "The next sentence speaks in $next."
                } else {
                    Warn "Could not switch to $next (continuing)."
                }
                Focus-App $appPid
            }
        }

        # End when the app has gone quiet after speaking (playback finished).
        if ($seen -gt 0 -and ((Get-Date) - $lastUtterance).TotalSeconds -ge 20) { $reason = 'finished'; break }
        Start-Sleep -Milliseconds 400
    }

    # ── 6. Done ───────────────────────────────────────────────────────────────
    switch ($reason) {
        'finished' { Good ("Demo arc complete ({0} sentences). Stop your recording." -f $seen) }
        'exited'   { Warn ("The app exited after {0} sentence(s). Stop your recording; check voiceover.log." -f $seen) }
        'timeout'  { Warn ("Hit the {0}s safety timeout after {1} sentence(s). Stop your recording; check voiceover.log." -f $MaxRunSeconds, $seen) }
    }

    # ── 7. Sync report (from the app's own log) ──────────────────────────────
    Write-Host "`n  Voiceover timeline (from the log):" -ForegroundColor White
    Select-String -Path $log -Pattern 'Utterance start idx=\d+ start=[0-9:.]+ pos=[0-9:.]+' |
        ForEach-Object { $_.Matches[0].Value } |
        ForEach-Object { Write-Host "    $_" -ForegroundColor DarkGray }
    Write-Host "  Language switches applied:" -ForegroundColor White
    Select-String -Path $log -Pattern 'SetActiveLanguage lang=\S+ voice=\S+' |
        ForEach-Object { Write-Host "    $($_.Matches[0].Value)" -ForegroundColor DarkGray }
}
finally {
    if ($CloseWhenDone) {
        Info "Closing the app..."
        Stop-Process -Id $appPid -ErrorAction SilentlyContinue
    } else {
        Info "Leaving the app open (PID $appPid). Close it when you're done."
    }
}
