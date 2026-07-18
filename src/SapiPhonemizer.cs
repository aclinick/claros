using System.Runtime.Versioning;
using System.Speech.Synthesis;
using WindowsNaturalVoices.Internal;

namespace WindowsNaturalVoices;

/// <summary>
/// Runs the Windows SAPI text preprocessor over a string and returns a phoneme
/// id sequence ready for <see cref="NaturalVoiceEngine.SynthesizeAsync"/>.
///
/// Windows Natural Voices and Azure Speech share the same
/// <c>MSTTSLoc_OneCore.dll</c> frontend. SAPI's <c>PhonemeReached</c> event
/// exposes that frontend's IPA output for free and entirely offline, so no
/// separate grapheme-to-phoneme engine is required for supported locales.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SapiPhonemizer : IDisposable
{
    private readonly SpeechSynthesizer _synth;
    private bool _disposed;

    /// <summary>The SAPI voice used to drive the preprocessor.</summary>
    public string VoiceName { get; }

    private SapiPhonemizer(SpeechSynthesizer synth, string voiceName)
    {
        _synth = synth;
        VoiceName = voiceName;
        _synth.SetOutputToNull();
    }

    /// <summary>
    /// Create a phonemizer bound to the given SAPI voice. When
    /// <paramref name="voiceName"/> is null the platform default voice is used.
    /// Common choices on English installs are "Microsoft Zira Desktop" and
    /// "Microsoft David Desktop".
    /// </summary>
    public static SapiPhonemizer Create(string? voiceName = null)
    {
        var synth = new SpeechSynthesizer();
        if (!string.IsNullOrEmpty(voiceName))
        {
            synth.SelectVoice(voiceName);
        }
        return new SapiPhonemizer(synth, synth.Voice.Name);
    }

    /// <summary>
    /// Convert <paramref name="text"/> to a phoneme id sequence for the given
    /// voice's <paramref name="phonemes"/> table. The result starts with
    /// <see cref="PhonemeTable.Bos"/> and ends with <see cref="PhonemeTable.Eos"/>.
    /// The <paramref name="locale"/> string selects the prefix used to look up
    /// ARPABET keys (for example <c>en-US</c> resolves as <c>en-us_iy1</c>).
    /// Symbols the map does not recognize (including SAPI's inter-word silence
    /// marker) are dropped; the acoustic model handles word boundaries from the
    /// phone context alone.
    /// </summary>
    public IReadOnlyList<int> Phonemize(string text, PhonemeTable phonemes, string locale = "en-US")
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(phonemes);

        var ipa = new List<(string Ipa, double DurationMs)>();
        void Handler(object? _, PhonemeReachedEventArgs e) =>
            ipa.Add((e.Phoneme ?? string.Empty, e.Duration.TotalMilliseconds));

        _synth.PhonemeReached += Handler;
        try
        {
            _synth.Speak(text);
        }
        finally
        {
            _synth.PhonemeReached -= Handler;
        }

        var ids = new List<int> { phonemes.Bos };
        var atWordStart = true;
        foreach (var (symbol, _) in ipa)
        {
            if (!IpaPhonemeMap.IpaToArpa.TryGetValue(symbol, out _))
            {
                // Unknown symbol (silence, punctuation, or unmapped IPA).
                // Treat as a soft word boundary so the next vowel gets stress.
                atWordStart = true;
                continue;
            }

            var arpa = IpaPhonemeMap.Convert(symbol, atWordStart)!;
            if (!phonemes.TryGetArpabet(locale, arpa, out var id))
            {
                // Fall back to the unstressed key.
                var bare = arpa.TrimEnd('0', '1', '2');
                if (!phonemes.TryGetArpabet(locale, bare, out id))
                {
                    atWordStart = false;
                    continue;
                }
            }

            ids.Add(id);
            atWordStart = false;
        }
        ids.Add(phonemes.Eos);
        return ids;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _synth.Dispose();
    }
}
