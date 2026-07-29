# API reference

`Claros` ships full XML documentation on every public type and
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

## 3. Naming conventions

Three rules run through the whole surface:

- **A "synthesizer" makes audio; a "speaker" plays it.** Types that turn text
  into samples are named `*SpeechSynthesizer` and are built by
  `SpeechPlatform.CreateSynthesizer`. The word *speaker* is reserved for an
  actual output device — `AudioGraphSpeakerSink`, and the `speaker` parameter
  of `CreateConversation`, both of which are `IAudioSink`. Previously both
  meanings were in play at once, so `CreateSpeaker` returned something quite
  different from the `speaker` a conversation takes.
- **`Synthesize*` produces audio; `Speak*` plays it.** On the synthesizers and on
  `ISpeechSynthesizer`, `SynthesizeAsync` returns a `WaveformResult` and
  `SynthesizeToSinkAsync` streams into an `IAudioSink`;
  `EmbeddedSpeechSynthesizer.SpeakToDefaultOutputAsync` is the only member that reaches
  the speakers. (One level down, `NaturalVoiceEngine.SynthesizeAsync` returns the
  raw `CodecTokens` a vocoder still has to turn into a waveform — it is a stage of
  the pipeline, not a synthesizer.) A plain `string` converts implicitly to a
  `SpeechSynthesisRequest`, so `SynthesizeAsync("hello")` is the simple case;
  empty content is rejected, because synthesizing nothing is a caller bug.
- **Factories own, constructors borrow.** Anything you pass into a constructor
  stays yours and is never disposed, so a warm engine can be shared. Anything a
  `SpeechPlatform.Create*` factory builds for you is owned by the object it
  returns, so a single `using` (or `await using`) covers the whole lifetime.

## 4. Public type index

| Type | Purpose |
| --- | --- |
| `VoiceCatalog` | Enumerate installed Windows Natural Voices; raises `VoicesChanged` on install/update/removal. |
| `VoiceInfo` | Metadata for one installed voice (name, locale, gender, age, vendor, version, package paths). |
| `NaturalVoiceSynthesizer` | Transparent, license-free pipeline: SAPI frontend plus on-device ONNX acoustic model and vocoder. An `ISpeechSynthesizer`, so it drives `TimedNarrator` and `SpeechConversation` too; reports no word boundaries and rejects SSML/prosody rather than dropping them. |
| `EmbeddedSpeechSynthesizer` | Highest-fidelity path; reuses the on-device Azure Embedded Speech runtime, with live streaming to the default output. |
| `EmbeddedVoiceOptions` | Configuration for `EmbeddedSpeechSynthesizer` (voice, output format, forced-HD threshold). |
| `SpokenWord` | Word-boundary event data raised during live streaming. |
| `EmbeddedTranscriber` | Offline speech-to-text: drives the on-device Live Captions recognition model through the Azure Embedded Speech runtime. |
| `LiveTranscriptionSession` | Push-driven live transcription primitive; write PCM as it arrives, read growing text, commit a turn per speaker/channel. |
| `EmbeddedTranscriberOptions` | Configuration for `EmbeddedTranscriber` (runtime staging, profanity masking, segmentation timeout, sample rate). |
| `TranscriptionModelCatalog` | Enumerate installed on-device recognition models (`MicrosoftWindows.Speech.<locale>` packs). |
| `TranscriptionModelInfo` | Metadata for one installed recognition model (locale, model name, package paths). |
| `TranscriptionResult`, `TranscriptionSegment` | Recognized transcript text plus its ordered, sentence-level segments. |
| `NaturalVoiceEngine` | Loads and runs a voice's encoder/decoder acoustic model; returns discrete codec tokens. |
| `NaturalVoiceEngineOptions`, `SynthesisOptions` | Tuning for session build and decoder generation. |
| `Vocoder` | Converts codec tokens to mono PCM samples via stock ONNX Runtime. |
| `SapiPhonemizer` | Drives the shipped Windows SAPI text preprocessor for grapheme-to-phoneme conversion. |
| `PhonemeTable` | Phoneme-key to model-id map shipped inside a voice package. |
| `CodecTokens` | Discrete codec tokens emitted by the acoustic model, consumed by the vocoder. |
| `WaveFile` | Write mono 16-bit PCM WAV files. |
| `NaturalVoiceException` and subtypes | Typed errors: voice unavailable, package format, synthesis failure. |

### Platform facade and streaming interfaces

| Type | Purpose |
| --- | --- |
| `SpeechPlatform` | Single entry point over both halves: discover voices and recognition models, then create warm synthesizers, transcribers, narrators, and conversations. |
| `VoiceSource` | Which tier produces a voice's audio. `Device` is the default; `Cloud` only ever comes from an explicit opt-in. |
| `SynthesizerCapabilities` | What an engine guarantees: word boundaries, raw SSML, prosody, offline, metered. Check it instead of assuming. (The audio format is stated exactly by `ISpeechSynthesizer.OutputFormat`, not described here.) |
| `CloudSpeechSynthesizer`, `CloudVoiceOptions` | Opt-in hosted tier (Azure neural / HD / MAI-Voice) behind the same `ISpeechSynthesizer`. Requires a key and region; never used unless you construct it. |
| `ISpeechSynthesizer` | Request-in/audio-out synthesis contract, buffered or streamed to an `IAudioSink`. Exposes `OutputFormat` so a sink can be sized without a (possibly billed) probe request. Deliberately platform-neutral so another tier can implement it. |
| `ISpeechRecognizer`, `RecognitionEvent` | Streaming recognition contract and its partial/final events. |
| `ISpeechActivityDetector`, `EnergyVoiceActivityDetector` | Voice-activity detection used to endpoint a turn and trigger barge-in. |
| `SpeechSynthesisRequest`, `SpeechProsody` | Plain text, prosody-shaped text, or raw SSML input to a synthesizer. |
| `AudioFormat`, `AudioBuffer`, `IAudioSource`, `IAudioSink` | Audio primitives shared by capture, synthesis, and playback. |
| `SpeechConversation` | Round-trip loop: capture, endpoint, recognize, hand the turn to a handler, speak the reply, with barge-in. |
| `TimedNarrator`, `TimedCue`, `SubtitleParser` | Subtitle- and cue-timed narration: turn a timeline of cues into an aligned voiceover track. |
| `StreamingRecognizer`, `CallLegTranscriber` | Live recognition over a push audio stream, and per-channel transcription of a two-party call. |

See the [generated API reference](api/index.md) for the complete member-level
documentation.
