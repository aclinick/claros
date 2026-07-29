using Claros;

// End-to-end, barge-in-capable conversation loop — entirely on-device.
//
//   dotnet run -r win-arm64 -- <path-to-16kHz-mono-wav> [out.wav] [locale]
//
// A recorded utterance stands in for the microphone (paced to real time via
// WavFileAudioSource, so the voice-activity detector endpoints it exactly as a
// live mic would). The Live Captions recognizer transcribes the turn, an echo
// "assistant" turns it into a reply, and a natural voice speaks that reply into
// a WAV "speaker" you can play back. Swap the echo handler for an LLM to get a
// real assistant; swap the WAV source/sink for AudioGraphMicrophoneSource /
// AudioGraphSpeakerSink to run against live devices.

var micWav = args.Length > 0 ? args[0] : null;
var outWav = args.Length > 1 ? args[1] : "conversation-out.wav";
var locale = args.Length > 2 ? args[2] : "en-US";

if (micWav is null)
{
    Console.Error.WriteLine("usage: ConversationLoop <path-to-16kHz-mono-wav> [out.wav] [locale]");
    return 2;
}

using var platform = new SpeechPlatform();

var voice = await platform.FindVoiceAsync(locale);
if (voice is null)
{
    Console.Error.WriteLine($"No natural voice installed for '{locale}'.");
    return 1;
}

var model = platform.FindRecognitionModel(locale);
if (model is null)
{
    Console.Error.WriteLine($"No recognition model installed for '{locale}'.");
    return 1;
}

Console.WriteLine($"Voice:      {voice.DisplayName} ({voice.Locale})");
Console.WriteLine($"Recognizer: {model.ModelName} ({model.Locale})\n");

// The "microphone": replay the recorded WAV paced to real time.
var microphone = new WavFileAudioSource(micWav, TimeSpan.FromMilliseconds(100), realtime: true);

// The "assistant": echo whatever the user said back to them.
ConversationTurnHandler echo = (utterance, _) =>
{
    Console.WriteLine($"  user> {utterance}");
    var reply = $"You said: {utterance}";
    Console.WriteLine($"  bot > {reply}");
    return Task.FromResult<SpeechSynthesisRequest?>(reply);
};

// The synthesizer knows its output format up front, so the sink can be sized
// without synthesizing a throwaway phrase first. That matters beyond tidiness:
// against a hosted voice, a probe request would be billed.
using var synthesizer = platform.CreateSynthesizer(voice);
var speaker = new BufferedAudioSink(synthesizer.OutputFormat);

using var transcriber = platform.CreateTranscriber(model);
using var recognizer = transcriber.StartRecognizer();
using var detector = new EnergyVoiceActivityDetector(microphone.Format);

var conversation = new SpeechConversation(
    microphone, recognizer, detector, synthesizer, speaker, echo);
conversation.TurnRecognized += text => Console.WriteLine($"  [turn] {text}");
conversation.BargedIn += () => Console.WriteLine("  [barge-in] assistant cut off");

Console.WriteLine("Listening (replaying the recording as a live mic) ...\n");

// RunAsync returns naturally when the WAV "mic" is exhausted and the last turn
// has been spoken.
await conversation.RunAsync();

var spoken = speaker.ToWaveform();
WaveFile.WriteMono16(outWav, spoken.Samples, spoken.SampleRate);
Console.WriteLine($"\nAssistant audio ({spoken.Samples.Length / (double)spoken.SampleRate:F1}s) written to {outWav}");
Console.Out.Flush();

// The embedded recognition engine can fault as its native worker threads tear
// down at process exit; everything is captured above, so exit promptly.
Environment.Exit(0);
return 0;
