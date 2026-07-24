using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Windows.Foundation;
using Windows.Media;
using Windows.Media.Audio;
using Windows.Media.MediaProperties;
using Windows.Media.Render;

namespace Windows.Speech;

/// <summary>
/// A speaker <see cref="IAudioSink"/> backed by Windows <see cref="AudioGraph"/>.
/// Buffers written to it (16-bit mono PCM at a fixed rate) are played out the
/// default render device, so the <see cref="SpeechConversation"/> loop's spoken
/// responses are heard live. Barge-in cancellation stops the write pump; the
/// graph keeps running so the next turn can play immediately.
/// </summary>
/// <remarks>
/// Construct with <see cref="CreateAsync"/>, keep it warm for the conversation's
/// lifetime, and dispose it to release the render device. This is a runtime device
/// adapter exercised by the sample apps, not unit tests.
/// </remarks>
[SupportedOSPlatform("windows10.0.26100.0")]
public sealed class AudioGraphSpeakerSink : IAudioSink, IAsyncDisposable
{
    private readonly AudioGraph _graph;
    private readonly AudioDeviceOutputNode _output;
    private readonly AudioFrameInputNode _input;
    private readonly global::System.Diagnostics.Stopwatch _clock = new();
    private double _submittedSeconds;

    // Keep at most this much audio queued ahead of the playhead. Bounding the
    // queue is what makes barge-in effective: when the caller cancels a response,
    // no more than this window can still be in flight, and the rest is discarded.
    private static readonly TimeSpan MaxLookahead = TimeSpan.FromMilliseconds(200);

    private AudioGraphSpeakerSink(
        AudioGraph graph, AudioDeviceOutputNode output, AudioFrameInputNode input, AudioFormat format)
    {
        _graph = graph;
        _output = output;
        _input = input;
        Format = format;
    }

    /// <inheritdoc />
    public AudioFormat Format { get; }

    /// <summary>
    /// Creates and starts a speaker sink at <paramref name="sampleRate"/> Hz mono
    /// (24 kHz suits the natural voices). Throws
    /// <see cref="NaturalVoiceUnavailableException"/> if the audio graph or the
    /// render device cannot be initialized.
    /// </summary>
    public static async Task<AudioGraphSpeakerSink> CreateAsync(int sampleRate = 24_000)
    {
        var settings = new AudioGraphSettings(AudioRenderCategory.Speech)
        {
            EncodingProperties = AudioEncodingProperties.CreatePcm((uint)sampleRate, 1, 16),
        };

        var graphResult = await AudioGraph.CreateAsync(settings);
        if (graphResult.Status != AudioGraphCreationStatus.Success)
        {
            throw new NaturalVoiceUnavailableException(
                $"Could not create the audio graph: {graphResult.Status}.");
        }

        var graph = graphResult.Graph;
        var outputResult = await graph.CreateDeviceOutputNodeAsync();
        if (outputResult.Status != AudioDeviceNodeCreationStatus.Success)
        {
            graph.Dispose();
            throw new NaturalVoiceUnavailableException(
                $"Could not open the default speaker: {outputResult.Status}.");
        }

        var format = AudioFormat.Pcm16Mono(sampleRate);
        var input = graph.CreateFrameInputNode(
            AudioEncodingProperties.CreatePcm((uint)sampleRate, 1, 16));
        input.AddOutgoingConnection(outputResult.DeviceOutputNode);

        var sink = new AudioGraphSpeakerSink(graph, outputResult.DeviceOutputNode, input, format);
        graph.Start();
        return sink;
    }

    /// <inheritdoc />
    public async ValueTask WriteAsync(AudioBuffer buffer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (!buffer.Format.Equals(Format))
        {
            throw new ArgumentException(
                "The buffer's format does not match the speaker's format.", nameof(buffer));
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (buffer.IsEmpty) return;

        var samples = buffer.ToSamples();
        var bytes = new byte[samples.Length * sizeof(float)];
        for (var i = 0; i < samples.Length; i++)
        {
            var value = BitConverter.GetBytes(samples[i]);
            Buffer.BlockCopy(value, 0, bytes, i * sizeof(float), sizeof(float));
        }

        var frame = new AudioFrame((uint)bytes.Length);
        WriteFrame(frame, bytes);

        // Pace submission to real time so no more than MaxLookahead of audio is
        // ever queued. If cancelled while waiting (barge-in), discard whatever is
        // still queued so the assistant stops promptly instead of draining the
        // whole buffered response.
        if (!_clock.IsRunning) _clock.Start();
        _submittedSeconds += buffer.Duration.TotalSeconds;
        var ahead = TimeSpan.FromSeconds(_submittedSeconds) - _clock.Elapsed;
        if (ahead > MaxLookahead)
        {
            try
            {
                await Task.Delay(ahead - MaxLookahead, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                try { _input.DiscardQueuedFrames(); } catch { /* best effort */ }
                _submittedSeconds = _clock.Elapsed.TotalSeconds;
                throw;
            }
        }

        _input.AddFrame(frame);
    }

    /// <inheritdoc />
    public ValueTask CompleteAsync(CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;

    private static unsafe void WriteFrame(AudioFrame frame, byte[] bytes)
    {
        using var buffer = frame.LockBuffer(AudioBufferAccessMode.Write);
        using var reference = buffer.CreateReference();
        ((IMemoryBufferByteAccess)reference).GetBuffer(out var dest, out var capacity);
        var length = Math.Min(bytes.Length, (int)capacity);
        new Span<byte>(bytes, 0, length).CopyTo(new Span<byte>(dest, length));
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        try { _graph.Stop(); } catch { /* best effort */ }
        _input.Dispose();
        _output.Dispose();
        _graph.Dispose();
        await Task.CompletedTask;
    }

    [ComImport]
    [Guid("5B0D3235-4DBA-4D44-865E-8F1D0E4FD04D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMemoryBufferByteAccess
    {
        unsafe void GetBuffer(out byte* buffer, out uint capacity);
    }
}
