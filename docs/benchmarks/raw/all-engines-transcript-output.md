# Live call STT: reco chat output, all engines, identical strict-live harness

**What this is.** Every engine below transcribes the *same* two-party mortgage
call (`mortgage-call-stereo.mp4`, 58 s: left = advisor / Anna, right = customer /
Mark) through the **same** engine-agnostic harness
(`AudioWorker --itranscriber-test`): 100 ms mono chunks, real-time paced, one
recognizer per leg, causal, finals are immutable (they would go to the LLM the
instant they are emitted). This is the chat-balloon output a live agent would
actually see. Measured 2026-07-20 on a Snapdragon X (ARM64) laptop.

**The ground-truth line that matters** (advisor, ~00:16):
> "You have **$610,000** in stocks averaging **6.2%** return and a **$320,000**
> mortgage at **5.5%** interest, fixed until January when it resets."

That single sentence is the whole reason on-device ITN matters, so watch how each
engine renders it.

---

## Headline result

| Engine | Hardware | Memory (2 legs) | Emits immutable finals? | Renders `$610,000` / `6.2%` (ITN)? | Chat-balloon usable live? |
| --- | --- | --- | --- | --- | --- |
| **Apple SpeechAnalyzer** (macOS 26, reference) | ANE | **~440 MB** (~220 MB/transcriber; ~27 MB process RSS + shared Speech daemon) | **Yes** (per sentence, append-only) | **Yes** | **Yes** - clean immutable bubbles, ITN, real-time, per-speaker exact. *macOS-only.* |
| **Live Captions** (this library, embedded) | CPU | **~507 MB** | Yes (per sentence) | **No** - spells out "six hundred ten thousand dollars" | Partly - clean bubbles, but **dropped the key $610k sentence** and no ITN |
| **WinAI Speech Preview** (Whisper Turbo) | NPU | **~6.4 GB** | **No** - rolling partials only | **Yes** | No - never commits a final |
| **Nemotron 0.6b** (Foundry Local) | CPU | **~1.75 GB** | One giant blob per leg | **No** - spelled out | No - no sentence breaks, whole leg is one final |
| **Parakeet TDT 0.6b** (offline, rolling window) | CPU | **~1.8 GB per leg** (worse for 2) | Yes, but duplicates | **Yes** | No - duplicate finals + 0.7x RT (too slow) |

**Apple is the reference bar; among *Windows* engines nobody wins cleanly.**
Apple SpeechAnalyzer clears every bar at once - immutable finals, native ITN,
real-time, per-speaker attribution, light footprint - but it is macOS-only, so
it is the *target* the Windows path is measured against, not a Windows option.
Of the Windows engines, the two that produce real ITN (WinAI, Parakeet) cannot
produce clean immutable finals live; the two that produce clean/streaming finals
(Live Captions, Nemotron) do not do ITN in these paths. This is the honest
picture MSFT should see.

---

## 0. Apple SpeechAnalyzer (macOS 26, ANE) - the reference bar

Run on an Apple Silicon Mac (macOS 27.0, Xcode 26.5) through a Swift port of the
*exact same* harness: one stereo capture de-interleaved to two legs, one
`SpeechAnalyzer` + `SpeechTranscriber` per leg, 100 ms chunks, real-time paced,
finals-only bubbles via the *same ported `SentenceCommitter`*. Full results:
`Contoso-Finance` branch `feature/macos-speech-worker`, `MacBench/APPLE_RESULTS.md`.

Chat balloons actually emitted (finals only, merged by arrival):

```
[+  4102ms] Anna:  Hi, Mark.
[+  4117ms] Anna:  Good to see you.
[+ 11543ms] Mark:  I've been thinking I should sell my stock and pay off the mortgage.
[+ 11570ms] Anna:  What's on your mind?
[+ 11592ms] Anna:  I hear you.
[+ 15489ms] Anna:  Let's check the numbers.
[+ 23037ms] Anna:  You have $610,000 in stocks averaging 6.2% return.
[+ 27008ms] Anna:  And a $320,000 mortgage at 5.5% interest.
[+ 30748ms] Mark:  I just want to sleep better at night.
[+ 30800ms] Anna:  Fixed until January when it resets.
[+ 34651ms] Anna:  Understandable.
[+ 34719ms] Anna:  If you paid it off, you'd eliminate that risk.
[+ 38543ms] Anna:  But remember, your investments are earning more than the loan is costing you.
[+ 46163ms] Mark:  That reset is what scares me.
[+ 49936ms] Anna:  And mortgage interest is often tax deductible, which lowers the real cost.
[+ 49955ms] Anna:  And that's okay.
[+ 53756ms] Anna:  Peace of mind is priceless.
[+ 57666ms] Mark:  Still, I hate the idea of carrying the debt.
[+ 57689ms] Mark:  Thanks, Anna.
[+ 58215ms] Anna:  We'll model both options, but the right choice is the one that lets you feel secure.
[+ 58270ms] Mark:  That's exactly what I needed.
```

- ITN: **yes** (`$610,000`, `6.2%`, `$320,000`, `5.5%`). The `$` is inserted at
  finalization; the volatile partial shows the bare number and the committed
  bubble carries the currency symbol. Native ITN survives into the finals.
