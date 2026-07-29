# Benchmarks

Measured evidence behind the claims in the pitch deck and the README. Everything
here was captured on real hardware against a real recording; nothing is modelled
or estimated.

## Contents

| File | What it is |
| --- | --- |
| [`parakeet-live-stt-evaluation.md`](parakeet-live-stt-evaluation.md) | Full evaluation of six STT engines under one strict live-call contract, and why the on-device Live Captions recognizer is the right engine for a live listener. |
| [`raw/`](raw) | The unedited transcript output and per-engine run logs the evaluation summarises. |

## The test

A real 58 s two-party mortgage-advisory call (stereo: advisor left, customer
right), on a Snapdragon X (ARM64) laptop, CPU only, one recognizer per call leg
so speaker attribution is exact. Every engine ran under the same harness — the
only variable was the model.

The contract each engine had to satisfy is what makes the comparison fair, and it
is the contract a real voice agent lives under:

- **Causal, no lookahead** — react only to audio that has already arrived.
- **Immutable finals** — once a sentence is emitted it goes downstream to the
  reasoning model and can never be retracted or rewritten.
- **Past audio is read-only context** — an engine may re-read what it has heard to
  transcribe new audio, but text it already emitted stays frozen.

## Headline result

| Engine | First final | Peak RAM (2 legs) | Real-time? | ITN quality | Live fit |
| --- | --- | --- | --- | --- | --- |
| **Live Captions (this library)** | 4.43 s | **507 MB** | Yes | spelled out, no ITN applied | **Native** |
| Apple SpeechAnalyzer (macOS, ANE) | 3.93 s | ~440 MB | Yes | `$610,000`, `6.2%` | Native |
| WinAI Speech Preview (NPU) | 3.51 s | ~6,400 MB | Yes | Best (Whisper Turbo) | Native (NPU) |
| Nemotron 0.6B (Foundry Local) | 1.22 s | ~1,750 MB | Yes | No clean sentence breaks | Poor |
| Parakeet TDT 0.6b | ~1.4 s | ~1,778 MB **per leg** | **No** (0.7x RT on one leg) | Good, but duplicates finals | Poor |
| Whisper small (CPU ONNX) | 2.25 s | ~1,200 MB | Yes | Low: hallucinates numbers | Poor |

Two things this establishes, and both are load-bearing for the pitch:

1. **The on-device recognizer reaches Mac-class memory on ordinary CPU silicon** —
   ~507 MB against Apple's ~440 MB, versus ~6.4 GB for the NPU-gated Whisper path.
2. **Being a true streaming model matters more than raw model quality.** Parakeet
   is an excellent *offline* transcriber and its inverse text normalization is
   genuinely better, but it re-encodes a whole window per emit, so under an
   immutable-finals contract it either loses its ITN or falls behind real time.
   Live Captions carries streaming state frame to frame, so stable finals cost it
   nothing.

The one honest gap: Live Captions ships an ITN model but the library does not
currently apply it, so numbers arrive spelled out (`six hundred ten thousand
dollars` rather than `$610,000`). That is a fixable in-box gap, not a model
limitation.

## A scoping note on the Parakeet result

Third-party Windows apps do run Parakeet live, including on CPU, and the
evaluation carries an addendum (§11) explaining precisely why their result and
ours differ. Two of the reasons were our own configuration — we used the stateless
ONNX export and re-encoded a 10 s window every second, rather than a cache-aware
export chunked on silence — so the "cannot hold real time" figure measures our
harness, not the model's ceiling.

What survives that correction is the part that matters here: a captioning UI is
free to repaint text as context arrives, and this library's listener is not,
because a sentence handed to a reasoning model cannot be unsent. Parakeet's
wide-window ITN needs audio that has not arrived when an immutable final must be
committed. That conflict is architectural, and so is the memory gap.

## Reproducing

The Live Captions numbers come from `samples/TranscriptionBenchmark`, which is in
this repo and runs against any stereo recording. The other engines were driven by
throwaway spikes that are not committed — they carry multi-hundred-megabyte model
files. The evaluation records their configuration precisely enough to rebuild
them if the comparison ever needs rerunning.
