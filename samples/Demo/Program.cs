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
Console.WriteLine($"Phoneme table: {engine.Phonemes.Count} entries, bos={engine.Phonemes.Bos}, eos={engine.Phonemes.Eos}\n");

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
Console.WriteLine($"First few 20 Hz tokens: [{string.Join(", ", result.C20Hz.Take(10))}]");
Console.WriteLine($"First few 40 Hz tokens: [{string.Join(", ", result.C40Hz.Take(10))}]");

Console.WriteLine("\nNote: turning these codec tokens into a waveform requires the vocoder,");
Console.WriteLine("which uses an undocumented StreamingConv custom op that Microsoft ships");
Console.WriteLine("in the OS but has not exposed to third parties.");

return 0;
