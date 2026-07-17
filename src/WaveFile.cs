namespace WindowsNaturalVoices;

/// <summary>
/// Minimal RIFF WAV writer used by <c>WindowsNaturalVoices.Demo</c> so the
/// sample has no extra dependencies. Writes 16 bit mono PCM.
/// </summary>
public static class WaveFile
{
    /// <summary>
    /// Write <paramref name="samples"/> to <paramref name="path"/> as 16 bit
    /// mono PCM at <paramref name="sampleRate"/>. Callers can override the
    /// sample rate written to the WAV header to intentionally re-pitch
    /// playback without touching the samples themselves.
    /// </summary>
    public static void WriteMono16(string path, float[] samples, int sampleRate)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(samples);

        var byteCount = samples.Length * sizeof(short);
        using var fs = File.Create(path);
        using var w = new BinaryWriter(fs);

        w.Write("RIFF"u8);
        w.Write(36 + byteCount);
        w.Write("WAVE"u8);
        w.Write("fmt "u8);
        w.Write(16);                        // fmt chunk size
        w.Write((short)1);                   // PCM
        w.Write((short)1);                   // channels
        w.Write(sampleRate);
        w.Write(sampleRate * 2);             // byte rate
        w.Write((short)2);                   // block align
        w.Write((short)16);                  // bits per sample
        w.Write("data"u8);
        w.Write(byteCount);

        foreach (var s in samples)
        {
            var v = (int)Math.Clamp(s * 32767f, -32768f, 32767f);
            w.Write((short)v);
        }
    }
}
