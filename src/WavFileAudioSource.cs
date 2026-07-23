using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace WindowsNaturalVoices;

/// <summary>
/// An <see cref="IAudioSource"/> that replays a mono 16-bit PCM WAV file in
/// fixed-size chunks, optionally paced to real time so it behaves like a live
/// microphone. This makes the full <see cref="SpeechConversation"/> loop —
/// recognition, endpointing, and spoken responses — reproducible and headless-
/// testable against a recorded utterance instead of a physical microphone.
/// </summary>
/// <remarks>
/// The source resamples nothing: the WAV's own sample rate becomes the source's
/// <see cref="Format"/>, so pair it with a recognizer/detector bound to the same
/// rate (Live Captions expects 16 kHz mono). When <see cref="Realtime"/> is
/// <see langword="true"/> (the default) each chunk is released no faster than its
/// own duration, matching the wall-clock cadence a microphone would produce.
/// </remarks>
public sealed class WavFileAudioSource : IAudioSource
{
    private readonly float[] _samples;
    private readonly int _chunkSamples;
    private readonly bool _realtime;

    /// <summary>
    /// Loads <paramref name="wavPath"/> and prepares to replay it in chunks of
    /// <paramref name="chunkDuration"/> (default 100 ms), paced to real time when
    /// <paramref name="realtime"/> is <see langword="true"/>.
    /// </summary>
    public WavFileAudioSource(string wavPath, TimeSpan? chunkDuration = null, bool realtime = true)
    {
        ArgumentException.ThrowIfNullOrEmpty(wavPath);
        var (samples, sampleRate) = WaveFile.ReadMono16(File.ReadAllBytes(wavPath));
        _samples = samples;
        Format = AudioFormat.Pcm16Mono(sampleRate);

        var chunk = chunkDuration ?? TimeSpan.FromMilliseconds(100);
        if (chunk <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(chunkDuration), "Chunk duration must be positive.");
        _chunkSamples = Math.Max(1, (int)Math.Round(chunk.TotalSeconds * sampleRate));
        _realtime = realtime;
    }

    /// <inheritdoc />
    public AudioFormat Format { get; }

    /// <summary>Whether chunks are released paced to real time (like a live mic).</summary>
    public bool Realtime => _realtime;

    /// <inheritdoc />
    public async IAsyncEnumerable<AudioBuffer> ReadAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var clock = Stopwatch.StartNew();
        var elapsedAudio = TimeSpan.Zero;

        for (var offset = 0; offset < _samples.Length; offset += _chunkSamples)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var count = Math.Min(_chunkSamples, _samples.Length - offset);
            var chunk = new float[count];
            Array.Copy(_samples, offset, chunk, 0, count);
            var buffer = AudioBuffer.FromSamples(chunk, Format);

            if (_realtime)
            {
                elapsedAudio += buffer.Duration;
                var behind = elapsedAudio - clock.Elapsed;
                if (behind > TimeSpan.Zero)
                    await Task.Delay(behind, cancellationToken).ConfigureAwait(false);
            }

            yield return buffer;
        }
    }
}
