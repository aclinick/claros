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
    bool StoppedByGate);
