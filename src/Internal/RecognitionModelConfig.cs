namespace WindowsNaturalVoices.Internal;

/// <summary>
/// The identifying fields of an on-device recognition model, parsed from the
/// <c>lp.config</c> file that ships in every <c>MicrosoftWindows.Speech.&lt;locale&gt;</c>
/// pack (the Live Captions recognition packs). Example contents:
/// <code>
/// name=Microsoft Speech Recognizer en-US FP Model V11
/// power-level=full
/// locale=en-US
/// version=11
/// </code>
/// </summary>
internal sealed record RecognitionModelConfig(
    string Name,
    string Locale,
    string? PowerLevel,
    string? Version)
{
    /// <summary>The <c>lp.config</c> filename inside a recognition pack.</summary>
    public const string FileName = "lp.config";

    /// <summary>
    /// Parse the <c>key=value</c> lines of an <c>lp.config</c> file. Returns
    /// <c>null</c> when the required <c>name</c> and <c>locale</c> keys are
    /// absent. Unknown keys, blank lines, and <c>#</c> comments are ignored.
    /// </summary>
    public static RecognitionModelConfig? Parse(string? content)
    {
        if (string.IsNullOrWhiteSpace(content)) return null;

        string? name = null, locale = null, powerLevel = null, version = null;
        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line[0] == '#') continue;

            var eq = line.IndexOf('=');
            if (eq <= 0) continue;

            var key = line[..eq].Trim();
            var value = line[(eq + 1)..].Trim();
            switch (key)
            {
                case "name": name = value; break;
                case "locale": locale = value; break;
                case "power-level": powerLevel = value; break;
                case "version": version = value; break;
            }
        }

        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(locale)) return null;
        return new RecognitionModelConfig(name, locale, powerLevel, version);
    }

    /// <summary>
    /// Read and parse the <c>lp.config</c> in <paramref name="packageDirectory"/>,
    /// or <c>null</c> when it is missing or malformed.
    /// </summary>
    public static RecognitionModelConfig? FromPackage(string packageDirectory)
    {
        var path = Path.Combine(packageDirectory, FileName);
        if (!File.Exists(path)) return null;
        try { return Parse(File.ReadAllText(path)); }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }
}
