# ttslib: Windows Natural Voices for .NET

A .NET library for offline text-to-speech on Windows, built entirely on the neural
voice models that ship with the operating system. It turns text into audio using
the Natural Voices already installed on the machine: no cloud, no third-party G2P
engine, no vendor SDK.

- Enumerates installed Natural Voices via the AppExtension catalog.
- Runs the on-device acoustic model and vocoder with stock **ONNX Runtime**.
- Uses the shipped **Windows SAPI** text preprocessor for grapheme-to-phoneme.

The models already ship in Windows and sound like Microsoft's cloud voices, but
there is no supported API to reach them. ttslib fills that gap, and doubles as a
reference implementation of the public API Microsoft should ship. See
[`docs/BACKGROUND.md`](docs/BACKGROUND.md).

## Documentation

- [`docs/API.md`](docs/API.md): the full public API reference, plus how to build and browse the DocFX site
- [`docs/BACKGROUND.md`](docs/BACKGROUND.md): why this exists and what Microsoft should ship
- [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md): the pipeline and key design decisions
- [`docs/ROADMAP.md`](docs/ROADMAP.md): what works, what's next, and the definition of done
- [`src/README.md`](src/README.md): the deeper technical writeup

## Install

Not yet published to NuGet. Consume via `ProjectReference` or a submodule until 0.1.0 ships.

## Quick start

```csharp
using Claros;

// SpeechPlatform is the single entry point: it discovers installed voices and
// recognition models and creates warm speakers/transcribers for both halves.
using var platform = new SpeechPlatform();
var voices = await platform.ListVoicesAsync();

using var speaker = NaturalVoiceSpeaker.Load(voices[0]);
var waveform = await speaker.SpeakAsync("The quick brown fox, jumps over the lazy dog.");

WaveFile.WriteMono16("hello.wav", waveform.Samples, waveform.SampleRate);
```

See [`samples/`](samples) for runnable end-to-end examples (quick-start Demo,
voice discovery, batch synthesis, and a low-level pipeline walkthrough).

## Speech-to-text (offline)

The library also transcribes audio with the same on-device recognition model
that powers **Windows Live Captions**, through Microsoft's Azure Embedded Speech
runtime. Everything runs locally; no network call is made.

```csharp
using Claros;

using var platform = new SpeechPlatform();
var model = platform.FindRecognitionModel("en-US");
using var transcriber = platform.CreateTranscriber(model!);

var result = await transcriber.TranscribeFileAsync("call.wav");
Console.WriteLine(result.Text);
```

For live audio, `EmbeddedTranscriber.StartSession()` returns a
`LiveTranscriptionSession` you feed 16-bit mono PCM as it arrives; commit a turn
per speaker (for example, one session per channel of a stereo call). The
[TranscriptionBenchmark](samples/TranscriptionBenchmark) sample does exactly
that and measures memory and latency against Foundry Local and NPU engines.

## Build

```
dotnet build Claros.slnx
dotnet run --project samples\Demo\Claros.Demo.csproj -- "text to speak"
```

The `Claros.slnx` solution builds the library and the Demo together.
`global.json` pins the toolchain to the .NET 10 SDK.

## Test

```
dotnet test Claros.slnx
```

The suite covers the pure-logic core (header extraction, streaming-op rewriting,
the phoneme table, the IPA→ARPABET map, `Tokens.xml` parsing, WAV writing, and
the vocoder tensor/normalization helpers) with synthetic inputs, so it runs
without an installed voice. Runtime-integration paths (WinRT enumeration, ONNX
inference, SAPI) require a real installed voice and are exercised via the Demo.

## Requirements

- Windows 10 19041 or newer
- .NET 10 SDK
- At least one installed **Natural Voice** (Settings > Time & language > Speech)
- The default **Microsoft Zira Desktop** SAPI voice (present on stock Windows)

## Legal

The voice model binaries themselves are shipped by Microsoft with a plaintext EULA
that forbids redistribution. ttslib reads them at runtime from the installed
AppExtension package. It never bundles or ships them.

## License

MIT for the library code. Voice binaries remain covered by their own EULA.
