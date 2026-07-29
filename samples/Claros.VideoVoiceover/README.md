# VideoVoiceover

A WinUI 3 desktop app that plays a video and **narrates its subtitles live, on
device**, in the language you pick — switching voices mid-playback without ever
cutting a sentence in half.

This is the sample behind the demo in the pitch deck. Nothing is pre-rendered:
every line is synthesized by `Claros` as the video reaches it, from voices
already installed on the machine.

## What it shows

- **Subtitle-driven narration.** Cues are grouped into sentences and spoken as
  playback reaches them, so the voiceover stays aligned to the picture.
- **Live language switching.** Change the voice from the dropdown while the video
  is playing and the *next* sentence speaks in the new voice.
- **Never interrupts an utterance.** A switch, pause, or seek stops scheduling
  *new* audio and re-syncs; whatever line is already speaking finishes first.
  This is deliberate — cancelling the thread-hostile native synthesizer from the
  UI thread reliably crashes the runtime (see the note on `VoiceoverController`).
- **Warm voices.** Every voice is preloaded up front, because model load
  dominates first-call latency.

## Prerequisites

- The .NET 10 SDK and the Windows App SDK.
- **One Natural Voice per language you want to hear** (Settings > Time &
  language > Speech > Manage voices). The language dropdown only lists voices
  that are actually installed, so install French and Chinese voices if you want
  the multilingual arc.

## Run it

The project builds for a concrete architecture; the RID defaults to the host's.

```powershell
dotnet run -c Release --project samples\Claros.VideoVoiceover\Claros.VideoVoiceover.csproj
```

Pick a language, press Play, and the narration follows the subtitles. The app
writes a `voiceover.log` next to the binary recording every utterance start,
its media position, and every language switch — that log is how the scripts
below verify timing.

## Recording a hands-off demo

`demo-drive.ps1` is a **demo maker**. It builds and launches the app, waits for
every voice to warm up, gives you a countdown to start a screen recorder, then
presses Play and drives the language switches over UI Automation — so you can
record a polished multilingual demo without touching the machine.

```powershell
./demo-drive.ps1                      # build, launch, 8s countdown, then run the arc
./demo-drive.ps1 -Countdown 3 -SkipBuild   # quick re-run against an existing build
./demo-drive.ps1 -WaitForKey          # wait for a keypress instead of counting down
```

| Parameter | Effect |
| --- | --- |
| `-Countdown <n>` | Seconds to wait after warm-up so you can start recording (default 8). |
| `-WaitForKey` | Wait for a keypress instead of counting down. |
| `-SkipBuild` | Use the existing binary. |
| `-CloseWhenDone` | Close the app when the arc finishes (default: leave it open). |
| `-Configuration` | Build configuration (default `Release`). |

How the choreography works: the app logs an `Utterance start` line each time it
begins a sentence, and the script reacts to each one by selecting the next
language in the cycle (English → Chinese → French → Spanish → …). Because the app
applies a change on the following sentence boundary, sentence *N+1* speaks in the
new voice. There is **no timing math** — the script just follows the app, so it
stays in sync even though per-sentence synthesis adds wall-clock time.

Two details that matter when capturing:

- **The window is never moved or resized.** The script only activates it, so you
  can position the app once (for example inside a slide) and capture a fixed
  frame.
- **Capture system audio.** The voiceover is real, live audio — use Win+Shift+R,
  Xbox Game Bar, or OBS with system audio enabled.

When the arc finishes the script prints the voiceover timeline and the language
switches it applied, both read back out of `voiceover.log`, so you can confirm
each switch landed on a sentence boundary before you keep the take.

## Automated UI tests

`ui-tests.ps1` runs end-to-end UI Automation assertions against a running
instance and writes screenshots alongside the results:

```powershell
./ui-tests.ps1 -AppPid <pid>
```
