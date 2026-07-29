namespace Windows.Speech;

/// <summary>
/// Where a <see cref="VoiceInfo"/>'s audio is produced — the tier a caller has
/// explicitly chosen for a voice.
/// </summary>
/// <remarks>
/// The library is local-first: every voice discovered from the OS is
/// <see cref="Device"/>, and no code path ever silently promotes a request to a
/// remote service. <see cref="Cloud"/> exists so a caller who deliberately opts
/// into a hosted engine can describe that voice with the same type, and so
/// device-only components can reject it loudly instead of failing obscurely on a
/// missing package path.
/// </remarks>
public enum VoiceSource
{
    /// <summary>
    /// Synthesized entirely on this machine from an installed voice package. No
    /// network, no metering, and available on every machine that has the voice.
    /// </summary>
    Device = 0,

    /// <summary>
    /// Synthesized by a hosted service the caller explicitly opted into. Carries
    /// network latency, per-use cost, and no offline guarantee; the package
    /// identity fields on <see cref="VoiceInfo"/> are empty for these voices.
    /// </summary>
    Cloud = 1,
}
