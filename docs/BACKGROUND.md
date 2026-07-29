# Background

## The goal

**Claros is a reference implementation that shows Microsoft how a public API for
the on-device Windows Natural Voices should look.** The models already ship in
Windows and sound like the cloud, but there is no supported way to use them.
This library demonstrates, in a few hundred lines of ordinary .NET, that the
missing API is small, safe, and straightforward to provide. The hope is that
Windows ships something like it as a first-class, supported surface.

## The models are already on the machine

Modern Windows ships Microsoft's neural ("Natural") text-to-speech voices as
on-device models. When a user installs a voice from **Settings > Time & language
> Speech > Manage voices**, Windows lays down an MSIX package containing the
acoustic model, the vocoder, a phoneme table, and voice metadata. These are the
same voice families Microsoft offers in the cloud through Azure AI Speech (Ava,
Aria, Jenny, and friends), running fully locally on the CPU.

## They sound like the cloud

The on-device models share lineage with Azure Speech: the same voice
families (Ava, Aria, Jenny, and friends), and on-device they are hosted by the
**Azure Embedded Speech runtime**, the offline-capable sibling of the same
Cognitive Services Speech technology. The local output is perceptually very
close to the cloud neural voices. (That the on-device host is Embedded Speech is
a community-observed finding, not officially documented.) You get near-cloud
quality with **no network round trip, no per-character billing, and no data
leaving the device**.

## But there is no API to reach them

Each voice declares itself to the OS through the AppExtension contract
`com.microsoft.voice.model.1`: the standard mechanism apps use to publish
extensible content. Despite that, Microsoft ships **no public API** to enumerate
these voices, load their models, or run inference against them. The only
supported path is `Windows.Media.SpeechSynthesis.SpeechSynthesizer`, which is
restricted to packaged apps with the right WinRT surface and does not expose the
underlying models.

There is also no cryptographic protection on the model files. Every `*.bin` in a
voice package is a fixed plaintext EULA header, an 8-byte tag, and then a raw
ONNX `ModelProto`. The encoder, decoder, and vocoder all load under stock
`Microsoft.ML.OnnxRuntime` once the header is skipped and the vocoder's custom
streaming operators are rewritten to their standard ONNX equivalents.

## What this library is

**Claros fills that hole in the Windows API.** It is a
small .NET library that:

- discovers the Natural Voices installed on the machine,
- runs their on-device acoustic model and vocoder with stock ONNX Runtime, and
- reuses the shipped Windows SAPI text frontend for grapheme-to-phoneme,

so any .NET app can do offline, near-cloud-quality TTS with the voices the user
already has. This is the clean public API that Windows is missing today.

## What Microsoft should ship

The whole surface a supported API needs is already implied by this repo:

- **Enumeration**: a projection over the `com.microsoft.voice.model.1`
  AppExtension catalog with change notifications (see `VoiceCatalog`).
- **Voice metadata**: display name, locale, gender, age, vendor, version
  (see `VoiceInfo` / `Tokens.xml`).
- **Loading + inference**: an official entry point that returns the acoustic
  model and vocoder without needing to skip a plaintext header or rewrite custom
  ops (see `NaturalVoiceEngine` / `Vocoder`).
- **A reusable text frontend**: the on-device neural G2P frontend (hosted by
  the Azure Embedded Speech runtime that already ships in Windows) surfaced
  directly, returning the exact phone-id sequence and prosody it computes,
  instead of scraped through SAPI's `PhonemeReached` event (see `SapiPhonemizer`
  and the front-end plan in `docs/ROADMAP.md`).

Everything above is done here with public NuGet packages and no reverse
engineering of protected content. A first-party version would simply not require
the header-skipping and op-rewriting shims.

## Legal note

The library never bundles or redistributes Microsoft's voice bytes; it reads
them at runtime from the package installed on the machine that runs it. The
plaintext notice inside each `*.bin` states the model and software may not be
used or distributed except under a written agreement with Microsoft (reference
2774316). This project is a technical demonstration of the missing API. Consult
your own counsel before shipping a product on top of it. Library code is MIT;
voice binaries remain under their own EULA.
