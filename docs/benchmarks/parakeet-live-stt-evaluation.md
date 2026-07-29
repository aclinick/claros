# Parakeet TDT 0.6b as a live call-listener STT engine: evaluation

**Verdict: Parakeet is an excellent batch/offline transcriber, but it is the wrong
model for a live listener whose output is immutable and feeds a reasoning LLM.**
For that job the on-device Live Captions streaming recognizer is the right engine.
Parakeet earns its place on the recorded / post-call side, where it can see the
whole audio and its best-in-class inverse text normalization (ITN) is a real win.

Measured 2026-07-20 on a Snapdragon X (ARM64) laptop, CPU only, against the
Contoso-Finance mortgage-advisory call (`mortgage-call-stereo.mp4`, 58 s, stereo:
left = advisor / Anna, right = customer / Mark).

---

## 1. The evaluation contract (why this is a fair test)

Every engine was driven under one identical **strict live-call contract**, because
a live listener has no special knowledge of the audio and cannot revise the past:

1. **Causal, no lookahead.** The listener can only react to audio that has already
   arrived. Audio can come at any moment (mic or incoming channel); the recording
   only *simulates* a live call.
2. **Immutable finals.** The instant a sentence is emitted it is sent downstream
   to the reasoning LLM. It can never be retracted or rewritten. This is what a
   voice agent (e.g. GPT voice) must do in a live environment.
3. **Past audio is read-only context.** An engine may re-read already-heard audio
   to help transcribe *new* audio, but text it already emitted stays frozen.
4. **One recognizer per call leg** (advisor + customer), matching the Contoso
   Mac listener (Apple SpeechAnalyzer), so speaker attribution is exact.

The streamer has zero knowledge of what is coming. This is the same harness for
all engines: the only variable is the model.

---

## 2. Why "offline" is the crux

> **Scoping correction (2026-07-29).** The sentence below describes **the ONNX
> export this evaluation used**, not the Parakeet architecture in general. NeMo
> can export a *cache-aware / stateful* FastConformer that carries encoder state
> between chunks, but that is not the default export and was not what we ran. See
> §11 for what this changes and what it does not.

The stateless Parakeet TDT export behaves as an **encoder model with no carried
streaming state.** To transcribe, it runs its encoder over a *whole buffer at
once*, and every output token is conditioned on the **entire window**, including
audio that arrives *after* the word being emitted. That wide context is exactly
what makes its ITN so good: it sees
`...six hundred ten thousand dollars in stocks averaging six point two...`
as one span and *rewrites* it to `$610,000 ... 6.2%`.

That rewrite is precisely what breaks under a live, immutable contract. To emit a
final you must commit before you have heard the rest of the utterance, but the ITN
rewrite needs the rest of the utterance. The model is asked to do two contradictory
things at once.

Live Captions has no such conflict. It is a **true streaming RNN-T**: it carries
encoder/decoder hidden state frame to frame, so "context" is already baked in and
free, with no re-encoding. Its sentence committer freezes a sentence only once the
*next* sentence has begun, so finals are stable by construction.

---

## 3. Parakeet under the live contract: two ways to lose

Both were reproduced with a pure-.NET port of the Parakeet TDT decode loop
(byte-identical to the Python `onnx-asr` reference), driven by the strict-live
harness `spike-parakeet-net/Program.cs`.

### Failure mode A: small window -> ITN collapses
Emit as soon as a phrase ends (VAD fragment). The model never sees enough context,
so ITN regresses to the exact failure that got Nemotron 0.6B rejected:

```
"six hundred ten thousand dollars"   (lowercase, spelled out)
```

instead of `$610,000`. Short isolated fragments starve the model of context.

### Failure mode B: rolling context window -> ITN returns, but too slow and it duplicates
Keep re-feeding ~10 s of past audio each tick so ITN survives. It does:

```
[00:30 <- 2.2s]  You have $610,000 in stocks averaging 6.2% return and a
                 $320,000 mortgage at 5.5% interest, fixed until January when it resets.
```

But you pay to **re-encode the whole rolling window on every tick**, and because
each re-transcription is a fresh, differently-punctuated hypothesis that an
immutable harness cannot reconcile, the same sentence is emitted more than once:

