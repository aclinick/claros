using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Claros;

// SpeakWebPage: fetch a web page, extract its readable text, and read it aloud in
// real time with the flagship, fully-offline EmbeddedSpeechSynthesizer (forced HD).
// By default it streams narration to the speakers as it is synthesized and echoes
// each word as it is spoken; pass --out to write a WAV file instead.
//
// Usage:
//   dotnet run -r win-arm64 --project samples\SpeakWebPage\Claros.SpeakWebPage.csproj -- <url> [--voice Ava] [--max 1200] [--out page.wav]
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
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Claros/0.1 SpeakWebPage");
    http.Timeout = TimeSpan.FromSeconds(30);
    html = await http.GetStringAsync(parsed.Url);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Could not fetch the page: {ex.Message}");
    return 4;
}

var (title, body) = ContentExtractor.Extract(parsed.Url, html);
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

using var platform = new SpeechPlatform();
var voices = await platform.ListVoicesAsync();
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

EmbeddedSpeechSynthesizer synthesizer;
try
{
    synthesizer = platform.CreateSynthesizer(voice, license); // null license => resolved from the package
}
catch (NaturalVoiceException ex)
{
    Console.Error.WriteLine($"Could not load the Embedded Speech runtime: {ex.Message}");
    return 8;
}

using (synthesizer)
{
    if (parsed.OutPath is not null)
    {
        Console.WriteLine($"Narrating {text.Length} characters ...");
        var waveform = await synthesizer.SynthesizeAsync(text);
        var outPath = Path.GetFullPath(parsed.OutPath);
        WaveFile.WriteMono16(outPath, waveform.Samples, waveform.SampleRate);
        var seconds = waveform.Samples.Length / (double)waveform.SampleRate;
        Console.WriteLine($"Wrote {seconds:F1}s at {waveform.SampleRate} Hz: {outPath}");
    }
    else
    {
        Console.WriteLine($"Reading aloud ({text.Length} characters) - press Ctrl+C to stop.\n");
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        var column = 0;
        try
        {
            await synthesizer.SpeakToDefaultOutputAsync(text, word =>
            {
                if (column + word.Text.Length + 1 > 100) { Console.WriteLine(); column = 0; }
                Console.Write(word.Text);
                Console.Write(' ');
                column += word.Text.Length + 1;
            }, cts.Token);
            Console.WriteLine("\n\nDone.");
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("\n\nStopped.");
        }
    }
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
        Console.WriteLine("Usage: SpeakWebPage <url> [--voice <name>] [--max <chars>] [--out page.wav]");
        Console.WriteLine("  --voice  Substring of the Natural voice display name (default first installed).");
        Console.WriteLine("  --max    Max characters to narrate; 0 reads the whole page (default 1200).");
        Console.WriteLine("  --out    Write a WAV file instead of reading aloud live.");
        Console.WriteLine("By default the page is read aloud in real time. Ctrl+C stops playback.");
        Console.WriteLine("The on-device model license is read from the installed voice package (override with NATURAL_VOICE_LICENSE).");
    }
}

internal static class ContentExtractor
{
    // Reader-mode extraction: SmartReader is the .NET port of Mozilla's
    // Readability (the engine behind Edge/Firefox reader view), so it isolates
    // the article body from navigation, headers, and other page chrome. If the
    // page isn't article-shaped (or parsing fails), fall back to the
    // dependency-free heuristic in HtmlText.
    public static (string Title, string Body) Extract(string url, string html)
    {
        try
        {
            var reader = new SmartReader.Reader(url, html);
            var article = reader.GetArticle();
            if (article.IsReadable && !string.IsNullOrWhiteSpace(article.TextContent))
            {
                var title = article.Title?.Trim() ?? string.Empty;
                return (title, NormalizeLines(article.TextContent));
            }
        }
        catch
        {
            // Not readable as an article; fall through to the heuristic.
        }

        return HtmlText.Extract(html);
    }

    // Readability returns plain text with generous blank lines; trim each line
    // and drop empties so the narration flows without long dead-air gaps.
    private static string NormalizeLines(string text)
    {
        var lines = text
            .Replace("\r\n", "\n")
            .Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0);
        return string.Join("\n", lines);
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
