using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using System.Threading.Channels;
using Windows.Foundation;
using Windows.Media;
using Windows.Media.Audio;
using Windows.Media.Capture;
using Windows.Media.MediaProperties;
using Windows.Media.Render;

namespace WindowsNaturalVoices;

/// <summary>
/// A live-microphone <see cref="IAudioSource"/> backed by Windows
/// <see cref="AudioGraph"/>. It captures the default input device as mono 16-bit
/// PCM at a fixed rate (16 kHz by default, what the Live Captions recognizer
/// expects) and yields it in real-time chunks for the <see cref="SpeechConversation"/>
/// loop.
/// </summary>
/// <remarks>
/// Construct with <see cref="CreateAsync"/> (AudioGraph setup is asynchronous),
/// keep the instance for the life of the conversation, and dispose it to release
/// the capture device. This is a runtime device adapter and is exercised by the
/// sample apps, not by unit tests.
/// </remarks>
[SupportedOSPlatform("windows10.0.26100.0")]
public sealed class AudioGraphMicrophoneSource : IAudioSource, IAsyncDisposable
{
    private readonly AudioGraph _graph;
    private readonly AudioDeviceInputNode _input;
    private readonly AudioFrameOutputNode _output;
    private readonly Channel<AudioBuffer> _chunks =
        Channel.CreateUnbounded<AudioBuffer>(new UnboundedChannelOptions { SingleReader = true });

    private AudioGraphMicrophoneSource(
        AudioGraph graph, AudioDeviceInputNode input, AudioFrameOutputNode output, AudioFormat format)
    {
        _graph = graph;
        _input = input;
        _output = output;
        Format = format;
        _graph.QuantumStarted += OnQuantum;
    }

    /// <inheritdoc />
    public AudioFormat Format { get; }

    /// <summary>
    /// Creates and starts a microphone source at <paramref name="sampleRate"/> Hz
    /// mono. Throws <see cref="NaturalVoiceUnavailableException"/> if the audio
    /// graph or the input device cannot be initialized.
    /// </summary>
    public static async Task<AudioGraphMicrophoneSource> CreateAsync(int sampleRate = 16_000)
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
        var inputResult = await graph.CreateDeviceInputNodeAsync(MediaCategory.Speech);
        if (inputResult.Status != AudioDeviceNodeCreationStatus.Success)
        {
            graph.Dispose();
            throw new NaturalVoiceUnavailableException(
                $"Could not open the default microphone: {inputResult.Status}.");
        }

        var output = graph.CreateFrameOutputNode();
        inputResult.DeviceInputNode.AddOutgoingConnection(output);

        var source = new AudioGraphMicrophoneSource(
            graph, inputResult.DeviceInputNode, output, AudioFormat.Pcm16Mono(sampleRate));
        graph.Start();
        return source;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<AudioBuffer> ReadAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var chunk in _chunks.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            yield return chunk;
    }

    private unsafe void OnQuantum(AudioGraph sender, object args)
    {
        using var frame = _output.GetFrame();
        using var audioBuffer = frame.LockBuffer(AudioBufferAccessMode.Read);
        using var reference = audioBuffer.CreateReference();

        ((IMemoryBufferByteAccess)reference).GetBuffer(out var dataInBytes, out var capacity);
        if (capacity == 0) return;

        // AudioGraph frames are 32-bit float; convert to the 16-bit PCM the
        // speech interfaces use.
        var floats = new ReadOnlySpan<float>(dataInBytes, (int)capacity / sizeof(float));
        var pcm = new float[floats.Length];
        floats.CopyTo(pcm);
        _chunks.Writer.TryWrite(AudioBuffer.FromSamples(pcm, Format));
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _graph.QuantumStarted -= OnQuantum;
        _chunks.Writer.TryComplete();
        try { _graph.Stop(); } catch { /* best effort */ }
        _output.Dispose();
        _input.Dispose();
        _graph.Dispose();
        await Task.CompletedTask;
    }

    [System.Runtime.InteropServices.ComImport]
    [System.Runtime.InteropServices.Guid("5B0D3235-4DBA-4D44-865E-8F1D0E4FD04D")]
    [System.Runtime.InteropServices.InterfaceType(
        System.Runtime.InteropServices.ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMemoryBufferByteAccess
    {
        unsafe void GetBuffer(out byte* buffer, out uint capacity);
    }
}
