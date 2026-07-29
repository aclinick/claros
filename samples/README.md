# Samples

Runnable console apps that exercise `Claros`. Each requires at
least one **Natural Voice** installed (Settings > Time & language > Speech >
Manage voices). The `NaturalVoiceSpeaker`-based samples (Demo, BatchSynthesis,
LowLevelPipeline) also need the stock **Microsoft Zira Desktop** SAPI voice; the
`EmbeddedVoiceSpeaker` samples (SpeakWebPage, SpeakSubtitles) do not. The
speech-to-text samples (TranscribeFile, TranscriptionBenchmark) instead need an
on-device **recognition model** installed (Settings > Time & language > Speech,
or add a language with Live Captions / Voice Typing support).

| Sample | Shows | Run |
| --- | --- | --- |
| [Demo](Demo) | Quick start: enumerate voices, speak one phrase, write a WAV (plus the 24 kHz re-pitch trick). | `dotnet run --project samples\Demo\Claros.Demo.csproj -- "your text"` |
| [ListVoices](ListVoices) | Discovery only: enumerate installed voices, print metadata, react to `VoicesChanged`. | `dotnet run --project samples\ListVoices\Claros.ListVoices.csproj` |
| [BatchSynthesis](BatchSynthesis) | Load one `NaturalVoiceSpeaker` and synthesize many phrases to separate WAV files; tune `SynthesisOptions`. | `dotnet run --project samples\BatchSynthesis\Claros.BatchSynthesis.csproj` |
| [SpeakWebPage](SpeakWebPage) | Flagship forced-HD `EmbeddedVoiceSpeaker`: fetch a web page, extract its text, and narrate it to a WAV. | `dotnet run -r win-arm64 --project samples\SpeakWebPage\Claros.SpeakWebPage.csproj -- <url>` |
| [SpeakSubtitles](SpeakSubtitles) | Turn a `.srt`/`.vtt` file into a timeline-aligned voiceover track; picks a voice by locale so a French subtitle narrates in French. | `dotnet run -r win-arm64 --project samples\SpeakSubtitles\Claros.SpeakSubtitles.csproj -- <file>` |
| [TranscribeFile](TranscribeFile) | Offline speech-to-text: transcribe a 16 kHz mono WAV with the on-device Live Captions recognizer via `EmbeddedTranscriber`. | `dotnet run -r win-arm64 --project samples\TranscribeFile\Claros.TranscribeFile.csproj -- <wav> [locale]` |
| [TranscriptionBenchmark](TranscriptionBenchmark) | Live two-party call transcription: split a stereo recording per caller, transcribe both channels in real time, and report memory/latency against Foundry Local and NPU engines. | `dotnet run -r win-arm64 --project samples\TranscriptionBenchmark\Claros.TranscriptionBenchmark.csproj -- <stereo.mp4>` |
| [ConversationLoop](ConversationLoop) | End-to-end barge-in conversation: a recorded WAV stands in for the mic, the Live Captions recognizer + energy VAD endpoint each turn, and a natural voice speaks an echo reply to a WAV. Swap the echo handler for an LLM. | `dotnet run -r win-arm64 --project samples\ConversationLoop\Claros.ConversationLoop.csproj -- <16kHz-mono.wav> [out.wav] [locale]` |
| [LowLevelPipeline](LowLevelPipeline) | Drive `SapiPhonemizer`, `NaturalVoiceEngine`, and `Vocoder` directly to inspect phoneme ids and codec tokens. | `dotnet run --project samples\LowLevelPipeline\Claros.LowLevelPipeline.csproj -- "your text"` |

All samples are part of `Claros.slnx`, so `dotnet build
Claros.slnx` builds them together. If no voice is installed, each
sample prints guidance and exits without synthesizing.

## Desktop app

| Sample | Shows | Run |
| --- | --- | --- |
| [VideoVoiceover](Claros.VideoVoiceover) | WinUI 3 app that narrates a video's subtitles live and on device, switching voice language mid-playback without ever cutting a sentence in half. Includes `demo-drive.ps1`, a hands-off demo maker that drives the whole arc over UI Automation so you can screen-record it untouched. | `dotnet run -c Release --project samples\Claros.VideoVoiceover\Claros.VideoVoiceover.csproj` |