```
[00:03 <- 1.4s]  Hi Mark, good to see ye.
[00:04 <- 2.3s]  Hi Mark, good to see ye.     <- duplicate final, cannot be retracted
...
[00:47 <- 9.6s]  But remember, your investments are earning more than the loan is costing you.
[00:47 <- 4.1s]  And mortgage interest is often tax deductible, which lowers the real cost.
```

Duplicated / conflicting finals going to the LLM is a correctness problem, not a
cosmetic one.

---

## 4. Measured comparison (same machine, same 58 s call, 2 legs, CPU)

| Engine | First emit | Peak RAM (2 legs) | Real-time (2 legs)? | ITN quality | Live fit |
| --- | --- | --- | --- | --- | --- |
| **Live Captions (this library)** | 4.43 s | **507 MB** | Yes (60.4 s wall / 58 s) | spelled out (`six hundred ten thousand dollars`), **no ITN** | **Native** |
| **Parakeet TDT 0.6b (strict-live)** | ~1.4 s | **1778 MB** | **No** (right leg 0.7x RT) | `$610,000`, `6.2%` only with 10 s rolling window; else collapses; duplicates finals | **Poor** |
| Apple SpeechAnalyzer (macOS, ANE) | 3.93 s | ~440 MB (2x220) | Yes | `$610,000`, `6.2%` | Native |
| WinAI Speech Preview (NPU) | 3.51 s | ~6400 MB (2x3200) | Yes | Best ITN (Whisper Turbo) | Native (NPU) |
| Nemotron 0.6B (Foundry Local) | 1.22 s | ~1750 MB (2x875) | Yes | Good words, no clean sentence breaks | Poor |
| Whisper small (CPU ONNX) | 2.25 s | ~1200 MB (2x600) | Yes | LOW: hallucinates $10,000 / 312% | Poor |

Parakeet numbers are from `spike-parakeet-net` (context=10 s, eval cadence=1.0 s):
- Left leg (advisor): 51.9 s compute / 58 s audio = **1.1x RT (barely OK for one leg)**.
- Right leg (customer): 78.4 s compute / 58 s audio = **0.7x RT (falls behind real time)**.
- Peak working set **1778 MB** for a single leg's process. A real two-leg call needs
  two such workloads concurrently, so the picture only gets worse.

Live Captions numbers are a fresh run of `samples/TranscriptionBenchmark` on the
same machine and clip: **507 MB peak for both legs, real-time, stable finals.**

So on the same hardware Parakeet costs **~3.5x the memory** of Live Captions and
**cannot keep up with real time on both legs**.

**Correction (verified 2026-07-20):** an earlier draft claimed Live Captions
"already delivers" the same ITN. That is wrong. Probing the raw recognizer
hypothesis shows our embedded Live Captions path spells numbers out
(`six hundred ten thousand dollars`, `six point two percent`) and never renders
`$610,000` / `6.2%`. ITN/display-form runs only at native segment finalization,
which we suppress to avoid the ARM64 finalizer crash. So neither cheap CPU engine
(Live Captions, Nemotron) does ITN here; only WinAI (Whisper Turbo, NPU) and
Parakeet (offline) do. See `ALL-ENGINES-CHAT-OUTPUT.md` for the per-engine reco
output.

**How much does ITN actually matter here?** Not much, for this pipeline. The
transcript's consumer is a reasoning LLM, and an LLM reads
`six hundred ten thousand dollars at five point five percent` just as correctly as
`$610,000 at 5.5%`; it only costs a few more tokens. ITN is a **display** nicety
(nicer chat bubbles, easier deterministic regex extraction), not a **correctness**
requirement for an LLM-consumed transcript. That reframing matters: it removes the
one real quality knock against Live Captions, because its lexical numbers are fine
for the LLM, and it can be prettied up on the display side later (see section 7).

---

## 5. Where Parakeet is genuinely the right tool

None of the above is a knock on the model. Given the whole audio at once, Parakeet
is excellent:

- Full 58 s pass, one shot: `$610,000 in stocks averaging 6.2% return and a
  $320,000 mortgage at 5.5% interest`, correct casing and punctuation, clean
  per-channel speaker separation, at roughly **25x real time**.
- Best-in-class on-device ITN with no NPU and no cloud (CC-BY-4.0, int8 ~631 MB
  of weights).

That is the profile of a **batch / post-call transcription engine**: recordings,
voicemail, meeting notes, and the clean post-call transcript + summary for this
very demo, where it can see the entire call and nothing is immutable.

---

## 6. Recommendation

