# Roadmap

ttslib works end-to-end today: it enumerates installed Windows Natural Voices,
runs their acoustic model and vocoder, and produces offline audio. Nothing
structural is missing for basic TTS. The items below are quality, coverage, and
packaging improvements - and, above all, the case for a first-party API.

## Now (works today)

- Dynamic voice enumeration via `AppExtensionCatalog` with `VoicesChanged`.
- Voice metadata from `Tokens.xml` (name, locale, gender, age, vendor, version).
- Acoustic model load + autoregressive decode → codec tokens.
- Vocoder load via streaming-op rewrite → mono PCM at 26 kHz.
- Offline G2P through the shipped SAPI frontend for supported locales.
- **Flagship `EmbeddedVoiceSpeaker`** - drives the installed voice through
  Microsoft's own on-device Azure Embedded Speech runtime, fully offline, with
  the high-fidelity HD acoustic model forced on for every utterance (see the
  front-end section). This is Microsoft's exact frontend + engine, so cadence,
  punctuation, and pronunciation match the OS itself.
- One-call `NaturalVoiceSpeaker` facade and a runnable Demo.
- Unit-tested pure-logic core (extraction, op-rewrite, phoneme table, IPA map,
  Tokens.xml, WAV, vocoder helpers).

## Next (quality and ergonomics)

- **A pluggable text front end (`ITextFrontend`).** The single highest-leverage
  change. Today's `SapiPhonemizer` is hard-wired into the pipeline; extracting an
  `ITextFrontend` seam lets the real Microsoft frontend (see below) drop in as the
  flagship implementation, keeps SAPI as a fallback, and preserves a
  bring-your-own-phonemes path. Everything below hangs off this seam.
- **Fail loud, never drop silently.** The current path silently discards IPA it
  cannot map and table keys it cannot resolve, so valid text can become garbled
  or near-empty audio while synthesis still "succeeds." Return a structured
  phonemization result (ids, unknown symbols, unresolved keys, coverage %), make
  the default policy strict, and offer an explicit warn-and-continue mode.
- **Real stress instead of a heuristic.** `PhonemeReachedEventArgs.Emphasis`
  already carries the frontend's `SPVFEATURE_STRESSED` bit; use it instead of the
  `atWordStart` guess (which stresses only a word-initial vowel and never fires
  for consonant-initial words).
- **Locale-correct frontend selection.** `NaturalVoiceSpeaker` currently drives
  en-US Zira regardless of the target voice's locale, then relabels its phones -
  wrong pronunciation for non-English voices. Select a SAPI voice whose culture
  matches, and fail loudly when none exists.
- **Sample-rate handling.** Make the 26 kHz→24 kHz re-pitch an explicit,
  documented option (proper resampling) instead of a header rewrap.

## The front end - reuse Microsoft's, don't reinvent it

**Confirmed and shipped (2026-07):** the on-device Windows Natural voices are
hosted by the **Azure Embedded Speech runtime that ships in Windows**
(`Microsoft.CognitiveServices.Speech.extension.embedded.tts.dll` under
`SystemApps`), and the installed `MicrosoftWindows.Voice.*` package runs
**fully offline** via `EmbeddedSpeechConfig.FromPath`. `EmbeddedVoiceSpeaker`
does exactly this end-to-end, so Microsoft's exact text frontend (lexicon,
neural letter-to-sound, polyphony tagger, phone converter, prosody) and acoustic
engine produce the audio - no lossy IPA→ARPABET re-implementation required.

