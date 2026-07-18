# SpeakWebPage

Fetch a web page, extract its readable text, and narrate it to a WAV file using
the flagship, fully-offline **`EmbeddedVoiceSpeaker`** (forced HD) - Microsoft's
own on-device neural voice, no cloud call.

## Run

`EmbeddedVoiceSpeaker` drives the gated on-device Embedded Speech runtime. The
required on-device model license is read automatically from the installed voice
package, so no configuration is needed. Run with an explicit runtime identifier
so the native runtime is placed correctly:

```powershell
dotnet run -r win-arm64 `
  --project samples\SpeakWebPage\WindowsNaturalVoices.SpeakWebPage.csproj `
  -- https://example.com/article --out article.wav --voice Ava
```

Use `-r win-x64` on x64 machines. Set `NATURAL_VOICE_LICENSE` only if you need to
override the license read from the package.

## Options

| Flag | Meaning | Default |
| --- | --- | --- |
| `<url>` | Absolute http/https page to read (required). | - |
| `--out` | Output WAV path. | `page.wav` |
| `--voice` | Substring of the installed Natural voice display name. | first installed |
| `--max` | Max characters to narrate; `0` reads the whole page. | `1200` |

## Notes

- Requires at least one **Natural Voice** installed (Settings > Time & language >
  Speech > Manage voices) and the gated Embedded Speech extension that ships with
  Windows Narrator.
- The HTML-to-text step is intentionally dependency-free and best-effort: it
  strips scripts, styles, and markup and collapses whitespace. It is not a full
  readability engine, so navigation chrome may leak in on complex pages.
- Forced HD renders every utterance through the high-fidelity acoustic model (see
  [`EmbeddedVoiceSpeaker`](../../src/EmbeddedVoiceSpeaker.cs)), so output is close
  to Microsoft's cloud voices.
