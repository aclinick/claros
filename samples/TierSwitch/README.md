# TierSwitch

The same synthesis code, run against **an installed on-device voice** or **a
hosted voice**, switched only by which `ISpeechSynthesizer` you construct.

```powershell
# on-device: free, private, offline, works on the whole Windows 11 fleet
dotnet run -r win-arm64 --project samples\TierSwitch\Claros.TierSwitch.csproj -- "hello there"

# hosted: a brand voice or a locale you have not installed - billed
$env:SPEECH_KEY='<your Speech resource key>'
$env:SPEECH_REGION='eastus'
dotnet run -r win-arm64 --project samples\TierSwitch\Claros.TierSwitch.csproj -- "hello there" --cloud <voice-name>
```

## The point

Everything after the `if` is tier-agnostic — one `ISpeechSynthesizer` variable
drives both paths, and the timed narrator, the conversation loop, and the audio
sinks all accept either. Moving a voice to the cloud does not mean restructuring
the app around it.

## Two rules the library holds to

**On-device is the default, and there is no silent fallback.** Nothing constructs
a hosted synthesizer for you and no code path quietly reaches the network when a
local voice is missing or slow. The cloud tier happens because you supplied a key
and asked for it. That is deliberate: a speech library that quietly phones home
cannot honestly claim to be offline or free.

**Capabilities are negotiated, not assumed.** The sample prints
`SynthesizerCapabilities` for whichever tier it used, because the tiers genuinely
differ and callers should branch on the difference rather than discover it at
runtime:

| | on-device | hosted |
| --- | --- | --- |
| `Offline` | yes | no |
| `Metered` | no | yes |
| `WordBoundaries` | yes | yes |
| `StableSampleRate` | yes | yes |

Consumers act on these. `TimedNarrator.RenderAsync` mixes every clip onto one
timeline at sample-computed offsets, so it refuses an engine that cannot promise
`StableSampleRate` — **before** synthesizing anything, because on a metered
engine a mid-render failure has already been paid for.

## What the hosted tier costs you

- **Money.** Requests are billed. Cancelling mid-flight — a barge-in, say — does
  not reliably avoid the charge for work the service already did, so retry loops
  deserve more care than they do against a local engine.
- **Latency and availability.** First audio depends on the network rather than on
  a warm local model, and network jitter has no on-device equivalent.
- **Privacy.** The text leaves the machine. That is the whole difference.

## Choosing a voice

Hosted voices — Azure neural, HD, and MAI-Voice models alike — are selected by
name, exactly as in the `name` attribute of an SSML `<voice>` element. Pass that
name to `--cloud`.

MAI-Voice's **instant voice cloning is gated**: only authorized, licensed voices
can be synthesized, and access is application-only rather than self-serve. Do not
design a product around a custom brand voice until you have that approval — the
prebuilt voices are available without it.
