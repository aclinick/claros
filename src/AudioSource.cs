using System.Runtime.CompilerServices;

namespace WindowsNaturalVoices;

/// <summary>
/// Factories for simple <see cref="IAudioSource"/> instances, chiefly an
/// in-memory source over a known sequence of buffers. Useful for feeding scripted
/// audio through the streaming speech interfaces in tests and for replaying
/// captured audio.
/// </summary>
public static class AudioSource
{
    /// <summary>
    /// Creates a source that yields <paramref name="buffers"/> in order. Every
    /// buffer must be in <paramref name="format"/>; empty buffers are yielded as
    /// given (they are valid, zero-length chunks).
    /// </summary>
    public static IAudioSource FromBuffers(AudioFormat format, IEnumerable<AudioBuffer> buffers)
    {
        ArgumentNullException.ThrowIfNull(format);
        ArgumentNullException.ThrowIfNull(buffers);
        return new EnumerableAudioSource(format, buffers);
    }

    private sealed class EnumerableAudioSource : IAudioSource
    {
        private readonly IEnumerable<AudioBuffer> _buffers;

        public EnumerableAudioSource(AudioFormat format, IEnumerable<AudioBuffer> buffers)
        {
            Format = format;
            _buffers = buffers;
        }

        public AudioFormat Format { get; }

        public async IAsyncEnumerable<AudioBuffer> ReadAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var buffer in _buffers)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ArgumentNullException.ThrowIfNull(buffer);
                if (!buffer.Format.Equals(Format))
                {
                    throw new ArgumentException(
                        "A buffer's format does not match the source's format.", "buffers");
                }
                yield return buffer;
                await Task.Yield();
            }
        }
    }
}
