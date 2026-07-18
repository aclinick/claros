param([Parameter(Mandatory)][int]$AppPid)
# Automated end-to-end validation for the Video Voiceover sample.
# NOTE: do NOT name the param $Pid (read-only in PowerShell).

$ErrorActionPreference = 'Continue'
$logDir = "D:\source\ttslib-extract\samples\WindowsNaturalVoices.VideoVoiceover\bin\Release\net10.0-windows10.0.26100.0\win-arm64"
$log = Join-Path $logDir 'voiceover.log'
$shotDir = "D:\source\ttslib-extract\samples\WindowsNaturalVoices.VideoVoiceover\screenshots"
New-Item -ItemType Directory -Force -Path $shotDir | Out-Null
$pass = 0; $fail = 0; $results = @()

function Test-UI {
    param([string]$Name, [scriptblock]$Script)
    try {
        $out = & $Script 2>&1
        if ($LASTEXITCODE -eq 0) { $script:pass++; $script:results += @{ name = $Name; status = 'PASS' } }
        else { $script:fail++; $script:results += @{ name = $Name; status = 'FAIL'; detail = "$out" } }
    } catch { $script:fail++; $script:results += @{ name = $Name; status = 'FAIL'; detail = "$_" } }
}
function Assert([string]$Name, [bool]$Cond, [string]$Detail = '') {
    if ($Cond) { $script:pass++; $script:results += @{ name = $Name; status = 'PASS' } }
    else { $script:fail++; $script:results += @{ name = $Name; status = 'FAIL'; detail = $Detail } }
}
function Alive { return [bool](Get-Process -Id $AppPid -ErrorAction SilentlyContinue) }

# Count near-white pixels in a fractional sub-region of a screenshot (used to prove
# the caption text renders WHITE, and that it disappears when captions are hidden).
function Count-WhiteInRegion([string]$path, [double]$x0f, [double]$y0f, [double]$x1f, [double]$y1f) {
    Add-Type -AssemblyName System.Drawing -ErrorAction SilentlyContinue
    $bmp = [System.Drawing.Bitmap]::new($path)
    try {
        $w = $bmp.Width; $h = $bmp.Height
        $x0 = [int]($w * $x0f); $x1 = [int]($w * $x1f)
        $y0 = [int]($h * $y0f); $y1 = [int]($h * $y1f)
        $c = 0
        for ($y = $y0; $y -lt $y1; $y += 2) {
            for ($x = $x0; $x -lt $x1; $x += 2) {
                $p = $bmp.GetPixel($x, $y)
                if ($p.R -gt 235 -and $p.G -gt 235 -and $p.B -gt 235) { $c++ }
            }
        }
        return $c
    } finally { $bmp.Dispose() }
}
# Poll until the caption text is present (only true while a sentence is on screen
# AND captions are enabled). Uses the TextBlock (a Border has no UIA peer).
function Wait-Caption([int]$tries = 20) {
    for ($i = 0; $i -lt $tries; $i++) {
        winapp ui wait-for 'CurrentSentenceText' -a $AppPid -t 900 2>$null | Out-Null
        if ($LASTEXITCODE -eq 0) { return $true }
        Start-Sleep 1
    }
    return $false
}

# ── 1. Startup / language discovery ──
Test-UI 'ComboBox exists' { winapp ui wait-for 'LanguageComboBox' -a $AppPid -t 5000 }
Test-UI 'Open video button exists' { winapp ui wait-for 'OpenVideoButton' -a $AppPid -t 3000 }
Test-UI 'Captions toggle exists' { winapp ui wait-for 'ShowCaptionsToggle' -a $AppPid -t 3000 }
Test-UI 'Captions toggle defaults ON' { winapp ui wait-for 'ShowCaptionsToggle' -a $AppPid --value 'On' -t 3000 }
winapp ui screenshot -a $AppPid -o "$shotDir/01-initial.png" 2>$null | Out-Null

# Select a language by (re-)opening the ComboBox and clicking the item found fresh.
# ComboBoxItem RuntimeIds change each time the dropdown re-opens, so never reuse a
# selector captured earlier. Returns $true if the item was invoked.
function Select-Language([string]$Match) {
    for ($attempt = 0; $attempt -lt 3; $attempt++) {
        winapp ui invoke 'LanguageComboBox' -a $AppPid 2>$null | Out-Null
        Start-Sleep 2
        $hit = winapp ui search $Match -a $AppPid --json 2>$null | ConvertFrom-Json
        $sel = ($hit.matches | Where-Object { $_.className -eq 'ComboBoxItem' } | Select-Object -First 1).selector
        if ($sel) {
            winapp ui invoke $sel -a $AppPid 2>$null | Out-Null
            if ($LASTEXITCODE -eq 0) { Start-Sleep 1; return $true }
        }
        # No item enumerated this attempt, so close the dropdown and retry.
        winapp ui invoke 'LanguageComboBox' -a $AppPid 2>$null | Out-Null
        Start-Sleep 1
    }
    return $false
}

