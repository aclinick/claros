// LowLevelPipeline — drive each stage of the pipeline by hand.
//
// NaturalVoiceSpeaker is a facade over three components. This sample wires them
// up directly so you can see (and instrument) each stage: SAPI grapheme-to-
// phoneme, the acoustic model that emits discrete codec tokens, and the vocoder
// that turns those tokens into audio. Use this pattern when you need access to
// the intermediate phoneme ids or codec tokens.
using WindowsNaturalVoices;

using var catalog = new VoiceCatalog();
var voices = await catalog.ListVoicesAsync();
if (voices.Count == 0)
{
    Console.WriteLine("No Windows Natural Voice packages installed.");
    Console.WriteLine("Install one from Settings > Time and Language > Speech > Manage voices.");
    return 1;
}

// The sample phonemizes with the English "Microsoft Zira Desktop" SAPI voice,
// so prefer an en-US Natural Voice; fall back to the first voice otherwise.
var voice = voices.FirstOrDefault(v => v.Locale.StartsWith("en", StringComparison.OrdinalIgnoreCase))
            ?? voices[0];
if (!voice.Locale.StartsWith("en", StringComparison.OrdinalIgnoreCase))
{
    Console.WriteLine($"Warning: no English voice installed; using {voice.Locale}. " +
                      "The English SAPI frontend may mispronounce or drop phonemes.");
}
var text = args.Length > 0 ? string.Join(' ', args) : "Hello from the low level pipeline.";
Console.WriteLine($"Voice: {voice.DisplayName} ({voice.Locale})");
Console.WriteLine($"Text:  {text}\n");

// 1. Acoustic model — also owns the voice's phoneme table.
using var engine = NaturalVoiceEngine.Load(voice);
Console.WriteLine($"Loaded acoustic model. Phoneme table has {engine.Phonemes.Count} entries.");

// 2. Grapheme-to-phoneme via the shipped Windows SAPI frontend.
using var phonemizer = SapiPhonemizer.Create("Microsoft Zira Desktop");
var phonemeIds = phonemizer.Phonemize(text, engine.Phonemes, voice.Locale);
Console.WriteLine($"SAPI ({phonemizer.VoiceName}) produced {phonemeIds.Count} phoneme ids " +
                  $"(BOS={engine.Phonemes.Bos}, EOS={engine.Phonemes.Eos}).");

// 3. Run the acoustic model to get discrete codec tokens.
var tokens = await engine.SynthesizeAsync(phonemeIds);
Console.WriteLine($"Decoder ran {tokens.Steps} steps " +
                  $"(stoppedByGate={tokens.StoppedByGate}); " +
                  $"c20hz={tokens.C20Hz.Length} tokens, c40hz={tokens.C40Hz.Length} tokens.");

// 4. Vocoder — rewrites the streaming custom ops, then renders audio.
using var vocoder = Vocoder.Load(voice);
Console.WriteLine($"Vocoder rewrote {vocoder.RewrittenNodes} streaming nodes to stock ONNX ops.");
var waveform = vocoder.Synthesize(tokens);

var seconds = waveform.Samples.Length / (double)waveform.SampleRate;
Console.WriteLine($"Rendered {waveform.Samples.Length} samples at {waveform.SampleRate} Hz ({seconds:F2}s).");

var outPath = Path.Combine(Environment.CurrentDirectory, "lowlevel.wav");
WaveFile.WriteMono16(outPath, waveform.Samples, waveform.SampleRate);
Console.WriteLine($"Wrote {outPath}");

return 0;
