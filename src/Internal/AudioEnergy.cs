namespace Claros.Internal;

/// <summary>Pure audio-energy helpers used by the energy VAD.</summary>
internal static class AudioEnergy
{
    /// <summary>
    /// Root-mean-square amplitude of normalized samples (each in [-1, 1]). Returns
    /// 0 for an empty span. This is the loudness measure the endpointer thresholds.
    /// </summary>
    public static double Rms(ReadOnlySpan<float> samples)
    {
        if (samples.Length == 0) return 0.0;

        double sumSquares = 0.0;
        foreach (var s in samples) sumSquares += (double)s * s;
        return Math.Sqrt(sumSquares / samples.Length);
    }
}