- **Live listener (text -> LLM in real time): Live Captions.** It is the only
  engine here that is causal, immutable-final, real-time, and light (507 MB, CPU)
  at once, and it is a streaming RNN-T, the architecturally correct shape. Its
  lexical numbers (no ITN) are fine for an LLM consumer, so that is not a real
  knock. The one genuine defect to fix is the sentence committer dropping /
  duplicating finals on terminator flicker (it lost the "$610,000 mortgage"
  sentence entirely).
- **Idle-time refiner / post-call transcript: Parakeet TDT 0.6b.** This is its
  correct role. Given a complete window of audio it is excellent: `$610,000 ...
  6.2% ... $320,000 ... 5.5%`, correct casing/punctuation, clean per-channel
  separation, ~25x real time, best-in-class on-device ITN (CC-BY-4.0, int8 ~631 MB).
  Use it exactly where it shines - offline, over a finished window - not in the
  live path.
- **Do not adopt Parakeet as the *live* STT engine.** Forcing an offline attention
  model into a live immutable-finals pipeline makes it fight itself: too small a
  window destroys the ITN that is its whole advantage, and a window large enough to
  keep the ITN is not real-time on CPU (0.7x RT) and emits duplicate, unretractable
  finals.

Right model, wrong job - for the *live* listener. But Parakeet has a real job in
the architecture below.

---

## 7. Recommended architecture: tiered live engine + idle-time refiner

The two engines are not competitors; they are two tiers of one system, each doing
what it is good at. The key realization is that a chat bubble has **two versions**:
the frozen text already sent to the LLM (immutable, contract-bound) and the text
shown on screen (a view, freely re-renderable). The refiner only ever touches the
display copy.

```
                 per-leg 16 kHz PCM (one recognizer per speaker)
                                  |
        +-------------------------+--------------------------+
        |  TIER 1: LIVE (Live Captions, streaming RNN-T)     |
        |  - causal, emits committed sentence finals         |
        |  - raw lexical text -> LLM INSTANTLY (frozen)      |
        |  - also renders the first-pass chat bubble         |
        +-------------------------+--------------------------+
                                  |
                   (last N bubbles + a short rolling PCM ring buffer per leg)
                                  |
        +-------------------------+--------------------------+
        |  TIER 2: IDLE REFINER (background thread)          |
        |  Trigger: this leg goes quiet (VAD) - usually      |
        |  because the OTHER speaker is now talking, so the  |
        |  refine runs on genuinely free compute.            |
        |  Action, cheapest first:                           |
        |   (a) deterministic ITN pass over last N bubbles   |
        |       -> "$610,000", "6.2%" (microseconds), OR     |
        |   (b) re-run Parakeet over the buffered window     |
        |       with full context -> higher-quality, ITN'd   |
        |       transcript that also RECOVERS any sentence   |
        |       the live committer dropped.                  |
        |  Updates the DISPLAY copy of the bubbles in place. |
        +----------------------------------------------------+
```

Why this works:

- **Turn-taking gives free time slots.** Two speakers alternate, so each leg's
  silence is the other leg's speech. Anna's bubbles get refined precisely while
  Mark is talking. No added steady-state load.
- **The live contract is never violated.** The LLM already received the raw final
  the instant it was spoken; the refiner only re-renders pixels. (If you ever want
  the LLM to benefit from a correction, send it as a *new* addendum turn - never a
  rewrite.)
- **Parakeet lands in its wheelhouse.** The refine window is small, already
  complete, and has no real-time deadline - exactly the offline/batch conditions
  where Parakeet is best-in-class. Its live weaknesses (latency, duplicate finals)
  simply do not apply off the hot path.
- **It doubles as a safety net.** A full-context Parakeet re-pass over the buffered
  window would recover the "$610,000 ... mortgage" sentence the live committer
  dropped - so the refiner fixes both cosmetics (ITN) and correctness (drops).

Design questions to settle when building it:

- Silence trigger: per-leg VAD vs. a debounce after the last final.
- Window size N: last few bubbles vs. last ~10-15 s of audio (Parakeet needs the
  PCM retained, so keep a short rolling ring buffer per leg).
- Cancellation: the speaker can resume mid-refine; the refine is best-effort and
  must yield to the live path (abandon and let the live bubble stand).
