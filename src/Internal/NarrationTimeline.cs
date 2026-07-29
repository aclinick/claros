namespace Claros.Internal;

/// <summary>
/// Pure timeline mixing for offline narration. Lays synthesized clips onto one
/// silent mono timeline at sample offsets and additively mixes overlaps. Kept
/// free of any audio-runtime or synthesis dependency so it is unit-testable.
/// </summary>
internal static class NarrationTimeline
{
    /// <summary>A clip placed at a sample offset on the timeline.</summary>
    internal readonly record struct Placement(int StartSample, float[] Samples);

    /// <summary>
    /// Mixes <paramref name="placements"/> onto a single timeline whose length is
    /// at least <paramref name="minLengthSamples"/> and long enough to contain
    /// every clip. Negative start offsets are clamped to zero. The returned buffer
    /// may exceed ±1; callers clamp when quantizing to PCM.
    /// </summary>
    public static float[] Mix(IReadOnlyList<Placement> placements, int minLengthSamples)
    {
        ArgumentNullException.ThrowIfNull(placements);
        ArgumentOutOfRangeException.ThrowIfNegative(minLengthSamples);

        var total = minLengthSamples;
        foreach (var (start, samples) in placements)
        {
            total = Math.Max(total, Math.Max(0, start) + samples.Length);
        }

        var timeline = new float[total];
        foreach (var (start, samples) in placements)
        {
            var offset = Math.Max(0, start);
            for (var i = 0; i < samples.Length; i++)
            {
                timeline[offset + i] += samples[i]; // additive mix
            }
        }

        return timeline;
    }

    /// <summary>
    /// Applies a short linear fade-in and fade-out to a clip's edges (in place) so
    /// adjacent or overlapping clips don't begin or end with an audible click.
    /// </summary>
    public static void ApplyEdgeFade(float[] samples, int sampleRate, double milliseconds = 8)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (sampleRate <= 0) return;

        var n = Math.Min(samples.Length / 2, (int)(sampleRate * milliseconds / 1000.0));
        for (var i = 0; i < n; i++)
        {
            var gain = (float)(i / (double)n);
            samples[i] *= gain;
            samples[samples.Length - 1 - i] *= gain;
        }
    }

    /// <summary>Converts a timeline offset to a sample index at a sample rate.</summary>
    public static int ToSample(TimeSpan when, int sampleRate) =>
        (int)(when.TotalSeconds * sampleRate);
}
