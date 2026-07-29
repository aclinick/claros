using System.Text;

namespace Claros;

/// <summary>
/// Connection and voice settings for <see cref="CloudSpeechSynthesizer"/>. Supplying
/// these is the explicit opt-in that moves synthesis off this machine; nothing in
/// the library creates them on a caller's behalf.
/// </summary>
public sealed record CloudVoiceOptions
{
    /// <summary>Key for the Azure Speech resource that serves the voice.</summary>
    public required string SubscriptionKey { get; init; }

    /// <summary>Region of that Speech resource, for example <c>eastus</c>.</summary>
    public required string Region { get; init; }

    /// <summary>
    /// The hosted voice to speak with, as it appears in the <c>name</c> attribute
    /// of an SSML <c>&lt;voice&gt;</c> element. MAI-Voice models are selected the
    /// same way as any other Azure neural or HD voice.
    /// </summary>
    public required string VoiceName { get; init; }

    /// <summary>BCP-47 locale for the generated SSML, for example <c>en-US</c>.</summary>
    public string Locale { get; init; } = "en-US";

    /// <summary>
    /// Output sample rate in Hz: 16000, 24000, or 48000. Set explicitly rather
    /// than left to the SDK default, because the synthesizer parses the result as
    /// a WAV container and a raw-PCM default would be misread.
    /// </summary>
    public int SampleRate { get; init; } = 24_000;

    /// <summary>
    /// Throws when a required setting is missing, so a misconfigured connection
    /// fails at construction rather than on the first billed request.
    /// </summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(SubscriptionKey))
        {
            throw new ArgumentException(
                "A Speech resource key is required to reach a hosted voice.", nameof(SubscriptionKey));
        }
        if (string.IsNullOrWhiteSpace(Region))
        {
            throw new ArgumentException(
                "A Speech resource region is required, for example 'eastus'.", nameof(Region));
        }
        if (string.IsNullOrWhiteSpace(VoiceName))
        {
            throw new ArgumentException(
                "A hosted voice name is required, for example a MAI-Voice model name.", nameof(VoiceName));
        }
        if (string.IsNullOrWhiteSpace(Locale))
        {
            throw new ArgumentException(
                "A BCP-47 locale is required, for example 'en-US'.", nameof(Locale));
        }
        if (SampleRate is not (16_000 or 24_000 or 48_000))
        {
            throw new ArgumentOutOfRangeException(
                nameof(SampleRate), SampleRate,
                "Supported output rates are 16000, 24000, and 48000 Hz.");
        }
    }

    /// <summary>
    /// Formats this record without its credential. A record's generated
    /// <see cref="object.ToString"/> prints every property, which would put the
    /// Speech key into any log line, exception message, or debugger view that
    /// happened to render the options.
    /// </summary>
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("SubscriptionKey = ")
            .Append(string.IsNullOrEmpty(SubscriptionKey) ? "(unset)" : "(redacted)")
            .Append(", Region = ").Append(Region)
            .Append(", VoiceName = ").Append(VoiceName)
            .Append(", Locale = ").Append(Locale)
            .Append(", SampleRate = ").Append(SampleRate);
        return true;
    }
}
