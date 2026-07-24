// BatchSynthesis — synthesize many phrases with one warm speaker.
//
// Model load dominates first-call latency, so the recommended pattern is to
// load a NaturalVoiceSpeaker once and reuse it for every phrase. This sample
// loads one speaker, then writes each phrase to its own WAV file, and shows
// how SynthesisOptions can be tuned per call.
using System.Diagnostics;
using Windows.Speech;

using var platform = new SpeechPlatform();
var voices = await platform.ListVoicesAsync();
if (voices.Count == 0)
{
    Console.WriteLine("No Windows Natural Voice packages installed.");
    Console.WriteLine("Install one from Settings > Time and Language > Speech > Manage voices.");
    return 1;
}

// The samples phonemize with the English "Microsoft Zira Desktop" SAPI voice,
// so prefer an en-US Natural Voice; fall back to the first voice otherwise.
var voice = voices.FirstOrDefault(v => v.Locale.StartsWith("en", StringComparison.OrdinalIgnoreCase))
            ?? voices[0];
if (!voice.Locale.StartsWith("en", StringComparison.OrdinalIgnoreCase))
{
    Console.WriteLine($"Warning: no English voice installed; using {voice.Locale}. " +
                      "The English SAPI frontend may mispronounce or drop phonemes.");
}
Console.WriteLine($"Using voice: {voice.DisplayName} ({voice.Locale})\n");

var phrases = new[]
{
    "The quick brown fox jumps over the lazy dog.",
    "Text to speech runs entirely on this device.",
    "No cloud, no network, no per character billing.",
};

var outputDir = Path.Combine(Environment.CurrentDirectory, "batch-output");
Directory.CreateDirectory(outputDir);

// Cap runaway generation a little tighter than the default for short phrases.
var options = new SynthesisOptions { MaxDecoderSteps = 600 };

// Load once, reuse for every phrase — the sessions stay warm for the lifetime
// of the speaker.
var loadTimer = Stopwatch.StartNew();
using var speaker = NaturalVoiceSpeaker.Load(voice);
loadTimer.Stop();
Console.WriteLine($"Loaded speaker in {loadTimer.ElapsedMilliseconds} ms " +
                  $"(SAPI: {speaker.Phonemizer.VoiceName})\n");

for (var i = 0; i < phrases.Length; i++)
{
    var timer = Stopwatch.StartNew();
    var waveform = await speaker.SpeakAsync(phrases[i], options);
    timer.Stop();

    var path = Path.Combine(outputDir, $"phrase{i + 1}.wav");
    WaveFile.WriteMono16(path, waveform.Samples, waveform.SampleRate);

    var seconds = waveform.Samples.Length / (double)waveform.SampleRate;
    Console.WriteLine($"[{i + 1}/{phrases.Length}] {seconds:F2}s audio in " +
                      $"{timer.ElapsedMilliseconds} ms -> {path}");
}

Console.WriteLine($"\nWrote {phrases.Length} files to {outputDir}");
return 0;
