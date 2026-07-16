namespace WindowsNaturalVoices.Internal;

/// <summary>
/// Loader for the phoneme table shipped inside a Natural Voice package.
/// Each line has the shape <c>&lt;lang-locale&gt;_&lt;phoneme&gt;:id</c>,
/// for example <c>en-us_eh1:646</c>. A handful of control tokens use bare
/// names: <c>&lt;pad&gt;:0</c>, <c>&lt;bos&gt;:4149</c>, <c>&lt;/s&gt;:1</c>.
/// </summary>
public sealed class PhonemeTable
{
    private readonly Dictionary<string, int> _byKey;

    public PhonemeTable(Dictionary<string, int> byKey)
    {
        _byKey = byKey;
        Pad = LookupOrDefault("<pad>", 0);
        Bos = LookupOrDefault("<bos>", 0);
        Eos = LookupOrDefault("</s>", 1);
    }

    public int Pad { get; }
    public int Bos { get; }
    public int Eos { get; }
    public int Count => _byKey.Count;

    /// <summary>
    /// Try to resolve a phoneme by its full key, e.g. <c>en-us_eh1</c> or
    /// a bare control token like <c>&lt;bos&gt;</c>.
    /// </summary>
    public bool TryGet(string key, out int id) => _byKey.TryGetValue(key, out id);

    /// <summary>
    /// Resolve an ARPABET phoneme for a given locale. For <c>en-US</c> and
    /// input <c>EH1</c>, the actual table key is <c>en-us_eh1</c>.
    /// </summary>
    public bool TryGetArpabet(string locale, string arpabet, out int id) =>
        _byKey.TryGetValue(locale.ToLowerInvariant() + "_" + arpabet.ToLowerInvariant(), out id);

    public static PhonemeTable Load(string path)
    {
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim('\uFEFF', ' ', '\t', '\r', '\n');
            if (line.Length == 0) continue;
            var colon = line.LastIndexOf(':');
            if (colon <= 0 || colon == line.Length - 1) continue;
            var key = line[..colon];
            if (!int.TryParse(line[(colon + 1)..], out var id)) continue;
            map[key] = id;
        }
        return new PhonemeTable(map);
    }

    private int LookupOrDefault(string key, int fallback) =>
        _byKey.TryGetValue(key, out var v) ? v : fallback;
}
