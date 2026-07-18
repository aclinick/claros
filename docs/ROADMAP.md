# Roadmap

ttslib works end-to-end today: it enumerates installed Windows Natural Voices,
runs their acoustic model and vocoder, and produces offline audio. Nothing
structural is missing for basic TTS. The items below are quality, coverage, and
packaging improvements — and, above all, the case for a first-party API.

## Now (works today)

- Dynamic voice enumeration via `AppExtensionCatalog` with `VoicesChanged`.
- Voice metadata from `Tokens.xml` (name, locale, gender, age, vendor, version).
- Acoustic model load + autoregressive decode → codec tokens.
- Vocoder load via streaming-op rewrite → mono PCM at 26 kHz.
- Offline G2P through the shipped SAPI frontend for supported locales.
- One-call `NaturalVoiceSpeaker` facade and a runnable Demo.
- Unit-tested pure-logic core (extraction, op-rewrite, phoneme table, IPA map,
  Tokens.xml, WAV, vocoder helpers).

## Next (quality and ergonomics)

- **Prosody / stress.** SAPI's `Emphasis` field stays at zero for Zira, so
  stressed content words currently get the same weight as function words.
  Recover stress from the frontend or a lexicon so emphasis lands correctly.
- **Sample-rate handling.** Make the 26 kHz→24 kHz re-pitch an explicit,
  documented option (e.g. proper resampling) instead of a header rewrap.
- **Locale coverage.** Verify the IPA→ARPABET map beyond en-US and add mappings
  for additional SAPI locales; document the direct-phoneme-id path for locales
  SAPI cannot handle.
- **Error surfaces.** Clear, typed exceptions when a voice package is missing
  expected files or when no voices are installed.

## Later (bigger bets)

- **Real-time streaming.** The streaming-op rewrite discards per-chunk state, so
  synthesis is one-shot per phrase. Reimplement the `Streaming*` operator family
  with its state buffer, or integrate a permissive streaming vocoder
  (e.g. HiFi-GAN), to enable low-latency streaming output.
- **Non-SAPI G2P option.** Offer a pluggable G2P (piper, espeak-ng, misaki) for
  environments without the SAPI frontend or for full control over phonemization.
- **Platform coverage.** Explore whether the same models can be driven outside
  the packaged-app WinRT constraints, and on Windows on ARM.
- **NuGet packaging.** Ship `0.1.0` once the API stabilizes (currently consumed
  via `ProjectReference`/submodule).

## The north star — a first-party API

The primary goal of this project is to **show Microsoft how they should ship
this**. Everything here is done with public NuGet packages and no reverse
engineering of protected content; the header-skipping and op-rewriting shims
exist only because there is no supported entry point. A first-party API would:

- project the `com.microsoft.voice.model.1` catalog with change notifications,
- expose voice metadata as a supported type,
- provide an official load + inference entry point (no header skipping, no op
  rewriting), and
- surface the `MSTTSLoc_OneCore.dll` text frontend directly rather than through
  SAPI's `PhonemeReached` event.

If Windows ships that surface, most of this library becomes unnecessary — which
is exactly the point.

## Definition of done (per change)

Each committable change is a single unit that must: build clean, ship or update
**full unit-test coverage** for the pure-logic code it touches with the suite
passing, and pass a **code review by a different model** than the one that
authored it before it is committed.
