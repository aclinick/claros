using Claros;
using Claros.SpeakSubtitles;

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
//   dotnet run -r win-arm64 --project samples\SpeakSubtitles\Claros.SpeakSubtitles.csproj -- <file> [--out track.wav] [--lang fr-FR] [--voice Ava] [--dry-run]

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

// Speak whole sentences, not cue fragments: grouping consecutive cues keeps the
// synthesizer's intonation continuous so the talkover doesn't sound clipped.
var groups = CueSentenceGrouper.GroupIntoSentences(cues);
Console.WriteLine($"Grouped into {groups.Count} sentence utterances.");

using var platform = new SpeechPlatform();
var voices = await platform.ListVoicesAsync();
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
    Console.WriteLine("\nDry run — sentence utterances that would be narrated:");
    foreach (var g in groups)
    {
        Console.WriteLine($"  [{g.Index}] {g.Start:hh\\:mm\\:ss\\.fff}  {Preview(g.Text)}");
    }
    return 0;
}

var license = Environment.GetEnvironmentVariable("NATURAL_VOICE_LICENSE"); // null => resolved from the package

EmbeddedVoiceSpeaker speaker;
try
{
    speaker = platform.CreateSpeaker(voice, license);
}
catch (NaturalVoiceException ex)
{
    Console.Error.WriteLine($"Could not load the Embedded Speech runtime: {ex.Message}");
    return 8;
}

// Render the whole subtitle timeline to one voiceover track: TimedNarrator
// synthesizes each sentence and mixes it in at the cue's start time. The cues are
// already grouped into sentences above, so skip the narrator's own grouping.
WaveformResult track;
using (speaker)
{
    var narrator = new TimedNarrator(speaker);
    track = await narrator.RenderAsync(groups, new TimedNarrationOptions { GroupIntoSentences = false });
}

if (track.SampleRate == 0)
{
    Console.Error.WriteLine("Nothing was synthesized.");
    return 7;
}

var outPath = Path.GetFullPath(opt.OutPath ?? Path.ChangeExtension(opt.InputPath, ".wav"));
WaveFile.WriteMono16(outPath, track.Samples, track.SampleRate);
Console.WriteLine($"\nWrote {track.Samples.Length / (double)track.SampleRate:F1}s voiceover at {track.SampleRate} Hz: {outPath}");
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