- Refiner choice: ship (a) deterministic ITN first (no crash risk, trivial), add
  (b) Parakeet re-pass only if the higher quality / drop-recovery is worth the
  ~631 MB and the buffering.

Priority order: **(1)** fix the committer drop/duplicate on the live path
(correctness - the LLM must see the figure at all); **(2)** optional deterministic
display-ITN pass for nicer bubbles; **(3)** optional Parakeet idle refiner for
best-quality bubbles + drop recovery.

---

## 8. Apple SpeechAnalyzer (macOS) - the reference bar

To calibrate "how good is achievable on-device," the *exact same* strict-live
two-leg harness was ported to Swift and run against Apple's on-device
`SpeechAnalyzer` on an Apple Silicon Mac (macOS 27.0, Xcode 26.5). One stereo
capture, de-interleaved to two legs, one `SpeechAnalyzer` + `SpeechTranscriber`
per leg, 100 ms chunks, real-time paced, finals-only bubbles via the *same*
ported `SentenceCommitter`. Full run + transcript:
`Contoso-Finance@feature/macos-speech-worker:MacBench/APPLE_RESULTS.md`.

Apple clears **every** bar at once:

| Property | Apple result |
| --- | --- |
| Immutable finals | **Yes** - 21 append-only bubbles, never retracted |
| ITN (`$610,000`, `6.2%`) | **Yes** - native, inserted at finalization |
| Got the `$610k` sentence | **Yes** - recovered (Live Captions dropped it) |
| Speaker attribution | **Exact** - by source (per-leg recognizer) |
| Real-time | **Yes** - 58.3 s wall for 58.0 s audio |
| Init / first-final | **~65-107 ms** / **~4.0 s** |
| Memory | ~27 MB process RSS; **~220 MB per transcriber / ~440 MB two legs** (shared Speech daemon) |

Only wrinkle: Apple splits the one long spoken advisor sentence into three finals
at its own clause punctuation (`...return.` / `...interest.` / `Fixed until
January...`); numbers and attribution are exact, only bubble granularity is finer.

Apple is macOS-only, so it is the **target**, not a Windows deployment option -
but it proves the tier (immutable + ITN + real-time + light) is achievable
cheaply on-device.

---

## 9. Strategic takeaway: invest in Live Captions, not third-party models

The comparison confirms the intuition that **Live Captions is Microsoft's real
competitor to Apple's on-device STT - and the only Windows engine already in the
same tier.** The recommendation to Microsoft is to invest in its *own* Live
Captions model rather than integrating/optimizing third-party models to chase the
same bar.

Why the data supports this:

- **Live Captions already does the structurally hard things.** Streaming, causal,
  immutable per-sentence finals, real-time, on **CPU**, at **~500 MB / 2 legs**.
  The other Windows engines fail at least one of these *by construction*: WinAI
  (Whisper Turbo) never commits an immutable final (rolling partial only) and
  needs the NPU + ~6.4 GB; Nemotron emits one blob per leg with no sentence
  breaks; Parakeet duplicates finals and falls below real-time in the live path.
- **The gap to Apple is small and self-inflicted, not architectural.** Live
  Captions' only two deficits vs Apple are both fixable inside Microsoft's own
  stack:
  1. **ITN** - the ITN model *already ships inside the Live Captions package.* We
     lose it only because we suppress the native segment finalizer (it
     access-violates on ARM64). That is a finalizer-stability bug, not a missing
     capability.
  2. **The dropped `$610k` sentence** - a bug in *our* `SentenceCommitter`
     (emit-index skip on terminator flicker), not the recognizer; the raw
     hypothesis contained the sentence.
- **Shortest path to Apple-parity is first-party.** Stabilize the ARM64 finalizer
  (re-enabling in-box ITN) and fix the committer drop, and Live Captions reaches
  the Apple tier at ~0.5 GB CPU - versus optimizing a third-party model onto the
  NPU at ~6.4 GB to reach a bar Microsoft's own model is already ~90% of the way
  to.

The one-line version: **invest in the Live Captions finalizer + ITN path (both
already in-box) rather than integrating third-party models - it is the shortest
path to Apple-parity on-device STT, and it is Microsoft's to own.**

Two honesty caveats to keep the claim credible:

- "Apple-parity" means the **tier** (immutable + ITN + real-time + light), not
  identical segmentation - Apple itself splits one long sentence into three
  bubbles.
- Parity is **contingent** on fixing the two known defects above. Today Live
  Captions is "closest, with a fixable gap," not "already equal."

