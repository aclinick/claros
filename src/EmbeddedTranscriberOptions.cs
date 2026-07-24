using System.Runtime.Versioning;

namespace Windows.Speech;

/// <summary>
/// Configuration for <see cref="EmbeddedTranscriber"/>, the offline
/// speech-to-text engine that drives the Windows Live Captions recognition
/// model through Microsoft's on-device Azure Embedded Speech runtime.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed record EmbeddedTranscriberOptions
{
    /// <summary>
    /// Copy the gated native recognition extension DLLs and UWP VC++ runtimes
    /// out of the OS next to the running application on load, so the Embedded
    /// Speech runtime can be resolved. Disable when the host already deploys
    /// them.
    /// </summary>
    public bool StageNativeRuntime { get; init; } = true;

    /// <summary>
    /// Emit partial (in-progress) hypotheses through the <c>onPartial</c>
    /// callback of <see cref="EmbeddedTranscriber.TranscribeFileAsync"/> as audio
    /// is consumed. When <c>false</c>, that callback is not invoked (the final
    /// text is still returned). This does not affect
    /// <see cref="LiveTranscriptionSession"/>, whose whole purpose is to stream
    /// partials.
    /// </summary>
    public bool EmitPartialResults { get; init; } = true;

    /// <summary>
    /// Mask profanity in recognized text (replacing it with asterisks) rather
    /// than emitting it verbatim. Defaults to <c>false</c> (verbatim), matching
    /// a transcription use case.
    /// </summary>
    public bool MaskProfanity { get; init; }

    /// <summary>
    /// Silence, in milliseconds, the recognizer waits before declaring an
    /// end-of-utterance. This is deliberately set very high by default so the
    /// engine stays in a continuous streaming state and never runs its built-in
    /// end-of-utterance finalizer, which is unstable on some devices. Turn
    /// segmentation is instead driven by the caller through
    /// <see cref="LiveTranscriptionSession.Commit"/>. Lower it only if you have
    /// verified the native finalizer is stable on your target hardware.
    /// </summary>
    public int SegmentationSilenceTimeoutMs { get; init; } = 100_000;

    /// <summary>
    /// Sample rate, in hertz, of the 16-bit mono PCM audio fed to the recognizer.
    /// The Live Captions models accept 16000 (default) or 8000.
    /// </summary>
    public int SampleRate { get; init; } = 16_000;
}