- Finals: **immutable and append-only** - all 21 bubbles printed once, never
  retracted; every in-flight revision stays on the volatile stream.
- **Recovered the $610k sentence** that Live Captions dropped, and attributed
  every bubble to the correct speaker by source.
- Only wrinkle: Apple splits the one long spoken advisor sentence into **three**
  finals at its own clause punctuation (`...return.` / `...interest.` /
  `Fixed until January...`). Numbers are exact; only bubble granularity is finer.
- Cost: init **~65-107 ms**, first-final **~4.0 s**, **real-time** (58.3 s wall
  for 58.0 s audio), process RSS **~27 MB** - but the model runs in a shared
  Apple Speech daemon, so budget **~220 MB per transcriber / ~440 MB two legs**.

This is the bar: clean immutable finals, native ITN, real-time, exact
attribution, all at once. It is macOS-only, so it is the *target*, not a Windows
deployment option - but it proves the tier is achievable cheaply on-device.

## 1. Live Captions (this library, on-device embedded, CPU)

Chat balloons actually emitted (finals only, merged by arrival):

```
Anna:  Hi, Mark.
Anna:  Good to see you.
Anna:  I hear you.
Anna:  I hear you.                         <- duplicate final
Anna:  Let's check the numbers.
Mark:  I've been thinking I should sell my stock and pay off the mortgage.
Mark:  I just want to sleep better at night.
                                           <-- the $610,000 sentence should be HERE. It was DROPPED.
Anna:  Understandable.
Anna:  If you paid it off, you'd eliminate that risk.
Mark:  That reset is what scares me.
Anna:  But remember your investments are earning more than the loan is costing you
       and mortgage interest is often tax deductible which lowers the real cost and that's OK.
Anna:  Peace of mind is priceless.
Mark:  Still, I hate the idea of carrying the debt.
Mark:  Thanks, Anna.
Mark:  That's exactly what
Anna:  Will model both options, but the right choice is the one that lets you feel secure
```

Two problems, both verified:

- **No ITN.** The raw recognizer hypothesis (probed directly) reads:
  `You have six hundred ten thousand dollars in stocks averaging six point two
  percent return and a three hundred twenty thousand dollar mortgage at five point
  five percent interest`. It never contains "610". ITN/display-form runs only when
  the native segment *finalizer* fires, and we deliberately suppress that
  (`Speech_SegmentationSilenceTimeoutMs` set very high) because the finalizer
  access-violates on ARM64. We traded ITN for stability.
- **Dropped sentence.** The recognizer flickered the terminator
  (`resets.` -> `resets understandable` -> `resets. Understandable.`), and the
  sentence committer's emit-index skipped the whole 14 s financial sentence, so it
  never reached the chat. A short sentence ("I hear you.") also double-emitted.

Cost: **~507 MB peak for both legs, real-time.** Cheap and stable, but the two
issues above are real.

## 2. WinAI Speech Preview (Whisper Large v3 Turbo, NPU)

Best raw text quality, real ITN, but the streaming API only ever produces a
**rolling partial** that keeps getting rewritten. It never emits an immutable
final, so under the live contract there is nothing to commit as a chat balloon.

Advisor leg, successive partials (each replaces the last):

```
partial  Hi Mark, good to see you. What's on your mind?
partial   You have $610,000 in stocks, averaging 6.
partial   stocks averaging 6.2% return and a $320,000 mortgage at 5.5
partial   interest. Fixed until January when it res
partial   Remember your investments are earning more than the loan is costing you, and mortgage interest
partial   is often tax-deductible, which lowers the real cost.
partial   both options, but the right choice is the one that lets you feel
partial   secure.
```

Customer leg:

```
partial  I've been thinking I should sell my stock and pay off the mortgage. I just want to sleep better
partial   at night.
partial   That reset is what scares me.
partial   Still, I hate the idea of carrying the debt
partial   Thanks Anna, that's exactly what
```

- ITN: **yes** (`$610,000`, `6.2%`, `$320,000`, `5.5%`).
- Finals: **none.** All output is a volatile ~3 s window; committing any of it
  would violate "immutable finals" because the next update rewrites it.
- Cost: init 17-34 s (NPU graph compile), **~6.4 GB across two legs.**

## 3. Nemotron 0.6b (Foundry Local streaming ASR, CPU)

Emits token-by-token partials, then **one single FINAL for the entire leg** - no
sentence segmentation at all, so there are no chat bubbles, and no ITN.

Advisor leg, the one and only final:

```
FINAL  Hi Mark, good to see you. What's on your mind? I hear you. Let's check the
       numbers. You have six hundred ten thousand dollars in stocks averaging six
       point two percent return and a three hundred twenty thousand dollar mortgage
       at five point five percent interest. Fixed until January when it resets
       understandable if you paid it off, you'd eliminate that risk. But remember
       your investments are earning more than the loan is costing you and mortgage
       interest is often tax deductible, which lowers the real cost that's okay.
       Peace of mind is priceless. We'll model both options, but the right choice
       is the one that lets you feel secure
```

