using System.Text.RegularExpressions;

namespace Claros.SpeakSubtitles;

/// <summary>
/// Infers a target locale from a subtitle file name that follows the common
/// <c>name.&lt;lang&gt;.ext</c> convention, e.g. <c>movie.fr.srt</c> or
/// <c>movie.fr-FR.vtt</c>.
/// </summary>
internal static partial class LocaleInference
{
    public static string? FromFileName(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path); // drops .srt / .vtt
        var match = LangSuffixRegex().Match(name);
        return match.Success ? match.Groups[1].Value : null;
    }

    [GeneratedRegex(@"\.([A-Za-z]{2}(?:-[A-Za-z]{2})?)$")]
    private static partial Regex LangSuffixRegex();
}

/// <summary>Chooses which installed Natural voice narrates a subtitle file.</summary>
internal static class VoiceSelection
{
    /// <summary>
    /// Pick a voice by explicit name substring, else by locale, else the first
    /// installed voice. Returns <c>null</c> with an explanatory
    /// <paramref name="reason"/> when a requested name or locale has no match.
    /// </summary>
    public static VoiceInfo? Pick(
        IReadOnlyList<VoiceInfo> voices, string? nameSubstring, string? lang, out string reason)
    {
        if (nameSubstring is not null)
        {
            var byName = voices.FirstOrDefault(
                v => v.DisplayName.Contains(nameSubstring, StringComparison.OrdinalIgnoreCase));
            if (byName is not null) { reason = $"matched --voice '{nameSubstring}'"; return byName; }
            reason = $"No installed voice matches '{nameSubstring}'.";
            return null;
        }

        if (lang is not null)
        {
            var norm = lang.Replace('_', '-');
            var exact = voices.FirstOrDefault(v => v.Locale.Equals(norm, StringComparison.OrdinalIgnoreCase));
            if (exact is not null) { reason = $"locale {exact.Locale}"; return exact; }

            var langPart = norm.Split('-')[0];
            var byLang = voices.FirstOrDefault(
                v => LanguagePart(v.Locale).Equals(langPart, StringComparison.OrdinalIgnoreCase));
            if (byLang is not null) { reason = $"locale {byLang.Locale} (language {langPart})"; return byLang; }

            reason = $"No installed voice for locale '{lang}'.";
            return null;
        }

        reason = "default first installed voice";
        return voices[0];
    }

    private static string LanguagePart(string locale) => locale.Split('-')[0];
}
