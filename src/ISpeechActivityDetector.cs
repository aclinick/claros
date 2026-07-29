using System.Runtime.Versioning;

namespace Claros;

/// <summary>Details of a voice-activity transition.</summary>
/// <param name="Position">
/// The total amount of audio processed when the transition was detected, measured
/// from the first sample fed to the detector. For a <c>SpeechStarted</c> event
/// this is slightly after the true onset (by the start hangover); for
/// <c>SpeechEnded</c> it is after the end hangover.
/// </param>
public sealed record SpeechActivityEventArgs(TimeSpan Position);

/// <summary>
/// Detects when speech starts and stops in a stream of <see cref="AudioBuffer"/>s,
/// giving the platform turn-taking and barge-in signals. Feed captured audio with
/// <see cref="Process"/>; it raises <see cref="SpeechStarted"/> and
/// <see cref="SpeechEnded"/> as the stream crosses in and out of speech.
/// </summary>
/// <remarks>
/// Implementations are thread-hostile: feed audio from one thread and serialize
/// calls. A detector is bound to a single <see cref="Format"/> for its lifetime.
/// </remarks>
[SupportedOSPlatform("windows")]
public interface ISpeechActivityDetector : IDisposable
{
    /// <summary>The audio layout this detector consumes.</summary>
    AudioFormat Format { get; }

    /// <summary>Whether the detector currently considers the stream to be speech.</summary>
    bool IsSpeaking { get; }

    /// <summary>Raised when the stream transitions from silence to speech.</summary>
    event EventHandler<SpeechActivityEventArgs>? SpeechStarted;

    /// <summary>Raised when the stream transitions from speech back to silence.</summary>
    event EventHandler<SpeechActivityEventArgs>? SpeechEnded;

    /// <summary>
    /// Feeds the next chunk of captured audio. The buffer's format must match
    /// <see cref="Format"/>. May raise zero or more events as the chunk is
    /// processed frame by frame.
    /// </summary>
    void Process(AudioBuffer audio);

    /// <summary>
    /// Returns to the initial silence state, clearing any buffered partial frame
    /// and hangover accumulators (for example when reusing the detector for a new
    /// stream).
    /// </summary>
    void Reset();
}
