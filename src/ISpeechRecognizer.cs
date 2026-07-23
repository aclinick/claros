using System.Runtime.Versioning;

namespace WindowsNaturalVoices;

/// <summary>
/// A live speech-to-text recognizer that consumes pushed <see cref="AudioBuffer"/>s
/// and produces an ordered stream of <see cref="RecognitionEvent"/>s (volatile
/// partials, finalized sentences, and corrections). This is the recognition
/// counterpart of <see cref="ISpeechSynthesizer"/>, expressed over the same
/// Stage 1 audio primitives: audio flows in through <see cref="Write"/>, text flows
/// out through <see cref="ReadEventsAsync"/>.
/// </summary>
/// <remarks>
/// The typical shape is a producer/consumer pair: one flow writes microphone or
/// call-leg audio as it arrives while another awaits <see cref="ReadEventsAsync"/>.
/// Call <see cref="CompleteAsync"/> once, at end of audio, to drain the recognizer's
/// tail (so closing words are not lost) and finalize the last sentence; the event
/// stream then completes. Implementations are thread hostile with respect to
/// <see cref="Write"/>: push audio from a single flow.
/// </remarks>
[SupportedOSPlatform("windows")]
public interface ISpeechRecognizer : IDisposable
{
    /// <summary>The on-device recognition model this recognizer is bound to.</summary>
    TranscriptionModelInfo Model { get; }

    /// <summary>
    /// The audio layout this recognizer expects (mono 16-bit PCM at the model's
    /// sample rate). Buffers passed to <see cref="Write"/> must match it.
    /// </summary>
    AudioFormat Format { get; }

    /// <summary>
    /// Feeds a block of audio as it arrives. The buffer's <see cref="AudioBuffer.Format"/>
    /// must equal <see cref="Format"/>. Any recognition events this audio produces
    /// surface through <see cref="ReadEventsAsync"/>.
    /// </summary>
    void Write(AudioBuffer audio);

    /// <summary>
    /// Signals end of audio: waits briefly for the recognizer's hypothesis to
    /// settle so the last-written audio is fully transcribed, finalizes the
    /// trailing sentence, and completes the <see cref="ReadEventsAsync"/> stream.
    /// Idempotent; after completion <see cref="Write"/> must not be called.
    /// </summary>
    Task CompleteAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Yields recognition events in order as audio is recognized, until
    /// <see cref="CompleteAsync"/> drains and closes the stream (or
    /// <paramref name="cancellationToken"/> fires). Consume this from a single flow.
    /// </summary>
    IAsyncEnumerable<RecognitionEvent> ReadEventsAsync(CancellationToken cancellationToken = default);
}
