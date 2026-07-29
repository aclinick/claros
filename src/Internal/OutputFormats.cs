using Microsoft.CognitiveServices.Speech;

namespace Claros.Internal;

/// <summary>
/// Maps a sample rate to a Speech SDK output format.
/// </summary>
/// <remarks>
/// Every format here is a <c>Riff*</c> variant, i.e. a WAV container rather than
/// bare samples. That is deliberate and load-bearing: the synthesizers read
/// results with <see cref="WaveFile.ReadMono16"/>, which parses a RIFF header, so
/// a raw-PCM format would be misread as though its first bytes were a header.
/// The format is always set explicitly rather than left to the SDK default, so
/// this never depends on a default that could differ by version or by engine.
/// </remarks>
internal static class OutputFormats
{
    public static SpeechSynthesisOutputFormat Resolve(int sampleRate) => sampleRate switch
    {
        24_000 => SpeechSynthesisOutputFormat.Riff24Khz16BitMonoPcm,
        16_000 => SpeechSynthesisOutputFormat.Riff16Khz16BitMonoPcm,
        48_000 => SpeechSynthesisOutputFormat.Riff48Khz16BitMonoPcm,
        _ => throw new ArgumentOutOfRangeException(
            nameof(sampleRate), sampleRate,
            "Supported output rates are 16000, 24000, and 48000 Hz."),
    };
}
