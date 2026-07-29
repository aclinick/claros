# dothisonthemac — run Apple SpeechAnalyzer under the identical strict-live STT harness

**Who this is for:** a GitHub Copilot session running on an Apple Silicon Mac
(macOS 26+, Xcode 26+). **Goal:** produce the Apple `SpeechAnalyzer` row of a
cross-engine live-call STT comparison, using the *same* harness, *same* audio, and
*same* rules the Windows engines were run under, then hand the results back so they
can be pasted into `ALL-ENGINES-CHAT-OUTPUT.md`.

We already have Windows results for Live Captions, WinAI (Whisper Turbo NPU),
Nemotron 0.6b, and Parakeet TDT 0.6b. Only Apple is missing because it is
macOS-only. That is what you are filling in.

---

## The contract (must match exactly — do not "improve" it)

This simulates a **live** two-party call. The engine may only react to audio that
has already arrived, and any emitted final is treated as immutable (it would be
sent to a reasoning LLM the instant it appears and can never be retracted). The
harness already enforces this: 100 ms mono chunks, fed at 1x real-time pace, one
recognizer per call leg, printing `[+<ms> FINAL/partial] <text>`. Do not batch the
file, do not add lookahead, do not post-process.

- Left channel = **advisor (Anna)**. Right channel = **customer (Mark)**.
- Run each leg **separately** (one recognizer per speaker), exactly like Windows.

---

## Steps

### 1. Get the harness (it already exists)

```bash
cd ~/  # or wherever you keep repos
# Clone if you don't have it, else just fetch:
git clone https://github.com/<org>/Contoso-Finance.git 2>/dev/null || true
cd Contoso-Finance
git fetch origin
git checkout feature/macos-speech-worker
git pull

cd MacBench/ITranscriberHarness
swift build -c release
```

The built binary is at `.build/release/itranscriber-test` (or run via
`swift run -c release itranscriber-test ...`). Usage:
`itranscriber-test <wav-path> apple speechanalyzer`. It requires **16 kHz mono**
WAV input and will download the on-device Speech model on first run.

### 2. Produce the two call legs from the shared recording

The call is `scripts/mortgage-call-stereo.mp4` in this repo (stereo: L=Anna,
R=Mark). Split it into two 16 kHz mono WAVs — the exact same two legs the Windows
run used:

```bash
cd ~/Contoso-Finance   # repo root
mkdir -p /tmp/stt
# Advisor (Anna) = LEFT channel
ffmpeg -y -i scripts/mortgage-call-stereo.mp4 \
  -filter_complex "channelsplit=channel_layout=stereo:channels=FL[l]" \
  -map "[l]" -ar 16000 -ac 1 -c:a pcm_s16le /tmp/stt/call_left.wav
# Customer (Mark) = RIGHT channel
ffmpeg -y -i scripts/mortgage-call-stereo.mp4 \
  -filter_complex "channelsplit=channel_layout=stereo:channels=FR[r]" \
  -map "[r]" -ar 16000 -ac 1 -c:a pcm_s16le /tmp/stt/call_right.wav
```

(If ffmpeg is missing: `brew install ffmpeg`.)

### 3. Run Apple SpeechAnalyzer on each leg, capture output

```bash
BIN=./MacBench/ITranscriberHarness/.build/release/itranscriber-test

echo "===== ADVISOR (Anna) / call_left.wav ====="
"$BIN" /tmp/stt/call_left.wav  apple speechanalyzer 2>/tmp/stt/apple_left.err  | tee /tmp/stt/apple_left.out

echo "===== CUSTOMER (Mark) / call_right.wav ====="
"$BIN" /tmp/stt/call_right.wav apple speechanalyzer 2>/tmp/stt/apple_right.err | tee /tmp/stt/apple_right.out
```

Each run is ~60 s (real-time paced). The `.out` files hold the transcript lines
(`FINAL` / `partial`); the `.err` files hold init/timing diagnostics
(`initialized in <ms>ms`, `feed complete`, `done. total`).

### 4. What to report back

Paste the following into your reply so it can be dropped into the comparison:

1. **Advisor (Anna) leg** — all `FINAL` lines in order (the chat bubbles). If Apple
   only ever emits `partial` and never `FINAL`, say so and paste the last few
   partials instead.
2. **Customer (Mark) leg** — same.
3. **Two yes/no facts** that decide the comparison:
   - **Immutable finals?** Does it emit stable `FINAL` sentences, or only rolling
     `partial`s that keep getting rewritten?
   - **ITN?** For the advisor's key sentence, does it render **`$610,000`, `6.2%`,
     `$320,000`, `5.5%`** (true ITN), or spell them out
     (`six hundred ten thousand dollars`, `six point two percent`)?
4. **Init latency** (`initialized in <ms>ms` from the `.err`) and, if easy, peak
   memory (run `/usr/bin/time -l "$BIN" ... ` and report "maximum resident set
   size"). Note: Apple's model runs in a shared system daemon, so process RSS
   undercounts real footprint — mention that caveat.

### Ground-truth reference (advisor, ~00:16)

> "You have $610,000 in stocks averaging 6.2% return and a $320,000 mortgage at
> 5.5% interest, fixed until January when it resets."

Watch specifically whether that whole sentence survives as a clean bubble (the
Windows Live Captions path **dropped** it) and whether the numbers are ITN'd.

---

## For reference: how the other engines behaved on the same audio

So you know what a useful answer looks like (do NOT copy these — measure Apple):

| Engine | Immutable finals? | ITN (`$610,000`)? | Notes |
| --- | --- | --- | --- |
| Live Captions (Win, CPU) | yes, per sentence | no (spelled out) | dropped the $610k sentence; ~507 MB / 2 legs |
| WinAI Whisper Turbo (NPU) | no — rolling partials | yes | never commits a final; ~6.4 GB / 2 legs |
| Nemotron 0.6b (CPU) | one blob per leg | no | no sentence breaks |
| Parakeet TDT 0.6b (offline) | yes but duplicates | yes | 0.7x RT, 1.8 GB — too slow live |

The open question for Apple: does it give BOTH stable finals AND ITN in real time
on the ANE — i.e. is it the clean live win none of the Windows engines were?

---

### Paste-back template (fill this in and send it back)

```
APPLE SpeechAnalyzer (macOS 26, ANE) — strict-live harness, 100ms chunks, 1x paced

Advisor (Anna) FINAL bubbles:
  <lines>
Customer (Mark) FINAL bubbles:
  <lines>

Immutable finals? : yes / no (+ one sentence why)
ITN ($610,000/6.2%)? : yes / no  (paste the actual "$610,000..." or spelled-out line)
$610k sentence survived as a clean bubble? : yes / no
Init latency : <ms>
Peak RSS (caveated) : <MB>  (shared daemon, undercounts)
```
