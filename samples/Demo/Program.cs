using WindowsNaturalVoices;

using var catalog = new VoiceCatalog();
var voices = await catalog.ListVoicesAsync();

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
Console.WriteLine($"Loading acoustic model for: {pick.DisplayName}");
using var engine = NaturalVoiceEngine.Load(pick);
using var vocoder = Vocoder.Load(pick);
Console.WriteLine($"Phoneme table: {engine.Phonemes.Count} entries, bos={engine.Phonemes.Bos}, eos={engine.Phonemes.Eos}");
Console.WriteLine($"Vocoder: rewrote {vocoder.RewrittenNodes} streaming nodes to stock ONNX ops\n");

// "The quick brown fox, jumps over the lazy dog." via the SAPI text preprocessor.
// Requires "Microsoft Zira Desktop" (default Windows en-US SAPI voice).
var text = args.Length > 0 ? string.Join(' ', args) : "The quick brown fox, jumps over the lazy dog.";
Console.WriteLine($"Text: {text}");

using var phonemizer = SapiPhonemizer.Create("Microsoft Zira Desktop");
Console.WriteLine($"SAPI preprocessor: {phonemizer.VoiceName}");

var ids = phonemizer.Phonemize(text, engine.Phonemes, locale: pick.Locale);
Console.WriteLine($"Phoneme IDs ({ids.Count}): [{string.Join(", ", ids)}]");
Console.WriteLine("Running synthesis...");

var result = await engine.SynthesizeAsync(ids);

Console.WriteLine($"\nSynthesized {result.Steps} decoder steps.");
Console.WriteLine($"Stopped by gate: {result.StoppedByGate}");
Console.WriteLine($"20 Hz codec tokens: {result.C20Hz.Length} ({result.C20Hz.Length / 2} pairs)");
Console.WriteLine($"40 Hz codec tokens: {result.C40Hz.Length} ({result.C40Hz.Length / 2} pairs)");

Console.WriteLine("\nRunning vocoder...");
var waveform = vocoder.Synthesize(result);
Console.WriteLine($"Waveform: {waveform.Samples.Length} samples at {waveform.SampleRate} Hz ({waveform.Samples.Length / (double)waveform.SampleRate:F2}s)");

var outPath = Path.Combine(Environment.CurrentDirectory, "hello.wav");
WaveFile.WriteMono16(outPath, waveform.Samples, waveform.SampleRate);
Console.WriteLine($"Wrote native {waveform.SampleRate} Hz WAV: {outPath}");

// Also emit a rewrapped 24000 Hz variant. Same samples, different header,
// plays back roughly 8 percent slower and lower pitched. Closer to Azure.
var reWrappedPath = Path.Combine(Environment.CurrentDirectory, "hello_24000.wav");
WaveFile.WriteMono16(reWrappedPath, waveform.Samples, 24000);
Console.WriteLine($"Wrote 24000 Hz rewrap:            {reWrappedPath}");

return 0;
