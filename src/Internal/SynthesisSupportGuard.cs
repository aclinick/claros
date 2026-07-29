namespace Claros.Internal;

/// <summary>
/// Shared refusals for engines that implement <see cref="ISpeechSynthesizer"/>
/// without covering the whole contract.
/// </summary>
/// <remarks>
/// The interface describes a superset: plain text, prosody-shaped text, raw
/// SSML, and per-word boundaries. Not every engine can do all of it — the
/// reconstructed <see cref="NaturalVoiceSynthesizer"/> pipeline drives SAPI's
/// preprocessor with plain text and its acoustic model emits no word
/// alignment. The alternative to refusing is to accept the request and quietly
/// drop the part that cannot be honored, which is worse: markup would vanish
/// into audio that does not match what was asked for, and a caption highlighter
/// would wait forever for a callback that can never arrive. These throw instead,
/// and name the engine that does support the feature.
/// </remarks>
internal static class SynthesisSupportGuard
{
    /// <summary>
    /// Throws when <paramref name="request"/> needs SSML rendering — raw markup
    /// or non-empty prosody — on an engine that only speaks plain text. The two
    /// are reported separately so the message names the flag the caller should
    /// have checked.
    /// </summary>
    public static void RequirePlainText(SpeechSynthesisRequest request, string voiceName)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.IsSsml)
        {
            throw new NotSupportedException(
                $"'{voiceName}' is driven through this pipeline's SAPI text preprocessor, " +
                "which takes plain text, so a raw SSML document cannot be rendered " +
                $"({CapabilityName(nameof(SynthesizerCapabilities.RawSsml))} is false). Use " +
                $"{nameof(EmbeddedSpeechSynthesizer)} for SSML.");
        }

        if (request.Prosody is { IsEmpty: false })
        {
            throw new NotSupportedException(
                $"'{voiceName}' is driven through this pipeline's SAPI text preprocessor, " +
                "which takes plain text, so prosody cannot be honored " +
                $"({CapabilityName(nameof(SynthesizerCapabilities.Prosody))} is false). " +
                $"Speaking the text unshaped would not be what was asked for; use " +
                $"{nameof(EmbeddedSpeechSynthesizer)} for prosody.");
        }
    }

    /// <summary>
    /// Throws when a caller supplies an <c>onWord</c> callback to an engine that
    /// reports no word boundaries.
    /// </summary>
    public static void RequireNoWordCallback(
        Action<SpokenWord>? onWord, string voiceName, string paramName)
    {
        if (onWord is null) return;

        throw new NotSupportedException(
            $"'{voiceName}' produces no word boundaries through this pipeline " +
            $"({CapabilityName(nameof(SynthesizerCapabilities.WordBoundaries))} is false), " +
            $"so '{paramName}' would never be raised. Use " +
            $"{nameof(EmbeddedSpeechSynthesizer)} for word-boundary events.");
    }

    private static string CapabilityName(string flag) =>
        $"{nameof(ISpeechSynthesizer.Capabilities)}.{flag}";
}
