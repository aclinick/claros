namespace Windows.Speech.Internal;

/// <summary>
/// Maps IPA symbols (as emitted by <c>System.Speech.Synthesis.SpeechSynthesizer.PhonemeReached</c>)
/// to the ARPABET keys used by the Natural Voice phoneme table.
///
/// This is a hand-built, deliberately incomplete bridge used by
/// <see cref="SapiPhonemizer"/> as an approximation of the neural voices' real
/// text frontend. It is lossy: unmapped symbols are dropped and stress is only
/// approximate. The intended replacement is to reuse Microsoft's own on-device
/// frontend directly (see the front-end plan in <c>docs/ROADMAP.md</c>).
/// </summary>
internal static class IpaPhonemeMap
{
    /// <summary>Base ARPABET keys (no stress digit).</summary>
    public static readonly IReadOnlyDictionary<string, string> IpaToArpa = new Dictionary<string, string>
    {
        // Fricatives
        ["\u00f0"] = "dh",       // ð
        ["\u03b8"] = "th",       // θ
        ["\u0283"] = "sh",       // ʃ
        ["\u0292"] = "zh",       // ʒ
        ["f"] = "f", ["v"] = "v", ["s"] = "s", ["z"] = "z", ["h"] = "h",
        // Nasals
        ["m"] = "m", ["n"] = "n", ["\u014b"] = "ng",   // ŋ
        // Affricates (tie bar U+0361)
        ["t\u0361\u0283"] = "ch",   // t͡ʃ
        ["d\u0361\u0292"] = "jh",   // d͡ʒ
        // Stops
        ["p"] = "p", ["b"] = "b", ["t"] = "t", ["d"] = "d", ["k"] = "k",
        ["\u0261"] = "g",        // ɡ (IPA)
        ["g"] = "g",              // ASCII g
        // Approximants and liquids
        ["l"] = "l", ["w"] = "w", ["j"] = "y",
        ["r"] = "r",
        ["\u027b"] = "r",         // ɻ (rhotic used by Zira)
        // Vowels (short/long)
        ["\u026a"] = "ih",        // ɪ
        ["i"] = "iy",
        ["\u028a"] = "uh",        // ʊ
        ["u"] = "uw",
        ["\u025b"] = "eh",        // ɛ
        ["e"] = "eh",
        ["\u00e6"] = "ae",        // æ
        ["\u028c"] = "ah",        // ʌ
        ["\u0259"] = "ax",        // ə
        ["\u025a"] = "er",        // ɚ
        ["\u0251"] = "aa",        // ɑ
        ["\u0254"] = "ao",        // ɔ
        ["o"] = "ow",
        // Diphthongs (two vowels with combining tie bar U+0361)
        ["a\u0361\u028a"] = "aw", // a͡ʊ
        ["a\u0361\u026a"] = "ay", // a͡ɪ
        ["e\u0361i"] = "ey",       // e͡i
        ["o\u0361\u028a"] = "ow", // o͡ʊ
        ["\u0254\u0361\u026a"] = "oy", // ɔ͡ɪ
    };

    public static readonly HashSet<string> Vowels = new()
    {
        "ih","iy","uh","uw","eh","ae","ah","ax","aa","ao",
        "aw","ay","ey","ow","oy","er",
    };

    /// <summary>
    /// Convert one IPA symbol (as returned by SAPI) to an ARPABET key, appending
    /// a stress digit when <paramref name="stressed"/> is true and the phone
    /// is a vowel. Returns <c>null</c> when the symbol has no mapping (for
    /// example the <c>\u0004</c> silence marker between words).
    /// </summary>
    public static string? Convert(string ipa, bool stressed)
    {
        if (!IpaToArpa.TryGetValue(ipa, out var arpa)) return null;
        if (stressed && Vowels.Contains(arpa)) return arpa + "1";
        return arpa;
    }
}
