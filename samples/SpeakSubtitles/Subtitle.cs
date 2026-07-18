using System.Globalization;
using System.Text.RegularExpressions;

namespace WindowsNaturalVoices.SpeakSubtitles;

/// <summary>A single timed subtitle cue.</summary>
internal sealed record Cue(int Index, TimeSpan Start, TimeSpan End, string Text);

/// <summary>
/// Minimal parser for SubRip (<c>.srt</c>) and WebVTT (<c>.vtt</c>) subtitle
/// files. Both formats are blank-line-separated blocks whose timing line carries
/// a <c>--&gt;</c> arrow; SRT uses a comma before the milliseconds and WebVTT a
/// dot, and WebVTT timing lines may carry trailing cue settings that we ignore.
/// </summary>
internal static partial class SubtitleParser
{
    public static IReadOnlyList<Cue> Parse(string content)
    {
        // Normalize newlines and split into blank-line-separated blocks.
        var normalized = content.Replace("\r\n", "\n").Replace('\r', '\n');
        var blocks = BlankLineRegex().Split(normalized);

        var cues = new List<Cue>();
        var index = 0;
        foreach (var block in blocks)
        {
            var lines = block.Split('\n');
            var timingLineIndex = Array.FindIndex(lines, static l => l.Contains("-->"));
            if (timingLineIndex < 0) continue; // header ("WEBVTT"), NOTE, or empty block

            var timing = TimingRegex().Match(lines[timingLineIndex]);
            if (!timing.Success) continue;

            var start = ToTimeSpan(timing.Groups["h1"].Value, timing.Groups["m1"].Value,
                timing.Groups["s1"].Value, timing.Groups["ms1"].Value);
            var end = ToTimeSpan(timing.Groups["h2"].Value, timing.Groups["m2"].Value,
                timing.Groups["s2"].Value, timing.Groups["ms2"].Value);

            var text = CleanText(string.Join(' ', lines[(timingLineIndex + 1)..]));
            if (text.Length == 0) continue;

            cues.Add(new Cue(++index, start, end, text));
        }

        cues.Sort(static (a, b) => a.Start.CompareTo(b.Start));
        return cues;
    }

    private static TimeSpan ToTimeSpan(string h, string m, string s, string ms)
    {
        var hours = h.Length == 0 ? 0 : int.Parse(h, CultureInfo.InvariantCulture);
        var minutes = int.Parse(m, CultureInfo.InvariantCulture);
        var seconds = int.Parse(s, CultureInfo.InvariantCulture);
        var millis = int.Parse(ms.PadRight(3, '0')[..3], CultureInfo.InvariantCulture);
        return new TimeSpan(0, hours, minutes, seconds, millis);
    }

    private static string CleanText(string raw)
    {
        var stripped = TagRegex().Replace(raw, string.Empty); // drop <i>, <c>, <00:00:01.000> etc.
        stripped = System.Net.WebUtility.HtmlDecode(stripped);
        return WhitespaceRegex().Replace(stripped, " ").Trim();
    }

    [GeneratedRegex(@"\n\s*\n")]
    private static partial Regex BlankLineRegex();

    [GeneratedRegex(
        @"(?:(?<h1>\d+):)?(?<m1>\d{1,2}):(?<s1>\d{2})[.,](?<ms1>\d{1,3})\s*-->\s*" +
        @"(?:(?<h2>\d+):)?(?<m2>\d{1,2}):(?<s2>\d{2})[.,](?<ms2>\d{1,3})")]
    private static partial Regex TimingRegex();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