**The HD-gating trap (the headline finding).** Each HD voice package ships two
acoustic tiers and gates them from a legacy `1033.INI`
(`[Pipeline] HDVoiceThreshold`, default 10). Short utterances render through a
tiny low-fidelity **device vocoder** (~2.5 MB) that sounds like a caricature;
only long inputs (empirically ~17–26 words and up) cross the threshold and use
the ~127 MB HD model. So for the short phrases users hear most - UI prompts,
Narrator snippets, chat replies - the shipped default *never* engages the HD
model the user downloaded. There is **no public runtime override**: the config
property bag accepts `Pipeline.HDVoiceThreshold` and round-trips it, but the
engine only reads the value from the package INI at `FromPath` time and ignores
the property. `EmbeddedVoiceSpeaker` therefore forces HD by materializing a
writable **overlay** of the package (symlinks for the multi-hundred-megabyte
models, plus a patched INI with `HDVoiceThreshold=0`), falling back to a copy
when symlink privilege is unavailable. Two hack-free alternatives were validated
but sound worse or need native interop: **pad-and-trim** (pad past the threshold,
then trim the padding audio via word-boundary timestamps - zero disk, but the
padding's prosody bleeds in) and an **in-memory INI read hook**. That this
requires an overlay at all is itself the argument: a first-party API should
simply expose an HD/quality switch.

The remaining target end state for the *transparent, license-free* path
(raw ONNX + a text frontend), best → most pragmatic:

1. **Gold - capture the real frontend's exact phone stream.** Host the installed
   voice through Embedded Speech and capture the exact phone-id sequence it feeds
   the acoustic model: either the frontend metadata (`VoiceSetting.TtsPhonemeEvents`
   / the `"phones":[{"id","pron"}]` payload) or by hooking the integer tensor
   entering `hd_am_v5_encoder`, reversing it through `hd_phones.txt`. This is
   **bit-exact by construction**, needs no IPA round trip, no stress guessing, no
   coverage gaps, and generalizes to every locale. It relies on undocumented
   interfaces - acceptable here because this is an internal reference POC - and is
   the strongest possible argument for a first-party API. Needs a real installed
   voice + native interop, so it lands behind the `ITextFrontend` seam as a
   research spike.
2. **Clean fallback - native SAPI phone ids.** Drive `ISpVoice` with a custom
   `ISpTTSEngineSite` and read raw `SPEI_PHONEME` events (`SPPHONEID` + duration +
   `SPVFEATURE_STRESSED`) before `System.Speech` converts them to IPA. en-US phone
   ids 10–49 map almost 1:1 to our ARPABET keys. Fully documented SAPI COM.
3. **Reference data.** Replace the ad-hoc IPA map with Microsoft's own tables -
   the MIT-licensed `System.Speech` `AlphabetConverter`/UPS resources and Azure's
   published SAPI/IPA/UPS phonetic sets - as an authoritative, offline map.
4. **PLS lexicons.** Ship/point to W3C PLS lexicons (`AddLexicon`) to fix names,
   abbreviations, and known OOV words offline.

`Windows.Media.SpeechSynthesis` (WinRT) is **not** a candidate: it exposes only
word/sentence boundaries, no sub-word phoneme metadata.

To track progress, build a **front-end quality harness**: phoneme coverage per
installed voice, phoneme/stress error rate against a reference lexicon, and a
per-locale table-resolution matrix, gating a locale from the convenience facade
until it clears a threshold.

## Later (bigger bets)

- **Real-time streaming.** The streaming-op rewrite discards per-chunk state, so
  synthesis is one-shot per phrase. Reimplement the `Streaming*` operator family
  with its state buffer, or integrate a permissive streaming vocoder
  (e.g. HiFi-GAN), to enable low-latency streaming output.
- **Non-Microsoft G2P fallback.** For environments without any usable Windows
  frontend, offer a pluggable open G2P (piper, espeak-ng, misaki) behind the same
  `ITextFrontend` seam. Lower fidelity than Microsoft's own frontend, so this is a
  portability escape hatch, not the primary path.
- **Platform coverage.** Explore whether the same models can be driven outside
  the packaged-app WinRT constraints, and on Windows on ARM.
- **NuGet packaging.** Ship `0.1.0` once the API stabilizes (currently consumed
  via `ProjectReference`/submodule).

## The north star - a first-party API

The primary goal of this project is to **show Microsoft how they should ship
this**. The pipeline is built on public NuGet packages; the header-skipping and
op-rewriting shims exist only because there is no supported entry point, and the
best possible text front end (see above) would drive Microsoft's own on-device
Embedded Speech frontend directly. A first-party API would:

- project the `com.microsoft.voice.model.1` catalog with change notifications,
- expose voice metadata as a supported type,
- provide an official load + inference entry point (no header skipping, no op
  rewriting), and
- expose the on-device neural text frontend directly - the exact phone-id
  sequence and prosody it already computes - instead of forcing callers to scrape
  SAPI's `PhonemeReached` event or reverse-engineer the Embedded Speech runtime,
  and
- expose a supported **quality/HD switch** so callers can opt every utterance
  into the HD acoustic model, instead of the current length-gated `1033.INI`
  threshold that can only be changed by overlaying a patched package.

If Windows ships that surface, most of this library becomes unnecessary - which
is exactly the point.

## Definition of done (per change)

Each committable change is a single unit that must: build clean, ship or update
**full unit-test coverage** for the pure-logic code it touches with the suite
passing, and pass a **code review by a different model** than the one that
authored it before it is committed.