---

## 10. Watch item: Deepgram Nova-3 (the announced on-device NPU challenger)

Nova-3 is the most important engine to track, because on paper it is the **first
external model that satisfies this entire live contract *and* has ITN** - unlike
every Windows engine we measured. It changes the make-vs-buy conversation, so it
gets its own section even though it is not yet benchmarkable on-device.

**What it is (from Deepgram's own docs):**

- **Genuine streaming with immutable finals.** The streaming API emits
  `interim_results` (`is_final:false`, revisable) that stabilize into
  `is_final:true` finalized segments, plus `speech_final:true` at an endpoint.
  That is the *same RNN-T-style commit contract as Apple and Live Captions* - it
  commits immutable finals safe to send to an LLM. This is categorically unlike
  WinAI/Whisper (rolling partials, never commits) and Parakeet (offline re-encode,
  duplicates).
- **Native ITN + formatting.** `smart_format`, `punctuate`, "enhanced numeric
  recognition," and live redaction of ~50 PII entities - i.e. it renders
  `$610,000 / 6.2%`, the thing Live Captions loses.
- **Native per-channel attribution.** Streaming results carry a `channel:[A,B]`
  field for multichannel audio, plus diarization - it supports our two-leg
  (advisor L / customer R) model directly.
- **Accuracy claims (vendor):** 6.89% WER on production audio; ~53-54% lower
  streaming WER and 47.4% lower batch WER than "competitors"; preferred over
  Whisper 7/7 languages; real-time multilingual code-switching across 10 languages.
- (Deepgram also now ships **Flux**, a separate voice-agent-oriented streaming
  model with model-integrated end-of-turn detection at "Nova-3 level accuracy" -
  another sign the market is converging on exactly this live-agent contract.)

**Availability - the critical distinction:**

- **Nova-3 the model is GA today via the cloud API** (`v1/listen?model=nova-3`,
  options `nova-3-general` / `nova-3-medical`) and is offered **self-hosted /
  on-prem** for enterprise. All the streaming/finals/ITN/multichannel behavior
  above is documented and live *there*.
- **Nova-3 on-device on Snapdragon (Hexagon NPU) is a Deepgram x Qualcomm
  partnership announcement, not a shippable on-device SDK.** As of this writing
  there is no GA embedded runtime, no developer download, and **no published
  on-device latency, memory, or model-size numbers**. The announcement references
  the Jan 2026 Snapdragon X2, so it is very recent and directional ("optimizing
  for" / "bringing to"), aimed at OEMs. Whether the Hexagon build exposes the same
  API surface as the cloud is unstated.

**Why this does not (yet) change the recommendation:**

- The only *shipping, measurable, on-device* engines in the usable-live tier
  remain **Apple** (macOS / ANE) and **Live Captions** (Windows / CPU). Nova-3
  on-device is the credible **future** NPU challenger, but from a benchmarking
  standpoint it is vaporware-until-shipped - its ~0.5 GB-CPU-vs-NPU footprint
  question is unanswerable until Deepgram ships the embedded SDK.
- It actually *validates* the target tier: Apple (ANE) and Nova-3 (NPU) both prove
  immutable-finals + ITN + streaming on-device is real and worth reaching - which
  is exactly the bar Microsoft's own Live Captions is ~90% of the way to on CPU.
- It sharpens, rather than weakens, the make-vs-buy call. Nova-3 is the concrete
  "buy a third-party model for the NPU" option; the argument for investing in the
  in-box CPU Live Captions instead now rests on: commercial licensing (Deepgram is
  enterprise-priced), NPU dependency + unknown on-device memory vs. Live Captions'
  proven ~0.5 GB CPU, first-party data/privacy and control, and no per-seat cost.

**What would move it into the measured comparison:**

1. Benchmark Nova-3 *today* through the identical harness via the **cloud or
   self-hosted** API on the mortgage call - a real measured quality/finals/ITN row,
   but off-device (so not comparable on the memory/hardware axis).
2. Re-benchmark **on-device** once Deepgram ships the Snapdragon/Hexagon SDK, to
   answer the only open question: does it hold the tier at a competitive on-device
   footprint, or does it pay WinAI-class NPU memory to do so?

Bottom line: **Nova-3 is the one to watch - the first third-party engine that
architecturally matches the whole contract - but on-device it is announced, not
available, so it does not yet displace Live Captions as Microsoft's shortest path
to on-device Apple-parity.**

---

## 11. Addendum (2026-07-29): what shipping third-party Parakeet apps do differently

Third-party Windows apps *do* run Parakeet for live transcription, including on
plain CPU. That is not in dispute and it is worth being precise about why their
result and ours differ, because two of the five reasons are things we could have
done and did not.

### The one that is a requirements difference, not a technique

**They are allowed to rewrite what they already showed.** A captioning or
dictation UI paints volatile hypotheses and repaints them as context arrives; the
user sees text settle. That is a perfectly good product, and Parakeet's wide-window
ITN is an *asset* there, because the final repaint is the good one.

This evaluation deliberately forbade that (§1, rule 2), because the consumer is a
reasoning LLM in a live call, and a sentence handed to it cannot be unsent. Nothing
about our measurement contradicts theirs; we were grading a different exam. Any
comparison that ignores this is comparing a repaintable display against an
immutable commit.

### The two that were our own configuration, and are fixable

**We used the stateless ONNX export and re-encoded a rolling window.** Our harness
held a 10 s context and re-ran the encoder every 1 s, so roughly ten seconds of
audio was re-encoded per second of speech - about 10x redundant compute. That, not
the model, is what produced 0.7x real time on the second leg. NeMo can export a
**cache-aware / stateful** FastConformer that carries encoder state across chunks
and encodes each frame once; it simply is not the default export, and the
`onnx-asr` path we used does not expose it. Several third-party stacks either use
such an export or keep the decoder stateful in application code.

**We re-encoded on a fixed 1 s cadence rather than at pauses.** Shipping apps
typically chunk on VAD/silence boundaries, which both cuts how often the encoder
runs and puts chunk edges where they damage the transcript least.

### The two that are scope, and were deliberate

**Two concurrent legs, not one.** Every number here is a two-recognizer call so
speaker attribution is exact. Most demos - including the 2-vCPU real-time Space on
Hugging Face - transcribe a single microphone. Our peak of 1778 MB was for one
leg's process; a two-leg call needs two.

**Peak RAM was measured, not tuned.** We did not pursue quantization or runtime
tuning beyond the INT8 weights we already used, because the memory verdict was
already decided by the 507 MB baseline.

### What this changes

- **Overstated:** that Parakeet cannot hold real time on CPU. With a cache-aware
  export and VAD chunking it very likely can, for one stream. Our figure measures
  our configuration.
- **Unchanged:** that you cannot have *immutable finals* and *Parakeet-quality ITN*
  at the same time. That conflict is architectural, not a tuning problem - the ITN
  rewrite needs audio that has not arrived when the final must be committed. §3's
  two failure modes are both instances of it.
- **Unchanged:** the memory comparison. Even generously configured, Parakeet's
  working set is multiples of the 507 MB streaming recognizer, and that gap is what
  the platform argument rests on.

### Honest bottom line

Parakeet is a better *transcriber* than the conclusion above might suggest, and a
Windows app that shows settling text is right to use it. It remains the wrong
engine for this library's live-listener contract, and the reason is the contract -
not a benchmark we could re-run our way out of.

---

## 12. Retest (2026-07-29): the real-time claim was wrong

Prompted by the observation that third-party Windows apps run Parakeet live on
CPU, the harness was rebuilt and re-run on the same 58 s call, same machine, same
stateless INT8 ONNX export. The only variables swept were the two knobs §11
identified as ours: how much left context is re-encoded (`CONTEXT`) and how often
(`EVAL`).

| Context | Cadence | Compute for 58 s | Real time | Duplicate finals | ITN |
| --- | --- | --- | --- | --- | --- |
| 10 s | 1.0 s | 36.2 s | 1.6x | 7 | yes |
| 10 s | 2.0 s | 16.0 s | 3.6x | 4 | yes |
| 5 s | 2.0 s | 14.6 s | **4.0x** | 3 | yes |
| 3 s | 2.0 s | 8.7 s | 6.7x | 2 | yes |
| 5 s | 4.0 s | 6.0 s | **9.7x** | 2 | yes |
| 2 s | 2.0 s | 9.5 s | 6.1x | 4 | yes |

Peak working set stayed between 1517 MB and 1789 MB per leg across every setting.

### Two claims in this document were wrong

**"Cannot hold real time" is wrong.** At the original 1 s cadence this rebuild
measured 1.6x real time, not the 0.7x recorded in §4, and simply halving the
cadence reached 3.6x. With a 5 s context evaluated every 4 s it runs at **9.7x
real time** - nearly an order of magnitude of headroom on CPU, with the plain
stateless export and no cache-aware model at all. The original figure reflected a
pathological configuration (re-encoding ten seconds of audio every second), not
the model.

**"Small windows collapse ITN" (failure mode A) did not reproduce.** ITN survived
at every setting tested, down to a 2 s context: `$610,000` and `6.2%` were still
produced. Whatever caused the original collapse, it was not window size alone on
this audio.

### One claim held, and it is the one that matters

**Duplicate finals never reached zero.** Best case was 2 and worst was 7, at every
point on the frontier including the fastest. Under this contract a sentence is
emitted the moment it is committed, and re-transcribing overlapping context makes
the model produce that sentence again. Faster settings reduce duplicates only
because they re-transcribe less often, never because the conflict goes away.

The memory gap also held: ~1.5-1.8 GB per leg against the streaming recognizer's
507 MB for two, so a two-leg call is roughly 6x the working set.

### The honest, narrower verdict

Parakeet is **fast enough for live transcription on CPU by a wide margin**, and
this document was wrong to imply otherwise. It remains unsuitable for *this*
library's listener for two narrower reasons: it re-emits sentences it has already
committed, and it costs several times the memory of the streaming recognizer.

One caveat kept deliberately open: some of the duplication is plausibly the
harness's naive re-emission rather than the model's, and a committer that
suppressed exact repeats would cut it further. What such a layer cannot fix is a
*revised* final - the model changing a sentence it already emitted - because under
an immutable contract the revision can never be applied. Establishing how much of
the residue is revision rather than repetition is the next measurement, and it
should be done before this table is used to argue against Parakeet anywhere.

---

## 13. Live Captions re-measured (2026-07-29), and a real bug it exposed

The Parakeet retest in §12 was only half a comparison, so `samples/TranscriptionBenchmark`
was re-run on the same call, same machine, the same morning.

| | Live Captions | Parakeet TDT 0.6b (best config) |
| --- | --- | --- |
| Throughput | **81-91x real time** (un-paced, 1 leg) | 9.7x real time |
| Peak RAM, 2 legs | **~500 MB** | ~3.1 GB (1,550 MB per leg) |
| First final | 4.26 s | ~1.4 s |
| Duplicate finals | **0 exact** (21 emitted / 21 distinct) | 2-7, never zero |
| Number formatting | spells out | `$610,000`, `6.2%` |

Live Captions wins throughput by roughly 9x and memory by roughly 6x. Parakeet
keeps first-token latency and ITN. Note the two throughput figures are not
strictly commensurable: 81-91x is un-paced single-leg decode, while Parakeet's
9.7x is compute under the strict-live harness. The direction is unambiguous; the
ratio should not be quoted to two significant figures.

### The bug this exposed

The first re-run emitted **44 finals for 21 distinct sentences**, repeating single
lines up to five times - worse than the Parakeet behaviour this document
criticises. The archived 2026-07-20 run of the same audio emitted 16, so severity
was timing-dependent rather than deterministic.

The cause was subtle. The recognizer's segmentation oscillates at the head of the
transcript - `"Hi, Mark." + "Good to see you."` merges into
`"Hi, Mark, good to see you."` and splits back again. `SentenceCommitter`
reconciled by longest common prefix, so every flip changed position 0,
invalidated the whole prefix, and made every later sentence look new. One
unstable sentence re-surfaced the entire tail on each flip.

Fixed in `5e11d7d` by keeping the prefix diff - it is what recovers an in-place
revision, and is why the `$610,000` sentence is captured at all - and adding
exact-text tracking so a position that merely looks new does not re-emit.
Re-measured twice: 21 emitted, 21 distinct, transcript still complete.

Two residual lines are genuine revisions differing only in punctuation or in
swallowing the following sentence, so 21 emissions reduce to 19 semantically
distinct. That remainder needs positional replace rather than text dedup, and is
tracked in issue #20.

### Why this is recorded here

Both engines were measured under the same contract, and both turned out to
re-emit committed lines. Parakeet's residue is architectural; ours was a
reconciliation bug in this library and is now largely fixed. A comparison that
had only ever measured the competitor would have missed it.
