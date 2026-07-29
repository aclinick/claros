using Claros;

// Explicit tier switch: the SAME code synthesizes through an installed on-device
// voice or a hosted one, and the ONLY thing that changes is which
// ISpeechSynthesizer you construct. Nothing here falls back automatically —
// on-device is the default and the cloud path happens only because you asked for
// it by supplying a key.
//
//   dotnet run -r win-arm64 -- "text to speak"
//   dotnet run -r win-arm64 -- "text to speak" --cloud <voice-name>
//
// The hosted path reads SPEECH_KEY and SPEECH_REGION from the environment so a
// key never lands in source or in your shell history via an argument.

var text = "The quick brown fox jumps over the lazy dog.";
string? cloudVoice = null;

for (var i = 0; i < args.Length; i++)
{
    if (args[i] is "--cloud" && i + 1 < args.Length) { cloudVoice = args[++i]; continue; }
    if (!args[i].StartsWith("--", StringComparison.Ordinal)) text = args[i];
}

using var platform = new SpeechPlatform();

// One variable, one interface — everything below this point is tier-agnostic.
ISpeechSynthesizer synthesizer;

if (cloudVoice is null)
{
    var voice = (await platform.ListVoicesAsync()).FirstOrDefault();
    if (voice is null)
    {
        Console.Error.WriteLine(
            "No Natural Voice installed. Settings > Time & language > Speech > Manage voices.");
        return 1;
    }

    Console.WriteLine($"Tier:  on-device ({voice.DisplayName}, {voice.Locale})");
    synthesizer = platform.CreateSynthesizer(voice);
}
else
{
    var key = Environment.GetEnvironmentVariable("SPEECH_KEY");
    var region = Environment.GetEnvironmentVariable("SPEECH_REGION");
    if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(region))
    {
        Console.Error.WriteLine(
            "Set SPEECH_KEY and SPEECH_REGION to use the hosted tier, e.g.\n" +
            "  $env:SPEECH_KEY='...'; $env:SPEECH_REGION='eastus'");
        return 1;
    }

    Console.WriteLine($"Tier:  hosted ({cloudVoice}, {region}) - this request is billed");
    synthesizer = CloudSpeechSynthesizer.Connect(new CloudVoiceOptions
    {
        SubscriptionKey = key,
        Region = region,
        VoiceName = cloudVoice,
    });
}

using (synthesizer)
{
    // Capabilities are how you find out what a tier gives you, rather than
    // assuming. They differ in ways worth knowing before you build on them.
    var caps = synthesizer.Capabilities;
    Console.WriteLine(
        $"Caps:  offline={caps.Offline}, metered={caps.Metered}, " +
        $"wordBoundaries={caps.WordBoundaries}");
    Console.WriteLine(
        $"Audio: {synthesizer.OutputFormat.SampleRate} Hz mono 16-bit (known without synthesizing)");

    if (caps.Metered)
    {
        Console.WriteLine(
            "Note:  cancelling a hosted request mid-flight does not reliably avoid the\n" +
            "       charge for work the service has already done.");
    }

    var waveform = await synthesizer.SynthesizeAsync(text);
    var outPath = Path.Combine(Environment.CurrentDirectory, "tier.wav");
    WaveFile.WriteMono16(outPath, waveform.Samples, waveform.SampleRate);

    Console.WriteLine(
        $"Wrote: {outPath} ({waveform.Samples.Length / (double)waveform.SampleRate:F2}s " +
        $"at {waveform.SampleRate} Hz)");
}

return 0;