# ── 2. Language list contains English + French ──
winapp ui invoke 'LanguageComboBox' -a $AppPid 2>$null | Out-Null
Start-Sleep 1
$fr = winapp ui search 'French' -a $AppPid --json 2>$null | ConvertFrom-Json
$en = winapp ui search 'English' -a $AppPid --json 2>$null | ConvertFrom-Json
Assert 'French (France) offered' ($fr.matchCount -gt 0) "matches=$($fr.matchCount)"
Assert 'English offered' ($en.matchCount -gt 0) "matches=$($en.matchCount)"
winapp ui invoke 'LanguageComboBox' -a $AppPid 2>$null | Out-Null  # close the dropdown
Start-Sleep 1

# ── 3. Select French, wait for voice load ──
Test-UI 'Select French' { Select-Language 'French' }
Test-UI 'Status shows Remy ready' { winapp ui wait-for 'StatusText' -a $AppPid --value 'Remy' --contains -t 15000 }
winapp ui screenshot -a $AppPid -o "$shotDir/02-french-selected.png" 2>$null | Out-Null

# ── 4. Play → live French voiceover in sync ──
Test-UI 'Press Play' { winapp ui invoke 'PlayPauseButton' -a $AppPid }
Start-Sleep 16
$cap = (winapp ui get-value 'CurrentSentenceText' -a $AppPid --json 2>$null | ConvertFrom-Json).text
Assert 'Caption is non-empty (speaking)' ([bool]$cap) "caption='$cap'"
winapp ui screenshot -a $AppPid -o "$shotDir/03-playing.png" 2>$null | Out-Null
# At least three sentences should have started, in order, from the log.
$starts = @(Select-String -Path $log -Pattern 'Utterance start idx=(\d+)' | ForEach-Object { [int]$_.Matches[0].Groups[1].Value })
Assert 'At least 3 sentences spoken' ($starts.Count -ge 3) "count=$($starts.Count)"
$inOrder = $true; for ($i=1; $i -lt $starts.Count; $i++) { if ($starts[$i] -lt $starts[$i-1]) { $inOrder = $false } }
Assert 'Sentences spoken in order' $inOrder "seq=$($starts -join ',')"

# ── 4a. Live language switch mid-playback: no replay, English starts next (fixes #2/#3) ──
# Last sentence Start spoken BEFORE the switch (parsed from the log).
$startsBefore = @(Select-String -Path $log -Pattern 'Utterance start idx=\d+ start=([0-9:.]+)' | ForEach-Object { $_.Matches[0].Groups[1].Value })
$lastStartBefore = if ($startsBefore.Count -gt 0) { [TimeSpan]::Parse($startsBefore[-1]) } else { [TimeSpan]::Zero }
$utterCountBefore = $startsBefore.Count
Test-UI 'Switch to English while playing' { Select-Language 'English' }
Test-UI 'Active voice shows English' { winapp ui wait-for 'ActiveVoiceText' -a $AppPid --value 'English' --contains -t 6000 }
Start-Sleep 14
# The first utterance AFTER the switch must have a Start strictly greater than the
# last one spoken before it, i.e. we did NOT re-speak the current/previous line.
$startsAfter = @(Select-String -Path $log -Pattern 'Utterance start idx=\d+ start=([0-9:.]+)' | ForEach-Object { $_.Matches[0].Groups[1].Value })
Assert 'New sentence spoken after switch' ($startsAfter.Count -gt $utterCountBefore) "before=$utterCountBefore after=$($startsAfter.Count)"
$firstAfter = if ($startsAfter.Count -gt $utterCountBefore) { [TimeSpan]::Parse($startsAfter[$utterCountBefore]) } else { [TimeSpan]::Zero }
Assert 'Switch does NOT replay a spoken line' ($firstAfter -gt $lastStartBefore) "firstAfterSwitch=$firstAfter lastBefore=$lastStartBefore"
# Whole-session Start timestamps must be strictly increasing (no replay anywhere).
$allStarts = @($startsAfter | ForEach-Object { [TimeSpan]::Parse($_) })
$strictlyInc = $true; for ($i=1; $i -lt $allStarts.Count; $i++) { if ($allStarts[$i] -le $allStarts[$i-1]) { $strictlyInc = $false } }
Assert 'All utterance starts strictly increasing (no replay)' $strictlyInc "starts=$($startsAfter -join ',')"
winapp ui screenshot -a $AppPid -o "$shotDir/09-switched-english.png" 2>$null | Out-Null

# ── 4a2. Only successfully-loaded languages are offered (fix #4) ──
$preload = Select-String -Path $log -Pattern 'PreloadVoicesAsync requested=(\d+) loaded=(\d+)' | Select-Object -Last 1
if ($preload) {
    $reqN = [int]$preload.Matches[0].Groups[1].Value
    $loadN = [int]$preload.Matches[0].Groups[2].Value
    Assert 'At least one voice preloaded' ($loadN -ge 1) "loaded=$loadN"
    # ComboBox is closed now; re-open to count offered languages (retry for popup timing).
    $offered = 0
    for ($k = 0; $k -lt 3 -and $offered -eq 0; $k++) {
        winapp ui invoke 'LanguageComboBox' -a $AppPid 2>$null | Out-Null; Start-Sleep 2
        $offered = (winapp ui search 'ComboBoxItem' -a $AppPid --json 2>$null | ConvertFrom-Json).matchCount
        winapp ui invoke 'LanguageComboBox' -a $AppPid 2>$null | Out-Null; Start-Sleep 1  # close
    }
    Assert 'Only loaded languages offered' ($offered -eq $loadN) "offered=$offered loaded=$loadN"
} else {
    Assert 'Preload summary logged' $false 'no PreloadVoicesAsync summary line in log'
}


