# Windows.Speech API reference

Offline text-to-speech on Windows using the neural voices already built into
the operating system. This site is the generated API reference for the
`Windows.Speech` library.

- **[API reference](api/index.md)**: every public type and member.
- **[Project README](https://github.com/aclinick/ttslib/blob/master/src/README.md)**: why the library exists and what it does.

## Quick start

```csharp
using Windows.Speech;

using var catalog = new VoiceCatalog();
var voices = await catalog.ListVoicesAsync();

using var speaker = NaturalVoiceSpeaker.Load(voices[0]);
var waveform = await speaker.SpeakAsync("The quick brown fox, jumps over the lazy dog.");

WaveFile.WriteMono16("hello.wav", waveform.Samples, waveform.SampleRate);
```

For the highest-fidelity path that reuses the on-device Azure Embedded Speech
runtime directly, see <xref:Windows.Speech.EmbeddedVoiceSpeaker>.
