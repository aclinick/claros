namespace Claros.Internal;

/// <summary>
/// Loader for the phoneme table shipped inside a Natural Voice package.
/// Each line has the shape <c>&lt;lang-locale&gt;_&lt;phoneme&gt;:id</c>,
/// for example <c>en-us_eh1:646</c>. A handful of control tokens use bare
/// names: <c>&lt;pad&gt;:0</c>, <c>&lt;bos&gt;:4149</c>, <c>&lt;/s&gt;:1</c>.
/// </summary>
public sealed class PhonemeTable
{
    private readonly Dictionary<string, int> _byKey;

    /// <summary>
    /// Creates a table from an already-parsed key-to-id map. Resolves the
    /// <c>&lt;pad&gt;</c>, <c>&lt;bos&gt;</c>, and <c>&lt;/s&gt;</c> control
    /// tokens up front; use <see cref="Load"/> to read one from a package file.
    /// </summary>
    /// <param name="byKey">Phoneme key to model input id map.</param>
    public PhonemeTable(Dictionary<string, int> byKey)
    {
        _byKey = byKey;
        Pad = LookupOrDefault("<pad>", 0);
        Bos = LookupOrDefault("<bos>", 0);
        Eos = LookupOrDefault("</s>", 1);
    }

    /// <summary>Input id for the padding token (<c>&lt;pad&gt;</c>).</summary>
    public int Pad { get; }

    /// <summary>Input id for the beginning-of-sequence token (<c>&lt;bos&gt;</c>).</summary>
    public int Bos { get; }

    /// <summary>Input id for the end-of-sequence token (<c>&lt;/s&gt;</c>).</summary>
    public int Eos { get; }

    /// <summary>Number of entries in the table.</summary>
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

    /// <summary>
    /// Loads a phoneme table from a package's <c>hd_phones.txt</c>-style file,
    /// where each line has the shape <c>&lt;key&gt;:&lt;id&gt;</c>.
    /// </summary>
    /// <param name="path">Full path to the phoneme table file.</param>
    /// <returns>A populated <see cref="PhonemeTable"/>.</returns>
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