# (a) caption text present + WHITE on the dark band while ON
Assert 'Caption visible when ON' (Wait-Caption) 'caption text never appeared while ON'
winapp ui screenshot -a $AppPid -o "$shotDir/06-captions-on.png" 2>$null | Out-Null
$wOn = Count-WhiteInRegion "$shotDir/06-captions-on.png" 0.2 0.74 0.8 0.83
Assert 'Caption text renders WHITE' ($wOn -gt 400) "white px in caption region=$wOn (expected >400)"

# (a2) NO caption at the TOP of the video: the redundant top word-echo overlay was
# removed (it duplicated the bottom caption and wasn't governed by the toggle), and
# any embedded/external timed-text track is disabled. The upper-centre strip sits over
# the dark skull-top backdrop here, so only a handful of near-white pixels should
# remain (vs the bright bottom caption band).
$wTop = Count-WhiteInRegion "$shotDir/06-captions-on.png" 0.30 0.20 0.70 0.30
Assert 'No caption at top of video' ($wTop -lt 200) "near-white px in top strip=$wTop (expected <200; our bottom band=$wOn)"

# (b) toggle OFF hides the caption, but the voiceover keeps scheduling
winapp ui invoke 'ShowCaptionsToggle' -a $AppPid 2>$null | Out-Null
Start-Sleep 1
Test-UI 'Caption hidden when toggled OFF' { winapp ui wait-for 'CurrentSentenceText' -a $AppPid --gone -t 4000 }
$beforeOff = @(Select-String -Path $log -Pattern 'Utterance start idx=(\d+)').Count
Start-Sleep 16
$afterOff = @(Select-String -Path $log -Pattern 'Utterance start idx=(\d+)').Count
Assert 'Voiceover still plays with captions OFF' ($afterOff -gt $beforeOff) "utterances before=$beforeOff after=$afterOff"
winapp ui screenshot -a $AppPid -o "$shotDir/07-captions-off.png" 2>$null | Out-Null
$wOff = Count-WhiteInRegion "$shotDir/07-captions-off.png" 0.2 0.74 0.8 0.83
Assert 'Caption white text gone when OFF' ($wOn -gt ($wOff + 300)) "wOn=$wOn wOff=$wOff"

# (c) toggle back ON shows the caption again
winapp ui invoke 'ShowCaptionsToggle' -a $AppPid 2>$null | Out-Null
Start-Sleep 1
Assert 'Caption shown again when toggled ON' (Wait-Caption) 'caption did not reappear when ON'
winapp ui screenshot -a $AppPid -o "$shotDir/08-captions-on-again.png" 2>$null | Out-Null

# ── 5. Pause stops scheduling, no crash ──
$beforePause = @(Select-String -Path $log -Pattern 'Utterance start idx=(\d+)').Count
winapp ui invoke 'PlayPauseButton' -a $AppPid 2>$null | Out-Null
Start-Sleep 10
Assert 'App alive after pause' (Alive) 'process exited'
$afterPause = @(Select-String -Path $log -Pattern 'Utterance start idx=(\d+)').Count
# Paused: at most one more (the in-flight sentence that was already speaking) may appear, then none.
Assert 'No runaway sentences while paused' (($afterPause - $beforePause) -le 1) "before=$beforePause after=$afterPause"
winapp ui screenshot -a $AppPid -o "$shotDir/04-paused.png" 2>$null | Out-Null

# ── 6. Resume ──
Test-UI 'Resume play' { winapp ui invoke 'PlayPauseButton' -a $AppPid }
Start-Sleep 6
Assert 'App alive after resume' (Alive) 'process exited'

# ── 7. Seek re-syncs (SeekCompleted -> resync in log) ──
winapp ui set-value 'ProgressSlider' '10' -a $AppPid 2>$null | Out-Null
Start-Sleep 4
Assert 'App alive after seek' (Alive) 'process exited'
$seekResync = [bool](Select-String -Path $log -Pattern 'Seek -> resync')
Assert 'Seek triggered a resync' $seekResync 'no "Seek -> resync" in log'
winapp ui screenshot -a $AppPid -o "$shotDir/05-after-seek.png" 2>$null | Out-Null

# ── Results ──
Write-Host "`nPassed: $pass | Failed: $fail"
$results | Where-Object { $_.status -eq 'FAIL' } | ForEach-Object {
    Write-Host "  FAIL: $($_.name): $($_.detail)" -ForegroundColor Red
}
$results | ConvertTo-Json | Out-File "$shotDir/../test-results.json"
if ($fail -gt 0) { exit 1 } else { exit 0 }
