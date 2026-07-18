# SpeakSubtitles

Turn a subtitle file (`.srt` or `.vtt`) into a **voiceover track whose audio is
aligned to the subtitle timings**, using the flagship, fully-offline
**`EmbeddedVoiceSpeaker`** (forced HD).

Because the narration follows the cue timestamps, you can drive the talkover for
a video entirely from its subtitles: **edit the subtitle text or timings, re-run,
and drop the WAV back onto the video** as an audio track. Reword a line, nudge a
timestamp, regenerate — no re-recording.

## Language-aware voices

Windows on-device Natural voices are **locale-specific** (each voice has a single
`Locale`, e.g. `en-US`, `fr-FR`). This sample picks a voice by locale, so a
French subtitle narrates in an installed French voice. The target locale is
resolved in priority order:

1. `--voice <name>` — an explicit voice display-name substring (wins over locale).
2. `--lang <locale>` — e.g. `fr-FR` or `fr`.
3. The file name, using the `name.<lang>.ext` convention (`movie.fr-FR.srt`).
4. Otherwise the first installed voice.

If no installed voice matches the requested locale, the tool lists what *is*
installed. Install more languages from Settings > Time & language > Speech >
Manage voices.

## Run

```powershell
# Dry run: parse, pick a voice, and print the plan (no synthesis, nothing needed)
dotnet run -r win-arm64 `
  --project samples\SpeakSubtitles\WindowsNaturalVoices.SpeakSubtitles.csproj `
  -- movie.fr-FR.srt --dry-run

# Render the aligned voiceover track
dotnet run -r win-arm64 `
  --project samples\SpeakSubtitles\WindowsNaturalVoices.SpeakSubtitles.csproj `
  -- movie.fr-FR.srt --out movie.fr.wav
```

Use `-r win-x64` on x64 machines. The on-device model license is read
automatically from the installed voice package; set `NATURAL_VOICE_LICENSE` only
to override it.

## Options

| Flag | Meaning | Default |
| --- | --- | --- |
| `<file>` | Subtitle file, `.srt` or `.vtt` (required). | — |
| `--out` | Output WAV path. | input name with `.wav` |
| `--lang` | Target locale (`fr-FR` or `fr`); overrides file-name inference. | inferred |
| `--voice` | Substring of the Natural voice display name; overrides `--lang`. | by locale |
| `--dry-run` | Parse and pick a voice without synthesizing. | off |

## How timing works

Each cue is synthesized independently and placed on one silent timeline at its
start timestamp, then all clips are mixed into a single mono track at the voice's
sample rate (24 kHz). This keeps speech aligned to the video.

A synthesized line can be longer than its cue window; when that happens the tool
prints an `overruns next cue` note and the overlapping audio is mixed together.
To fix an overrun, shorten the line or widen the gap in the subtitle file — the
same edit-and-regenerate loop. This sample does not time-stretch audio to force a
line into its exact window.
