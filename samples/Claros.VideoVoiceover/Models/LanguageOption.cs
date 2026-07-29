using System.Globalization;
using Claros;

namespace Claros_VideoVoiceover.Models;

/// <summary>
/// A voiceover language the user can pick: a subtitle file discovered next to the
/// video, paired with an installed Natural voice whose locale matches it.
/// </summary>
public sealed class LanguageOption
{
    public LanguageOption(string lang, string subtitlePath, VoiceInfo voice)
    {
        Lang = lang;
        SubtitlePath = subtitlePath;
        Voice = voice;
        Label = BuildLabel(voice);
    }

    /// <summary>Inferred subtitle language code, e.g. "fr" (the base .srt is "en").</summary>
    public string Lang { get; }

    /// <summary>Full path to the subtitle file this language reads from.</summary>
    public string SubtitlePath { get; }

    /// <summary>The installed Natural voice that narrates this language.</summary>
    public VoiceInfo Voice { get; }

    /// <summary>Friendly ComboBox label, e.g. "French (France) · Microsoft Remy".</summary>
    public string Label { get; }

    private static string BuildLabel(VoiceInfo voice)
    {
        var locale = LocaleName(voice.Locale);
        var shortName = VoiceShortName(voice.DisplayName);
        return $"{locale} · {shortName}";
    }

    private static string LocaleName(string locale)
    {
        if (string.IsNullOrWhiteSpace(locale)) return "Unknown";
        try
        {
            return CultureInfo.GetCultureInfo(locale).DisplayName;
        }
        catch (CultureNotFoundException)
        {
            return locale;
        }
    }

    // "Microsoft Remy (Natural HD) - French (France)" -> "Microsoft Remy".
    private static string VoiceShortName(string displayName)
    {
        var cut = displayName.IndexOf(" (", StringComparison.Ordinal);
        return cut > 0 ? displayName[..cut].Trim() : displayName.Trim();
    }
}
