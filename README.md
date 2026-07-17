# ttslib — Windows Natural Voices for .NET

End-to-end offline text-to-speech on Windows, powered by the Natural Voices already
installed on the machine. No cloud, no third-party G2P engine, no vendor SDK.

- Enumerates installed Natural Voices via the AppExtension catalog.
- Runs the on-device acoustic model and vocoder with stock **ONNX Runtime**.
- Uses the shipped **Windows SAPI** text preprocessor for grapheme-to-phoneme.

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

See [`src/README.md`](src/README.md) for the deeper writeup, and
[`samples/Demo`](samples/Demo) for a runnable end-to-end example.

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
