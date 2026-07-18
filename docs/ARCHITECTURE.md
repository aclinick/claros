# Architecture

`WindowsNaturalVoices` turns a string into audio in three stages, plus a
discovery layer that finds the installed voices. Everything runs offline on the
CPU with stock NuGet packages.

## Pipeline overview

```
text
 │
 ▼
SapiPhonemizer ──▶ phoneme ids ──▶ NaturalVoiceEngine ──▶ codec tokens ──▶ Vocoder ──▶ PCM samples
 (G2P via SAPI)                     (encoder + decoder)     (C20Hz/C40Hz)   (ONNX)      (26 kHz mono)
```

`NaturalVoiceSpeaker` is the one-call facade that wires the three components
together and disposes them as a unit. `VoiceCatalog` sits in front to enumerate
the voices a machine has installed.

## Components

### VoiceCatalog: discovery

Opens the `AppExtensionCatalog` for the `com.microsoft.voice.model.1` contract
that every Natural Voice package declares. `ListVoicesAsync` queries the OS
(never cached) and builds a `VoiceInfo` per voice; `VoicesChanged` fires on
install/update/uninstall so an app can rebuild its list without polling.
Metadata comes from each package's `Tokens.xml` via `TokensXmlParser`, with the
installed path used to locate the model binaries.

### SapiPhonemizer: grapheme to phoneme

Drives the shipped Windows text preprocessor (`MSTTSLoc_OneCore.dll`, believed to
be closely related to the frontend Azure Speech uses, though this shared-frontend
link is an inference, not documented) through
`System.Speech.Synthesis.SpeechSynthesizer.PhonemeReached`, with audio output
set to null. It captures the frontend's IPA output, maps it to the acoustic
model's ARPABET keys with `IpaPhonemeMap`, looks the keys up in the voice's
`PhonemeTable`, and returns a phoneme-id list bracketed by `Bos`/`Eos`. No
separate G2P engine is needed for SAPI-supported locales; callers can supply
phoneme ids directly for locales SAPI cannot handle.

> **Known limitation: this is the weak link.** The acoustic model and vocoder
> reproduce Microsoft's neural voices almost exactly, so overall quality is now
> gated by this front end, not by inference. Scraping IPA out of SAPI's
> `PhonemeReached` event and re-deriving stress heuristically (`IpaPhonemeMap`,
> the `atWordStart` flag) is a lossy stand-in for the real neural text frontend.
> That frontend is not missing from the machine: community reverse-engineering
> (the `NaturalVoiceSAPIAdapter` project) shows the on-device Natural voices are
> hosted by the **Azure Embedded Speech runtime** that ships in Windows
> (`Microsoft.CognitiveServices.Speech.extension.embedded.tts.dll`), and the
> installed voice package can be driven offline via `EmbeddedSpeechConfig.FromPath`.
> (This is a community-observed finding, not official Microsoft documentation.)
> The best end state is therefore to reuse that real frontend (capturing the exact
> phone-id sequence it feeds this acoustic model) behind an `ITextFrontend` seam,
> with the SAPI path as a documented fallback. See `docs/ROADMAP.md` (“The front
> end: reuse Microsoft's, don't reinvent it”) for the ranked plan. This is
> precisely the public surface this library exists to demonstrate Microsoft should
> ship.

### NaturalVoiceEngine: acoustic model

Loads `hd_am_v5_encoder.bin` and `hd_am_v5_decoder.bin`, running each through
`ModelExtractor` to strip the plaintext EULA header before handing the raw ONNX
to `InferenceSession`. Inference runs the encoder once over the phoneme
sequence, then an **autoregressive decoder loop** that threads attention state,
two banks of LSTM state (20 Hz and 80 Hz), an attention context, and a stop
gate. Attention is seeded one-hot at position 0 so decoding starts on the BOS
phone. The loop ends when the stop gate crosses `StopThreshold` (after a warmup
guard) or the `MaxDecoderSteps` safety cap is hit. Output is `CodecTokens`: two
discrete token streams (`C20Hz`, `C40Hz`), not audio.

### Vocoder: tokens to waveform

Loads `hd_device_vocoder_v6_streaming.bin`, strips the header with
`ModelExtractor`, then runs `StreamingOpRewriter` before handing the model to
`InferenceSession`. The rewrite is the key trick: the shipped vocoder uses a
`Streaming*` custom-operator family in the `test.customop` domain that stock
ONNX Runtime cannot execute. Each wrapper is renamed to its standard ONNX op
(`StreamingConv` → `Conv`, …), its `streaming_control` state input is dropped,
scalar Conv attributes are promoted to the int-lists the standard ops require,
and the custom opset imports are stripped. `Synthesize` rearranges the codec
tokens to the channel-major layout the graph expects, runs inference, and
peak-normalizes the result to 0.9. Output is mono PCM at **26000 Hz**.

## Key design decisions

- **Read at runtime, never redistribute.** Model bytes are read from the
  installed package on the running machine. `ModelExtractor` finds the ONNX
  payload by scanning for the first `ir_version` protobuf tag rather than a fixed
  offset, so it survives header-length changes.
- **Non-streaming batch inference.** Rewriting the streaming ops discards the
  per-chunk state buffer, so each phrase is synthesized as a single
  non-streaming inference. Real-time streaming would require reimplementing the
  `Streaming*` operators or bringing in a permissive streaming vocoder.
- **Reference-pipeline parity.** The attention seed, raw-sigmoid gate comparison,
  warmup guard, 0.9 peak normalization, and the exact decoder tensor
  names/shapes match a reference Python pipeline and must be preserved.
- **26 kHz vs 24 kHz.** The vocoder natively emits 26000 Hz. Rewrapping the same
  samples with a 24000 Hz WAV header slows and lowers pitch ~8% to match the
  Azure Ava reference timing; it is a deliberate playback choice, not a bug.
- **Thread-hostile, kept warm.** `NaturalVoiceSpeaker`, `NaturalVoiceEngine`,
  and `SapiPhonemizer` are single-threaded; construct one per voice, serialize
  calls, and reuse across phrases because model load dominates first-call latency.

## Namespaces

- `WindowsNaturalVoices`: the public API (`VoiceCatalog`, `VoiceInfo`,
  `NaturalVoiceSpeaker`, `NaturalVoiceEngine`, `Vocoder`, `SapiPhonemizer`,
  `PhonemeTable`, `CodecTokens`, `WaveformResult`, `WaveFile`, option records).
- `WindowsNaturalVoices.Internal`: implementation shims (`ModelExtractor`,
  `StreamingOpRewriter`, `IpaPhonemeMap`, `TokensXmlParser`). Exposed to the test
  assembly via `InternalsVisibleTo`.

## Testability boundary

The pure-logic units (header extraction, streaming-op rewriting, the phoneme
table, the IPA→ARPABET map, `Tokens.xml` parsing, WAV writing, and the vocoder's
tensor-layout/normalization helpers) are covered by unit tests with synthetic
inputs and need no installed voice. The runtime-integration paths (`VoiceCatalog`
WinRT enumeration, the ONNX inference in `NaturalVoiceEngine`/`Vocoder`, and
`SapiPhonemizer`) require a real installed voice and hardware, so they are
exercised by running the Demo rather than by unit tests.
