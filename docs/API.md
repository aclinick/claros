# API reference

`WindowsNaturalVoices` ships full XML documentation on every public type and
member. There are three ways to read it.

## 1. In your IDE

The NuGet package includes the generated XML doc file, so IntelliSense shows
summaries, parameters, and remarks for every public member as you type.

## 2. Browse the generated site (DocFX)

The reference site is generated with [DocFX](https://dotnet.github.io/docfx/)
from the same XML docs. DocFX is pinned as a local tool, so no global install
is needed.

```powershell
dotnet tool restore          # once, installs the pinned DocFX
dotnet docfx docfx.json      # generate metadata + build the static site
dotnet docfx docfx.json --serve   # build and serve at http://localhost:8080
```

Output lands in `docs/_site/` (git-ignored). The API metadata under
`docs/api/*.yml` is regenerated each run and is git-ignored as well; only the
hand-written landing pages (`docs/index.md`, `docs/api/index.md`) are committed.

## 3. Public type index

| Type | Purpose |
| --- | --- |
| `VoiceCatalog` | Enumerate installed Windows Natural Voices; raises `VoicesChanged` on install/update/removal. |
| `VoiceInfo` | Metadata for one installed voice (name, locale, gender, age, vendor, version, package paths). |
| `NaturalVoiceSpeaker` | Transparent, license-free pipeline: SAPI frontend plus on-device ONNX acoustic model and vocoder. |
| `EmbeddedVoiceSpeaker` | Highest-fidelity path; reuses the on-device Azure Embedded Speech runtime, with live streaming to the default output. |
| `EmbeddedVoiceOptions` | Configuration for `EmbeddedVoiceSpeaker` (voice, output format, forced-HD threshold). |
| `SpokenWord` | Word-boundary event data raised during live streaming. |
| `NaturalVoiceEngine` | Loads and runs a voice's encoder/decoder acoustic model; returns discrete codec tokens. |
| `NaturalVoiceEngineOptions`, `SynthesisOptions` | Tuning for session build and decoder generation. |
| `Vocoder` | Converts codec tokens to mono PCM samples via stock ONNX Runtime. |
| `SapiPhonemizer` | Drives the shipped Windows SAPI text preprocessor for grapheme-to-phoneme conversion. |
| `PhonemeTable` | Phoneme-key to model-id map shipped inside a voice package. |
| `WaveFile` | Write mono 16-bit PCM WAV files. |
| `NaturalVoiceException` and subtypes | Typed errors: voice unavailable, package format, synthesis failure. |

See the [generated API reference](api/index.md) for the complete member-level
documentation.
