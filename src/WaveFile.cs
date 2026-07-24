namespace Windows.Speech;

/// <summary>
/// Minimal RIFF WAV writer used by <c>Windows.Speech.Demo</c> so the
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

    /// <summary>
    /// Decode a 16 bit mono PCM RIFF WAV into normalized float samples in the
    /// range [-1, 1]. Walks the RIFF chunk list rather than assuming a fixed
    /// header layout, so streams that carry extra chunks (as the Embedded
    /// Speech runtime emits) decode correctly. Returns the samples and the
    /// sample rate declared in the <c>fmt </c> chunk.
    /// </summary>
    public static (float[] Samples, int SampleRate) ReadMono16(byte[] wav)
    {
        ArgumentNullException.ThrowIfNull(wav);
        if (wav.Length < 12 ||
            wav[0] != (byte)'R' || wav[1] != (byte)'I' || wav[2] != (byte)'F' || wav[3] != (byte)'F' ||
            wav[8] != (byte)'W' || wav[9] != (byte)'A' || wav[10] != (byte)'V' || wav[11] != (byte)'E')
        {
            throw new ArgumentException("Buffer is not a RIFF/WAVE stream.", nameof(wav));
        }

        int sampleRate = 0, channels = 0, bits = 0, formatTag = 0;
        int dataOffset = -1, dataLength = 0;
        bool sawFmt = false;

        int pos = 12;
        while (pos + 8 <= wav.Length)
        {
            var id = global::System.Text.Encoding.ASCII.GetString(wav, pos, 4);
            int size = BitConverter.ToInt32(wav, pos + 4);
            int body = pos + 8;
            if (size < 0 || body + size > wav.Length)
            {
                // Streaming headers can carry a placeholder size; take the rest.
                size = wav.Length - body;
            }

            if (id == "fmt " && size >= 16)
            {
                formatTag = BitConverter.ToInt16(wav, body);
                channels = BitConverter.ToInt16(wav, body + 2);
                sampleRate = BitConverter.ToInt32(wav, body + 4);
                bits = BitConverter.ToInt16(wav, body + 14);
                sawFmt = true;
            }
            else if (id == "data")
            {
                dataOffset = body;
                dataLength = size;
            }

            pos = body + size + (size & 1); // chunks are word aligned
        }

        if (!sawFmt || dataOffset < 0)
        {
            throw new ArgumentException("WAV stream is missing a fmt or data chunk.", nameof(wav));
        }
        const int WaveFormatPcm = 1;
        if (formatTag != WaveFormatPcm)
        {
            throw new ArgumentException(
                $"Only uncompressed PCM (format 1) is supported (got format {formatTag}).", nameof(wav));
        }
        if (channels != 1 || bits != 16)
        {
            throw new ArgumentException(
                $"Only 16 bit mono PCM is supported (got {bits} bit, {channels} channel).", nameof(wav));
        }

        int count = dataLength / sizeof(short);
        var samples = new float[count];
        for (int i = 0; i < count; i++)
        {
            samples[i] = BitConverter.ToInt16(wav, dataOffset + i * sizeof(short)) / 32768f;
        }
        return (samples, sampleRate);
    }
}
