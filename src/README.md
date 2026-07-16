# WindowsNaturalVoices

A small .NET library that enumerates installed Windows Natural Voices and runs their on-device acoustic models via stock ONNX Runtime.

## Why this library exists

Microsoft ships neural TTS voices on Windows as MSIX packages. Each package declares itself with the AppExtension contract `com.microsoft.voice.model.1`. That contract is the standard OS mechanism apps use to publish extensible content for other apps to consume.

Despite declaring these voices as AppExtensions, Microsoft has not shipped a public API to enumerate them, load them, or run inference against them. The `Windows.Media.SpeechSynthesis.SpeechSynthesizer` API is the only way to reach these voices from third party code, and it is restricted to packaged apps with the appropriate WinRT surface.

There is no cryptographic protection on the model files. Each `*.bin` in a voice package starts with a 705 byte plaintext license notice, followed by 8 bytes of hex tag, followed by a raw ONNX ModelProto. The encoder and decoder load with stock `Microsoft.ML.OnnxRuntime` after skipping the header. This library demonstrates how straightforward the missing public API would be to ship.

## What it does

```csharp
using WindowsNaturalVoices;

using var catalog = new VoiceCatalog();
catalog.VoicesChanged += (_, _) => Console.WriteLine("Installed voices changed.");

var voices = await catalog.ListVoicesAsync();
foreach (var v in voices)
{
    Console.WriteLine($"{v.DisplayName}  ({v.Locale}, {v.Gender})");
}

using var engine = NaturalVoiceEngine.Load(voices[0]);

var phonemes = new List<int> { engine.Phonemes.Bos };
foreach (var arpabet in new[] { "h", "eh1", "l", "ow1" })
{
    engine.Phonemes.TryGetArpabet("en-us", arpabet, out var id);
    phonemes.Add(id);
}
phonemes.Add(engine.Phonemes.Eos);

var tokens = await engine.SynthesizeAsync(phonemes);
Console.WriteLine($"{tokens.Steps} decoder steps, {tokens.C20Hz.Length + tokens.C40Hz.Length} codec tokens.");
```

## What works today

- Dynamic voice enumeration through `AppExtensionCatalog`. Newly installed, updated, or removed voices raise `VoicesChanged`.
- Voice metadata parsed from `Tokens.xml`: display name, locale, gender, age, vendor, version.
- Phoneme table loaded from the package's `hd_phones.txt`.
- Acoustic model loaded via `NaturalVoiceEngine.Load(voice)`. Extracts encoder and decoder ONNX by skipping the plaintext header, then constructs `InferenceSession` for each.
- Autoregressive decoder loop with attention state, LSTM state, and stop gate. Returns discrete codec tokens.

## What is missing

Waveform playback. The decoder emits discrete codec tokens; the vocoder that turns those tokens into audio uses a custom op named `StreamingConv` in the ONNX domain `test.customop`. Microsoft's `SpeechRuntime.exe` implements that op but has not published it. Two forward paths exist:

1. Implement `StreamingConv` as a custom ORT op. It has the same interface as a 1D convolution with a streaming state buffer.
2. Swap in a permissive vocoder such as HiFi-GAN or Vocos and train a small adapter that maps the Microsoft codec tokens to the vocoder's expected mel input.

## Grapheme to phoneme

This library does not ship a G2P engine. Callers supply ARPABET-style phoneme identifiers using `PhonemeTable.TryGetArpabet` or full keys via `TryGet`. Piper, espeak-ng, and Kokoro's `misaki` are three permissive open source G2P options that fit.

## Building

```
dotnet build src\WindowsNaturalVoices\WindowsNaturalVoices.csproj
dotnet run --project samples\WindowsNaturalVoices.Demo
```

Requires the Windows App SDK target framework `net10.0-windows10.0.26100.0` (or later) for the WinRT `AppExtensionCatalog` projection. ONNX Runtime 1.22 handles inference.

## License and legal notes

The library itself is MIT licensed. Nothing in it redistributes Microsoft voice model bytes; the code reads them at runtime from the installed package on the machine that runs it. The plaintext license notice inside each `*.bin` file states that the model and software may not be used or distributed except under a written agreement with Microsoft (reference number 2774316). Consult your own counsel before using this library to power a shipping product. The intent here is technical demonstration and to make the case that a clean public API would be more useful than the current situation.
