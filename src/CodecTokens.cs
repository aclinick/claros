namespace Claros;

/// <summary>
/// Result of running the acoustic model on a phoneme sequence. Contains
/// the two streams of discrete codec token indices the Natural Voice
/// decoder emits per step. Playback to audio requires a vocoder that
/// understands these tokens.
/// </summary>
/// <param name="C20Hz">
/// Codec tokens emitted at 20 Hz. Shape is <c>[steps, 2]</c> flattened
/// row major.
/// </param>
/// <param name="C40Hz">
/// Codec tokens emitted at 40 Hz. Shape is <c>[steps, 2]</c> flattened.
/// The decoder metadata labels this <c>c40hz</c> even though the file
/// naming convention on disk says 80 Hz.
/// </param>
/// <param name="Steps">Number of decoder steps executed.</param>
/// <param name="StoppedByGate">
/// True when generation stopped because the model's stop token crossed
/// the configured threshold; false when the safety step cap was hit.
/// </param>
public sealed record CodecTokens(
    long[] C20Hz,
    long[] C40Hz,
    int Steps,
    bool StoppedByGate)
{
    /// <inheritdoc cref="CodecTokens(long[], long[], int, bool)"/>
    public long[] C20Hz { get; } = ValidateC20Hz(C20Hz, Steps);

    /// <inheritdoc cref="CodecTokens(long[], long[], int, bool)"/>
    public long[] C40Hz { get; } =
        C40Hz ?? throw new ArgumentNullException(nameof(C40Hz));

    /// <inheritdoc cref="CodecTokens(long[], long[], int, bool)"/>
    public int Steps { get; } = Steps >= 0
        ? Steps
        : throw new ArgumentOutOfRangeException(
            nameof(Steps), Steps, "Decoder step count cannot be negative.");

    // The 20 Hz stream is the one with a fixed, provable shape: the decoder
    // appends two tokens per step and the vocoder reshapes it to [1, 2, steps].
    // The 40 Hz stream is consumed as a flat [1, 1, length] tensor, so its
    // per-step width is a model detail and is deliberately not constrained here.
    private static long[] ValidateC20Hz(long[] c20Hz, int steps)
    {
        ArgumentNullException.ThrowIfNull(c20Hz);

        if (steps >= 0 && c20Hz.Length != steps * 2)
        {
            throw new ArgumentException(
                $"The 20 Hz codec stream holds {c20Hz.Length} tokens, which is not the " +
                $"two-per-step layout the vocoder reshapes to [1, 2, {steps}]. " +
                $"Expected {steps * 2} tokens for {steps} decoder steps.",
                nameof(c20Hz));
        }

        return c20Hz;
    }
}
