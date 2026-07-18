using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using WindowsNaturalVoices;

// SpeakWebPage: fetch a web page, extract its readable text, and narrate it to a
// WAV file with the flagship, fully-offline EmbeddedVoiceSpeaker (forced HD).
//
// Usage:
//   dotnet run -r win-arm64 --project samples\SpeakWebPage\WindowsNaturalVoices.SpeakWebPage.csproj -- <url> [--out page.wav] [--voice Ava] [--max 1200]
//
// The Embedded Speech runtime requires a Microsoft-issued license for the
// on-device models; by default it is read automatically from the installed voice
// package. Set NATURAL_VOICE_LICENSE to override it.

var parsed = Args.Parse(args);
if (parsed is null)
{
    Args.PrintUsage();
    return 2;
}

var license = Environment.GetEnvironmentVariable("NATURAL_VOICE_LICENSE");

Console.WriteLine($"Fetching {parsed.Url} ...");
string html;
try
{
    using var http = new HttpClient();
    http.DefaultRequestHeaders.UserAgent.ParseAdd(
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) WindowsNaturalVoices/0.1 SpeakWebPage");
    http.Timeout = TimeSpan.FromSeconds(30);
    html = await http.GetStringAsync(parsed.Url);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Could not fetch the page: {ex.Message}");
    return 4;
}

var (title, body) = HtmlText.Extract(html);
var text = body;
if (parsed.MaxChars > 0 && text.Length > parsed.MaxChars)
{
    text = Trim.ToSentenceBoundary(text, parsed.MaxChars);
    Console.WriteLine($"Trimmed to {text.Length} characters (use --max 0 to read the whole page).");
}

if (string.IsNullOrWhiteSpace(text))
{
    Console.Error.WriteLine("No readable text was found on the page.");
    return 5;
}

if (!string.IsNullOrWhiteSpace(title))
{
    Console.WriteLine($"Title: {title}");
    text = $"{title}. {text}";
}

using var catalog = new VoiceCatalog();
var voices = await catalog.ListVoicesAsync();
if (voices.Count == 0)
{
    Console.Error.WriteLine(
        "No Windows Natural Voice packages installed. Install one from " +
        "Settings > Time and Language > Speech > Manage voices.");
    return 6;
}

var voice = parsed.Voice is null
    ? voices[0]
    : voices.FirstOrDefault(v => v.DisplayName.Contains(parsed.Voice, StringComparison.OrdinalIgnoreCase));
if (voice is null)
{
    Console.Error.WriteLine($"No installed voice matches '{parsed.Voice}'. Installed:");
    foreach (var v in voices) Console.Error.WriteLine($"  {v.DisplayName}");
    return 7;
}

Console.WriteLine($"Voice: {voice.DisplayName} (forced HD)");
Console.WriteLine($"Narrating {text.Length} characters ...");

EmbeddedVoiceSpeaker speaker;
try
{
    speaker = EmbeddedVoiceSpeaker.Load(voice, license); // null license => resolved from the package
}
catch (NaturalVoiceException ex)
{
    Console.Error.WriteLine($"Could not load the Embedded Speech runtime: {ex.Message}");
    return 8;
}

using (speaker)
{
    var waveform = await speaker.SpeakAsync(text);
    var outPath = Path.GetFullPath(parsed.OutPath ?? "page.wav");
    WaveFile.WriteMono16(outPath, waveform.Samples, waveform.SampleRate);
    var seconds = waveform.Samples.Length / (double)waveform.SampleRate;
    Console.WriteLine($"Wrote {seconds:F1}s at {waveform.SampleRate} Hz: {outPath}");
}

return 0;

internal sealed record Options(string Url, string? OutPath, string? Voice, int MaxChars);

internal static class Args
{
    public static Options? Parse(string[] args)
    {
        string? url = null, outPath = null, voice = null;
        var maxChars = 1200;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--out" when i + 1 < args.Length: outPath = args[++i]; break;
                case "--voice" when i + 1 < args.Length: voice = args[++i]; break;
                case "--max" when i + 1 < args.Length && int.TryParse(args[++i], out var m): maxChars = m; break;
                case "-h" or "--help": return null;
                default:
                    if (url is null && !args[i].StartsWith('-')) url = args[i];
                    break;
            }
        }
        if (url is null) return null;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            Console.Error.WriteLine("The URL must be an absolute http or https address.");
            return null;
        }
        return new Options(url, outPath, voice, maxChars);
    }

    public static void PrintUsage()
    {
        Console.WriteLine("Usage: SpeakWebPage <url> [--out page.wav] [--voice <name>] [--max <chars>]");
        Console.WriteLine("  --out    Output WAV path (default page.wav).");
        Console.WriteLine("  --voice  Substring of the Natural voice display name (default first installed).");
        Console.WriteLine("  --max    Max characters to narrate; 0 reads the whole page (default 1200).");
        Console.WriteLine("The on-device model license is read from the installed voice package (override with NATURAL_VOICE_LICENSE).");
    }
}

internal static partial class HtmlText
{
    // Best-effort readable-text extraction. This is deliberately dependency-free
    // and not a full readability engine: it drops scripts, styles, and markup and
    // collapses whitespace. Good enough to narrate an article; not a parser.
    public static (string Title, string Body) Extract(string html)
    {
        var title = TitleRegex().Match(html) is { Success: true } t
            ? WebUtility.HtmlDecode(t.Groups[1].Value).Trim()
            : string.Empty;

        var cleaned = ScriptRegex().Replace(html, " ");
        cleaned = StyleRegex().Replace(cleaned, " ");
        cleaned = CommentRegex().Replace(cleaned, " ");
        cleaned = TitleRegex().Replace(cleaned, " "); // drop <title> so it isn't narrated twice
        cleaned = BlockBreakRegex().Replace(cleaned, "\n");
        cleaned = TagRegex().Replace(cleaned, " ");
        cleaned = WebUtility.HtmlDecode(cleaned);
        cleaned = WhitespaceRegex().Replace(cleaned, " ").Trim();
        return (title, cleaned);
    }

    [GeneratedRegex(@"<title[^>]*>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex TitleRegex();

    // Separate script/style patterns (no backreference) so NonBacktracking can be
    // used, keeping matching linear-time on untrusted, possibly malformed pages.
    [GeneratedRegex(@"<script[^>]*>.*?</script>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.NonBacktracking)]
    private static partial Regex ScriptRegex();

    [GeneratedRegex(@"<style[^>]*>.*?</style>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.NonBacktracking)]
    private static partial Regex StyleRegex();

    [GeneratedRegex("<!--.*?-->", RegexOptions.Singleline | RegexOptions.NonBacktracking)]
    private static partial Regex CommentRegex();

    [GeneratedRegex(@"</(p|div|section|article|h[1-6]|li|br|tr)\s*>|<br\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex BlockBreakRegex();

    [GeneratedRegex("<[^>]+>", RegexOptions.NonBacktracking)]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"\s+", RegexOptions.NonBacktracking)]
    private static partial Regex WhitespaceRegex();
}

internal static class Trim
{
    // Trim to at most maxChars, preferring to end on a sentence boundary so the
    // narration doesn't stop mid-word.
    public static string ToSentenceBoundary(string text, int maxChars)
    {
        if (text.Length <= maxChars) return text;
        var window = text[..maxChars];
        var lastStop = window.LastIndexOfAny(new[] { '.', '!', '?' });
        return lastStop > maxChars / 2 ? window[..(lastStop + 1)] : window;
    }
}
