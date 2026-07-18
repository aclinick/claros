# Samples

Runnable console apps that exercise `WindowsNaturalVoices`. Each requires at
least one **Natural Voice** installed (Settings > Time & language > Speech >
Manage voices) and the stock **Microsoft Zira Desktop** SAPI voice.

| Sample | Shows | Run |
| --- | --- | --- |
| [Demo](Demo) | Quick start: enumerate voices, speak one phrase, write a WAV (plus the 24 kHz re-pitch trick). | `dotnet run --project samples\Demo\WindowsNaturalVoices.Demo.csproj -- "your text"` |
| [ListVoices](ListVoices) | Discovery only: enumerate installed voices, print metadata, react to `VoicesChanged`. | `dotnet run --project samples\ListVoices\WindowsNaturalVoices.ListVoices.csproj` |
| [BatchSynthesis](BatchSynthesis) | Load one `NaturalVoiceSpeaker` and synthesize many phrases to separate WAV files; tune `SynthesisOptions`. | `dotnet run --project samples\BatchSynthesis\WindowsNaturalVoices.BatchSynthesis.csproj` |
| [LowLevelPipeline](LowLevelPipeline) | Drive `SapiPhonemizer`, `NaturalVoiceEngine`, and `Vocoder` directly to inspect phoneme ids and codec tokens. | `dotnet run --project samples\LowLevelPipeline\WindowsNaturalVoices.LowLevelPipeline.csproj -- "your text"` |

All samples are part of `WindowsNaturalVoices.slnx`, so `dotnet build
WindowsNaturalVoices.slnx` builds them together. If no voice is installed, each
sample prints guidance and exits without synthesizing.
