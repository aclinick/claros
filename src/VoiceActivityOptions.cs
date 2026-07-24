namespace Windows.Speech;

/// <summary>
/// Tuning for the energy-based voice-activity detector
/// (<see cref="EnergyVoiceActivityDetector"/>). Thresholds are on RMS amplitude
/// of normalized samples (0 = silence, 1 = full scale). Using a higher
/// <see cref="StartThreshold"/> than <see cref="EndThreshold"/> gives hysteresis
/// so a voice hovering near the threshold doesn't rapidly toggle, and the
/// start/stop hangovers debounce brief spikes and pauses.
/// </summary>
public sealed record VoiceActivityOptions
{
    /// <summary>
    /// The window each RMS measurement covers. Incoming audio is split into frames
    /// of this length before thresholding. Defaults to 20 ms.
    /// </summary>
    public TimeSpan FrameDuration { get; init; } = TimeSpan.FromMilliseconds(20);

    /// <summary>RMS at or above which audio counts as speech. Defaults to 0.02.</summary>
    public double StartThreshold { get; init; } = 0.02;

    /// <summary>
    /// RMS below which audio counts as silence. Should be at or below
    /// <see cref="StartThreshold"/> for hysteresis. Defaults to 0.012.
    /// </summary>
    public double EndThreshold { get; init; } = 0.012;

    /// <summary>
    /// How much continuous above-threshold audio must accumulate before speech is
    /// declared started, rejecting brief clicks. Defaults to 60 ms.
    /// </summary>
    public TimeSpan StartHangover { get; init; } = TimeSpan.FromMilliseconds(60);

    /// <summary>
    /// How much continuous below-threshold audio must accumulate before speech is
    /// declared ended, so short pauses within an utterance don't end the turn.
    /// Defaults to 500 ms.
    /// </summary>
    public TimeSpan EndHangover { get; init; } = TimeSpan.FromMilliseconds(500);

    /// <summary>The default options.</summary>
    public static VoiceActivityOptions Default { get; } = new();

    /// <summary>
    /// Throws when the options are internally inconsistent (non-positive frame or
    /// hangovers, negative thresholds, or a start threshold below the end
    /// threshold).
    /// </summary>
    public void Validate()
    {
        if (FrameDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(FrameDuration), FrameDuration, "Frame duration must be positive.");
        if (StartHangover < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(StartHangover), StartHangover, "Start hangover cannot be negative.");
        if (EndHangover < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(EndHangover), EndHangover, "End hangover cannot be negative.");
        if (!double.IsFinite(StartThreshold) || StartThreshold < 0)
            throw new ArgumentOutOfRangeException(nameof(StartThreshold), StartThreshold, "Threshold must be a finite, non-negative value.");
        if (!double.IsFinite(EndThreshold) || EndThreshold < 0)
            throw new ArgumentOutOfRangeException(nameof(EndThreshold), EndThreshold, "Threshold must be a finite, non-negative value.");
        if (StartThreshold < EndThreshold)
            throw new ArgumentException("StartThreshold must be greater than or equal to EndThreshold for stable hysteresis.", nameof(StartThreshold));
    }
}
