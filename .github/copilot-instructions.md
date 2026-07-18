# Copilot instructions: ttslib (WindowsNaturalVoices)

Offline text-to-speech for .NET that drives the **Natural Voices already installed
on a Windows machine**. No cloud, no bundled models, no third-party G2P engine.
The library reads Microsoft's on-device voice packages at runtime and runs their
ONNX acoustic model and vocoder through stock ONNX Runtime.

## Build & run

```
dotnet build WindowsNaturalVoices.slnx
dotnet test WindowsNaturalVoices.slnx
dotnet run --project samples\Demo\WindowsNaturalVoices.Demo.csproj -- "text to speak"
```

- `WindowsNaturalVoices.slnx` is the solution (XML `slnx` format); it aggregates the
  library, the Demo, and the test project. `global.json` pins the SDK to the 10.x band.
- **Tests:** xUnit project at `tests\WindowsNaturalVoices.Tests`. It covers only the
  pure-logic units (header extraction, streaming-op rewrite, phoneme table, IPA map,
  Tokens.xml, WAV, vocoder tensor/normalize helpers) with synthetic inputs; no
  installed voice required. Internal types are reachable via `InternalsVisibleTo`
  in the library csproj. Runtime paths (WinRT `VoiceCatalog`, ONNX inference, SAPI)
  need a real installed voice and are validated by running the Demo, not unit tests.
- **Definition of done per change:** builds clean, ships/updates full unit-test
  coverage for touched pure-logic code with the suite passing, and passes a code
  review by a *different* model than the one that authored it before commit.
- Target framework is `net10.0-windows10.0.26100.0`: requires the .NET 10 SDK and
  the Windows App SDK / WinRT projection. Everything is Windows-only.
- There is no lint config in this repo. Unit tests cover the pure-logic core; the
  full **synthesis pipeline cannot be unit tested** because it needs a real
  installed Natural Voice (Settings > Time & language > Speech > Manage voices)
  plus the stock "Microsoft Zira Desktop" SAPI voice. Validate those runtime
  paths by building and running the Demo; model files are read from
  `VoiceInfo.InstalledPath`. See `samples/` for runnable examples (Demo,
  ListVoices, BatchSynthesis, LowLevelPipeline) and `samples/README.md`.

## Pipeline architecture (the big picture)

Text becomes audio through three stages, chained by the `NaturalVoiceSpeaker`
facade (`SpeakAsync` → phonemize → acoustic model → vocoder):

1. **`SapiPhonemizer`**: grapheme-to-phoneme. Drives the shipped Windows SAPI
   text preprocessor (`MSTTSLoc_OneCore.dll`, the same frontend Azure Speech uses)
   via `System.Speech`'s `SpeechSynthesizer.PhonemeReached` with output set to
   null. Maps SAPI's IPA output to the acoustic model's ARPABET table. Emits a
   phoneme-id list bracketed by `PhonemeTable.Bos`/`Eos`.
2. **`NaturalVoiceEngine`**: the acoustic model. Loads `hd_am_v5_encoder.bin` and
   `hd_am_v5_decoder.bin`, runs the encoder once, then an autoregressive decoder
   loop carrying attention + LSTM state and a stop gate. Emits discrete codec
   tokens (`CodecTokens`, two streams `C20Hz`/`C40Hz`), not audio.
3. **`Vocoder`**: codec tokens to waveform. Loads
   `hd_device_vocoder_v6_streaming.bin`, runs it, returns mono PCM
   (`WaveformResult`) at **26000 Hz** peak-normalized to 0.9.

`VoiceCatalog` sits before all of this: it enumerates installed voices through the
`AppExtensionCatalog` for the `com.microsoft.voice.model.1` contract and raises
`VoicesChanged` on install/update/uninstall.

## Repo-specific conventions & gotchas

- **Model binaries are never redistributed.** Each `*.bin` in a voice package is a
  ~705-byte plaintext EULA header + 8-byte tag + a raw ONNX ModelProto.
  `Internal.ModelExtractor` strips the header by scanning for the first ONNX
  `ir_version` protobuf tag (`0x08 <1..15> 0x12`) rather than a fixed offset, so
  keep that scan intact if the header format shifts. Do not commit or ship voice
  bytes; the EULA (reference 2774316) forbids it.
- **Streaming custom ops are rewritten at load.** The vocoder uses `Streaming*`
  ops in the `test.customop` domain that stock ORT can't run.
  `Internal.StreamingOpRewriter` renames them to standard ops (`StreamingConv` →
  `Conv`, etc.), drops the `streaming_control` input/graph-input, promotes scalar
  Conv attributes to int-lists, and strips custom opset imports. This is
  non-streaming batch inference; per-chunk streaming state is intentionally lost.
- **Reference-pipeline parity is intentional.** Magic values (attention seeded
  one-hot at position 0, gate compared as a raw sigmoid scalar not a logit,
  warmup guard, 0.9 peak normalize, tensor names/shapes in the decoder loop) match
  a reference Python pipeline. Don't "clean these up" without preserving behavior.
- **26 kHz vs 24 kHz.** The vocoder natively emits 26000 Hz. Rewrapping the same
  samples with a 24000 Hz WAV header (see the Demo) slows/lowers pitch ~8% to
  match the Azure Ava reference. This is a deliberate playback trick, not a bug.
- **Threading:** `NaturalVoiceSpeaker`, `NaturalVoiceEngine`, and `SapiPhonemizer`
  are thread-hostile. Construct one per voice, keep it warm across phrases (model
  load dominates first-call latency), and serialize calls.
- **Namespacing:** public API in `WindowsNaturalVoices`; helpers in
  `WindowsNaturalVoices.Internal`. Windows-only public types carry
  `[SupportedOSPlatform("windows")]`. Nullable and implicit usings are enabled.
- **G2P fallback:** for locales SAPI can't handle, callers supply phoneme ids
  directly via `PhonemeTable.TryGetArpabet`/`TryGet`; ARPABET keys are looked up
  locale-prefixed (e.g. `en-us_iy1`).
