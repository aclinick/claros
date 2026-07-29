using Claros;

using var platform = new SpeechPlatform();
var voices = await platform.ListVoicesAsync();

if (voices.Count == 0)
{
    Console.WriteLine("No Windows Natural Voice packages installed.");
    Console.WriteLine("Install one from Settings > Time and Language > Speech > Manage voices.");
    return 1;
}

Console.WriteLine($"Found {voices.Count} installed Natural Voice(s):\n");
for (var i = 0; i < voices.Count; i++)
{
    var v = voices[i];
    Console.WriteLine($"  [{i}] {v.DisplayName}");
    Console.WriteLine($"       locale={v.Locale}  gender={v.Gender}  age={v.Age}  version={v.Version}");
    Console.WriteLine($"       package={v.PackageFullName}");
    Console.WriteLine();
}

var pick = voices[0];
Console.WriteLine($"Loading: {pick.DisplayName}");
using var speaker = NaturalVoiceSpeaker.Load(pick);
Console.WriteLine($"SAPI preprocessor: {speaker.Phonemizer.VoiceName}");
Console.WriteLine($"Vocoder: rewrote {speaker.Vocoder.RewrittenNodes} streaming nodes to stock ONNX ops\n");

var text = args.Length > 0 ? string.Join(' ', args) : "The quick brown fox, jumps over the lazy dog.";
Console.WriteLine($"Text: {text}");
Console.WriteLine("Speaking...");

var waveform = await speaker.SynthesizeAsync(text);
Console.WriteLine($"Waveform: {waveform.Samples.Length} samples at {waveform.SampleRate} Hz ({waveform.Samples.Length / (double)waveform.SampleRate:F2}s)");

var outPath = Path.Combine(Environment.CurrentDirectory, "hello.wav");
WaveFile.WriteMono16(outPath, waveform.Samples, waveform.SampleRate);
Console.WriteLine($"Wrote native {waveform.SampleRate} Hz WAV: {outPath}");

// Relabel the same samples at 24000 Hz. Nothing is resampled, so playback
// slows and the pitch drops by roughly 8 percent, matching the Azure Ava
// reference timing more closely.
var reWrapped = waveform.WithSampleRate(24000);
var reWrappedPath = Path.Combine(Environment.CurrentDirectory, "hello_24000.wav");
WaveFile.WriteMono16(reWrappedPath, reWrapped.Samples, reWrapped.SampleRate);
Console.WriteLine($"Wrote 24000 Hz rewrap:            {reWrappedPath}");

return 0;
