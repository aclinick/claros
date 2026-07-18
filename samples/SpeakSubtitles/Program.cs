using WindowsNaturalVoices;
using WindowsNaturalVoices.SpeakSubtitles;

// SpeakSubtitles: turn a subtitle file (.srt / .vtt) into a voiceover track whose
// audio is aligned to the subtitle timings. Because the narration follows the cue
// timestamps, you can re-time or reword the talkover for a video just by editing
// the subtitles and re-running this tool, then dropping the WAV back onto the
// video as an audio track.
//
// The voice is chosen by locale, so a French subtitle narrates in an installed
// French Natural voice. Locale comes from --lang, or is inferred from the file
// name (movie.fr-FR.srt / movie.fr.srt), or defaults to the first installed voice.
//
// Usage:
//   dotnet run -r win-arm64 --project samples\SpeakSubtitles\WindowsNaturalVoices.SpeakSubtitles.csproj -- <file> [--out track.wav] [--lang fr-FR] [--voice Ava] [--dry-run]

var opt = Options.Parse(args);
if (opt is null)
{
    Options.PrintUsage();
    return 2;
}

if (!File.Exists(opt.InputPath))
{
    Console.Error.WriteLine($"Subtitle file not found: {opt.InputPath}");
    return 3;
}

var cues = SubtitleParser.Parse(File.ReadAllText(opt.InputPath));
if (cues.Count == 0)
{
    Console.Error.WriteLine("No timed cues were found. Provide a .srt or .vtt file.");
    return 4;
}

Console.WriteLine($"Parsed {cues.Count} cues spanning {cues.Max(c => c.End):hh\\:mm\\:ss}.");

using var catalog = new VoiceCatalog();
var voices = await catalog.ListVoicesAsync();
if (voices.Count == 0)
{
    Console.Error.WriteLine(
        "No Windows Natural Voice packages installed. Install one from " +
        "Settings > Time and Language > Speech > Manage voices.");
    return 5;
}

var lang = opt.Lang ?? LocaleInference.FromFileName(opt.InputPath);
var voice = VoiceSelection.Pick(voices, opt.Voice, lang, out var reason);
if (voice is null)
{
    Console.Error.WriteLine(reason);
    Console.Error.WriteLine("Installed voices:");
    foreach (var v in voices) Console.Error.WriteLine($"  {v.DisplayName}  [{v.Locale}]");
    return 6;
}

Console.WriteLine($"Voice: {voice.DisplayName}  [{voice.Locale}]  ({reason})");

if (opt.DryRun)
{
    Console.WriteLine("\nDry run — cues that would be narrated:");
    foreach (var cue in cues)
    {
        Console.WriteLine($"  {cue.Start:hh\\:mm\\:ss\\.fff}  {Preview(cue.Text)}");
    }
    return 0;
}

var license = Environment.GetEnvironmentVariable("NATURAL_VOICE_LICENSE"); // null => resolved from the package

EmbeddedVoiceSpeaker speaker;
try
{
    speaker = EmbeddedVoiceSpeaker.Load(voice, license);
}
catch (NaturalVoiceException ex)
{
    Console.Error.WriteLine($"Could not load the Embedded Speech runtime: {ex.Message}");
    return 8;
}

int sampleRate;
var clips = new List<(int StartSample, float[] Samples)>(cues.Count);
using (speaker)
{
    // Synthesize each cue and remember where it starts on the timeline.
    var rate = 0;
    for (var i = 0; i < cues.Count; i++)
    {
        var cue = cues[i];
        var wave = await speaker.SpeakAsync(cue.Text);
        rate = wave.SampleRate;
        var startSample = (int)(cue.Start.TotalSeconds * rate);
        clips.Add((startSample, wave.Samples));

        var clipEnd = cue.Start + TimeSpan.FromSeconds(wave.Samples.Length / (double)rate);
        var overrun = i + 1 < cues.Count && clipEnd > cues[i + 1].Start;
        var flag = overrun ? "  (overruns next cue)" : string.Empty;
        Console.WriteLine($"  [{cue.Index}] {cue.Start:hh\\:mm\\:ss\\.fff} -> {clipEnd:hh\\:mm\\:ss\\.fff}{flag}  {Preview(cue.Text)}");
    }
    sampleRate = rate;
}

// Lay every clip onto one silent timeline at its start offset, mixing overlaps.
// Cues are sorted by start, so the latest END may belong to an earlier cue.
var lastCueEnd = (int)(cues.Max(c => c.End).TotalSeconds * sampleRate);
var totalSamples = lastCueEnd;
foreach (var (start, samples) in clips)
{
    totalSamples = Math.Max(totalSamples, start + samples.Length);
}

var timeline = new float[totalSamples];
foreach (var (start, samples) in clips)
{
    for (var i = 0; i < samples.Length; i++)
    {
        timeline[start + i] += samples[i]; // additive mix; WriteMono16 clamps
    }
}

var outPath = Path.GetFullPath(opt.OutPath ?? Path.ChangeExtension(opt.InputPath, ".wav"));
WaveFile.WriteMono16(outPath, timeline, sampleRate);
Console.WriteLine($"\nWrote {totalSamples / (double)sampleRate:F1}s voiceover at {sampleRate} Hz: {outPath}");
return 0;

static string Preview(string text) => text.Length <= 60 ? text : text[..57] + "...";

internal sealed record Options(string InputPath, string? OutPath, string? Voice, string? Lang, bool DryRun)
{
    public static Options? Parse(string[] args)
    {
        string? input = null, outPath = null, voice = null, lang = null;
        var dryRun = false;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--out" when i + 1 < args.Length: outPath = args[++i]; break;
                case "--voice" when i + 1 < args.Length: voice = args[++i]; break;
                case "--lang" when i + 1 < args.Length: lang = args[++i]; break;
                case "--dry-run": dryRun = true; break;
                case "-h" or "--help": return null;
                default:
                    if (input is null && !args[i].StartsWith('-')) input = args[i];
                    break;
            }
        }
        return input is null ? null : new Options(input, outPath, voice, lang, dryRun);
    }

    public static void PrintUsage()
    {
        Console.WriteLine("Usage: SpeakSubtitles <file.srt|file.vtt> [--out track.wav] [--lang fr-FR] [--voice <name>] [--dry-run]");
        Console.WriteLine("  --out      Output WAV path (default: input name with .wav).");
        Console.WriteLine("  --lang     Target locale (e.g. fr-FR or fr); overrides file-name inference.");
        Console.WriteLine("  --voice    Substring of the Natural voice display name; overrides --lang.");
        Console.WriteLine("  --dry-run  Parse and pick a voice without synthesizing (no license needed).");
        Console.WriteLine("The on-device model license is read from the installed voice package (override with NATURAL_VOICE_LICENSE).");
    }
}
