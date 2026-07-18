# ttslib — Windows Natural Voices for .NET

A .NET library for offline text-to-speech on Windows, built entirely on the neural
voice models that ship with the operating system. It turns text into audio using
the Natural Voices already installed on the machine — no cloud, no third-party G2P
engine, no vendor SDK.

- Enumerates installed Natural Voices via the AppExtension catalog.
- Runs the on-device acoustic model and vocoder with stock **ONNX Runtime**.
- Uses the shipped **Windows SAPI** text preprocessor for grapheme-to-phoneme.

The models already ship in Windows and sound like Microsoft's cloud voices, but
there is no supported API to reach them. ttslib fills that gap — and doubles as a
reference implementation of the public API Microsoft should ship. See
[`docs/BACKGROUND.md`](docs/BACKGROUND.md).

## Documentation

- [`docs/BACKGROUND.md`](docs/BACKGROUND.md) — why this exists and what Microsoft should ship
- [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) — the pipeline and key design decisions
- [`docs/ROADMAP.md`](docs/ROADMAP.md) — what works, what's next, and the definition of done
- [`src/README.md`](src/README.md) — the deeper technical writeup

## Install

Not yet published to NuGet. Consume via `ProjectReference` or a submodule until 0.1.0 ships.

## Quick start

```csharp
using WindowsNaturalVoices;

using var catalog = new VoiceCatalog();
var voices = await catalog.ListVoicesAsync();

using var speaker = NaturalVoiceSpeaker.Load(voices[0]);
var waveform = await speaker.SpeakAsync("The quick brown fox, jumps over the lazy dog.");

WaveFile.WriteMono16("hello.wav", waveform.Samples, waveform.SampleRate);
```

See [`samples/`](samples) for runnable end-to-end examples (quick-start Demo,
voice discovery, batch synthesis, and a low-level pipeline walkthrough).

## Build

```
dotnet build WindowsNaturalVoices.slnx
dotnet run --project samples\Demo\WindowsNaturalVoices.Demo.csproj -- "text to speak"
```

The `WindowsNaturalVoices.slnx` solution builds the library and the Demo together.
`global.json` pins the toolchain to the .NET 10 SDK.

## Test

```
dotnet test WindowsNaturalVoices.slnx
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
