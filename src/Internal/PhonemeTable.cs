namespace Claros.Internal;

/// <summary>
/// Loader for the phoneme table shipped inside a Natural Voice package.
/// Each line has the shape <c>&lt;lang-locale&gt;_&lt;phoneme&gt;:id</c>,
/// for example <c>en-us_eh1:646</c>. A handful of control tokens use bare
/// names: <c>&lt;pad&gt;:0</c>, <c>&lt;bos&gt;:4149</c>, <c>&lt;/s&gt;:1</c>.
/// </summary>
public sealed class PhonemeTable
{
    // A real voice package always ships these. Their absence is treated as a
    // corrupt package rather than defaulted, because the defaults collide:
    // a missing <bos> resolves to 0, which is also <pad>.
    private static readonly string[] RequiredControlTokens = ["<pad>", "<bos>", "</s>"];

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
    /// <remarks>
    /// Lines that do not parse are skipped, so a vendor file may carry comments
    /// or trailing junk. Two things are not tolerated, because both otherwise
    /// surface as silently wrong audio rather than an error: a key repeated with
    /// a conflicting id, and a package missing the control tokens. In particular,
    /// a missing <c>&lt;bos&gt;</c> would fall back to <c>0</c> — the same id as
    /// <c>&lt;pad&gt;</c> — so every utterance would be prefixed with padding
    /// instead of a begin-of-sequence marker.
    /// </remarks>
    /// <param name="path">Full path to the phoneme table file.</param>
    /// <returns>A populated <see cref="PhonemeTable"/>.</returns>
    /// <exception cref="VoicePackageFormatException">
    /// The file repeats a key with a different id, or omits a required control token.
    /// </exception>
    public static PhonemeTable Load(string path)
    {
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        // Every occurrence is compared against the first one, so a third,
        // differently-valued occurrence still reports the id and line the key
        // actually first had rather than whatever was seen most recently.
        var firstSeen = new Dictionary<string, (int Id, int Line)>(StringComparer.Ordinal);
        List<string>? conflicts = null;
        var lineNumber = 0;

        foreach (var raw in File.ReadLines(path))
        {
            lineNumber++;
            var line = raw.Trim('\uFEFF', ' ', '\t', '\r', '\n');
            if (line.Length == 0) continue;
            var colon = line.LastIndexOf(':');
            if (colon <= 0 || colon == line.Length - 1) continue;
            var key = line[..colon];
            if (!int.TryParse(line[(colon + 1)..], out var id)) continue;

            if (firstSeen.TryGetValue(key, out var first))
            {
                if (first.Id != id)
                {
                    conflicts ??= [];
                    conflicts.Add(
                        $"'{key}' is {first.Id} on line {first.Line} but {id} on line {lineNumber}");
                }

                // A repeat with the same id is harmless; keep the first mapping.
                continue;
            }

            firstSeen[key] = (id, lineNumber);
            map[key] = id;
        }

        if (conflicts is not null)
        {
            throw new VoicePackageFormatException(
                $"Phoneme table '{path}' assigns conflicting ids to the same phoneme: " +
                string.Join("; ", conflicts) + ".");
        }

        var missing = RequiredControlTokens.Where(t => !map.ContainsKey(t)).ToArray();
        if (missing.Length > 0)
        {
            throw new VoicePackageFormatException(
                $"Phoneme table '{path}' is missing the required control token(s) " +
                string.Join(", ", missing) +
                ". Without them the model would be fed padding in place of a real " +
                "sequence marker and would synthesize incorrect audio.");
        }

        return new PhonemeTable(map);
    }

    private int LookupOrDefault(string key, int fallback) =>
        _byKey.TryGetValue(key, out var v) ? v : fallback;
}
