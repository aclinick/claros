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
var voices = await catalog.ListVoicesAsync();

using var speaker = NaturalVoiceSpeaker.Load(voices[0]);
var waveform = await speaker.SpeakAsync("The quick brown fox, jumps over the lazy dog.");

WaveFile.WriteMono16("hello.wav", waveform.Samples, waveform.SampleRate);
```

## What works today

- Dynamic voice enumeration through `AppExtensionCatalog`. Newly installed, updated, or removed voices raise `VoicesChanged`.
- Voice metadata parsed from `Tokens.xml`: display name, locale, gender, age, vendor, version.
- Phoneme table loaded from the package's `hd_phones.txt`.
- Acoustic model loaded via `NaturalVoiceEngine.Load(voice)`. Extracts encoder and decoder ONNX by skipping the plaintext header, then constructs `InferenceSession` for each.
- Autoregressive decoder loop with attention state, LSTM state, and stop gate. Returns discrete codec tokens.
- Text-to-phoneme conversion via `SapiPhonemizer`, which drives the shipped Windows SAPI text preprocessor (`MSTTSLoc_OneCore.dll`) through `System.Speech.Synthesis.SpeechSynthesizer.PhonemeReached`. The same frontend powers Azure Speech and the on-device Natural Voices, and it runs entirely offline.
- Vocoder execution via `Vocoder`, which rewrites the shipped `Streaming*` custom operators back to their standard ONNX equivalents so stock ONNX Runtime can load and run the graph. Produces mono PCM samples at 26 kHz.

## What is missing

Nothing structural for basic offline TTS. The two remaining gaps are quality tuning and platform coverage, not blocking bugs. Quality tuning includes prosody: SAPI's `Emphasis` field stays at zero for Zira, so stressed content words currently get the same weight as function words. The vocoder emits at 26 kHz natively; rewrapping the samples with a 24 kHz header trades a small amount of pitch for slower delivery that matches Azure Ava more closely.

The vocoder rewrite loses streaming state, so this library synthesizes each phrase as one non-streaming inference. Real-time streaming would require reimplementing the `Streaming*` operator family with its per-chunk state buffer or bringing in a permissive streaming vocoder such as HiFi-GAN.

## Grapheme to phoneme

`SapiPhonemizer` handles English (and other SAPI-supported locales) by driving the shipped Windows text preprocessor and mapping its IPA output to the acoustic model's ARPABET table. For locales where SAPI has no voice, callers can still supply phoneme ids directly via `PhonemeTable.TryGetArpabet` or `TryGet`. Piper, espeak-ng, and Kokoro's `misaki` are three permissive open source G2P options that fit.

## Building

```
dotnet build src\WindowsNaturalVoices\WindowsNaturalVoices.csproj
dotnet run --project samples\WindowsNaturalVoices.Demo
```

Requires the Windows App SDK target framework `net10.0-windows10.0.26100.0` (or later) for the WinRT `AppExtensionCatalog` projection. ONNX Runtime 1.22 handles inference.

## License and legal notes

The library itself is MIT licensed. Nothing in it redistributes Microsoft voice model bytes; the code reads them at runtime from the installed package on the machine that runs it. The plaintext license notice inside each `*.bin` file states that the model and software may not be used or distributed except under a written agreement with Microsoft (reference number 2774316). Consult your own counsel before using this library to power a shipping product. The intent here is technical demonstration and to make the case that a clean public API would be more useful than the current situation.