Customer leg, the one and only final:

```
FINAL  I've been thinking I should sell my stock and pay off the mortgage. I just
       want to sleep better at night. That reset is what scares me still. I hate
       the idea of carrying the debt thanks, Anna. That's exactly what I needed
```

- ITN: **no** (spelled out, same as Live Captions).
- Finals: one blob per leg - useless for per-utterance chat bubbles feeding an LLM.
- Cost: ~1750 MB / 2 legs.

## 4. Parakeet TDT 0.6b (offline attention, strict-live rolling window, CPU)

The only CPU engine that produces real ITN *and* per-sentence finals, but it must
re-encode a rolling context window to keep the ITN, which makes finals **duplicate**
and pushes it **below real time**.

Advisor leg (finals; note the duplicates - each re-transcription is a fresh,
differently-punctuated hypothesis an immutable harness cannot reconcile):

```
[00:03]  Hi Mark, good to see ye.
[00:04]  Hi Mark, good to see ye.                          <- duplicate
[00:30]  You have $610,000 in stocks averaging 6.2% return and a $320,000
         mortgage at 5.5% interest, fixed until January when it resets.
[00:35]  If you paid it off, you'd eliminate that risk.
[00:36]  If you paid it off, you'd eliminate that risk.    <- duplicate
[00:47]  But remember, your investments are earning more than the loan is costing you.
[00:47]  And mortgage interest is often tax deductible, which lowers the real cost.
[00:51]  Peace of mind is priceless.
[00:52]  Peace of mind is priceless.                       <- duplicate
```

Customer leg similar, with large latencies (up to 19 s) as the window re-encodes.

- ITN: **yes** (`$610,000`, `6.2%`) - as long as the rolling window is big enough.
  A small VAD window collapses it back to "six hundred ten thousand dollars".
- Finals: emitted, but **duplicated/unreconcilable** under immutable rules.
- Cost: advisor 1.1x RT (OK), customer **0.7x RT (falls behind real time)**,
  **peak 1778 MB for one leg.** Two legs concurrently is worse.

---

## Bottom line for MSFT

- **ITN is a display nicety here, not a correctness requirement.** The transcript
  feeds a reasoning LLM, which reads "six hundred ten thousand dollars at five
  point five percent" just as correctly as "$610,000 at 5.5%" - it only costs a few
  tokens. So Live Captions' lack of ITN is not a real knock, and where nicer
  bubbles are wanted, ITN can be applied on the display side after the fact.
- **Live Captions is the right *live* engine.** Cheap (507 MB), real-time, causal,
  streams clean per-sentence finals whose lexical numbers are fine for an LLM. The
  one genuine defect is the sentence committer dropping / duplicating finals on
  terminator flicker (it lost the whole "$610,000 mortgage" sentence). That is the
  fix worth doing - not the engine choice.
- **Parakeet is a batch/offline transcriber, so give it the *idle-time refiner*
  role.** In the live path (causal + immutable finals) it fights itself: small
  window loses its ITN, rolling window goes non-real-time (0.7x RT, 1.8 GB) and
  duplicates finals. But given a finished window it is best-in-class (~25x RT, real
  ITN). Run it on a background thread during per-leg silence (i.e. while the other
  speaker talks) to re-render the last N bubbles - both prettifying them and
  recovering any sentence the live committer dropped. Two tiers, each doing the job
  it is good at. See `PARAKEET-LIVE-STT-EVALUATION.md` section 7 for the full
  architecture.
- **The other two are out.** WinAI (Whisper Turbo) has the best raw text and ITN
  but never commits an immutable final (rolling partial only) and needs the NPU +
  ~6.4 GB; Nemotron emits one blob per leg with no sentence breaks and no ITN.

---

## Strategic takeaway: Live Captions is the competitor worth funding

Apple SpeechAnalyzer is the reference bar (immutable finals + native ITN +
real-time + light + exact attribution), but it is macOS-only. Among **Windows**
engines, **Live Captions is the only one already in that tier** - and the gap to
Apple is small and self-inflicted, not architectural:

- Live Captions already delivers the structurally hard part: streaming, causal,
  immutable per-sentence finals, real-time, on CPU, ~500 MB / 2 legs. WinAI,
  Nemotron, and Parakeet each fail one of those *by construction*.
- Its only deficits vs Apple are **fixable in Microsoft's own stack**: (1) ITN -
  the ITN model already ships in the Live Captions package; we lose it only
  because we suppress the ARM64-crashing native finalizer; (2) the dropped $610k
  sentence - a bug in *our* SentenceCommitter, not the recognizer.

**Recommendation to MSFT:** invest in the Live Captions finalizer + ITN path
(both already in-box) rather than integrating/optimizing third-party models to
chase the same bar. It is the shortest path to Apple-parity on-device STT, at
~0.5 GB CPU instead of ~6.4 GB on the NPU - and it is Microsoft's to own.
Caveats: "parity" means the tier, not identical segmentation (Apple splits one
long sentence into three); and it is contingent on fixing the two known defects -
today Live Captions is "closest, with a fixable gap," not "already equal."
