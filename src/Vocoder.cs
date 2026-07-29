using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Claros.Internal;

namespace Claros;

/// <summary>
/// Waveform samples returned by <see cref="Vocoder.Synthesize"/> and
/// <see cref="NaturalVoiceSpeaker.SynthesizeAsync"/>.
/// </summary>
/// <param name="Samples">
/// Mono PCM samples in the range roughly <c>[-1, 1]</c>. Callers that need
/// integer PCM should scale by 32767 and clamp before writing to disk.
/// </param>
/// <param name="SampleRate">
/// The native sample rate the vocoder produced. The Microsoft HD vocoder
/// emits audio at 26000 Hz. Use <see cref="WaveformResult.WithSampleRate"/> to
/// relabel the same samples at another rate, which re-pitches rather than
/// resamples; relabelling to 24000 Hz slows and lowers the audio by roughly 8
/// percent and matches the Azure Ava reference timing more closely.
/// </param>
public sealed record WaveformResult(float[] Samples, int SampleRate)
{
    /// <summary>
    /// Reinterprets the same samples as though they had been recorded at
    /// <paramref name="sampleRate"/>, without resampling. Because no audio data
    /// changes, playback speed and pitch shift by the ratio between the rates:
    /// declaring a lower rate stretches and lowers the audio, a higher rate
    /// compresses and raises it.
    /// </summary>
    /// <remarks>
    /// This is a deliberate re-pitch, not a format conversion — nothing is
    /// interpolated and no quality is gained or lost. Its intended use is the
    /// known 26000 Hz to 24000 Hz relabel that slows the HD vocoder's output by
    /// roughly 8 percent to match the Azure reference timing. If you need audio
    /// that genuinely plays at a different rate <em>without</em> changing pitch,
    /// this is the wrong operation; resample the samples instead.
    /// </remarks>
    /// <param name="sampleRate">The rate to declare, in Hz. Must be positive.</param>
    public WaveformResult WithSampleRate(int sampleRate)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        return this with { SampleRate = sampleRate };
    }
}

/// <summary>
/// Loads a Natural Voice vocoder and converts codec tokens from
/// <see cref="NaturalVoiceEngine"/> into waveform audio.
///
/// The shipped vocoder ONNX uses a small family of custom operators in the
/// <c>test.customop</c> domain (see <see cref="Internal.StreamingOpRewriter"/>).
/// The extractor rewrites those into the standard ONNX ops of the same name
/// so stock ONNX Runtime can execute the graph.
/// </summary>
public sealed class Vocoder : IDisposable
{
    /// <summary>Sample rate the Microsoft HD vocoder produces.</summary>
    public const int NativeSampleRate = 26000;

    private readonly InferenceSession _session;
    private bool _disposed;

    /// <summary>Number of streaming operator nodes rewritten at load time.</summary>
    public int RewrittenNodes { get; }

    private Vocoder(InferenceSession session, int rewrittenNodes)
    {
        _session = session;
        RewrittenNodes = rewrittenNodes;
    }

    /// <summary>
    /// Load the vocoder that ships with <paramref name="voice"/>. Extracts the
    /// ONNX ModelProto from the shipped binary, rewrites the streaming custom
    /// operators, then hands the result to ONNX Runtime.
    /// </summary>
    public static Vocoder Load(VoiceInfo voice, NaturalVoiceEngineOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(voice);
        DeviceVoiceGuard.RequireOnDevice(voice, nameof(voice));
        options ??= new NaturalVoiceEngineOptions();
        var vocoderBin = Path.Combine(voice.InstalledPath, "hd_device_vocoder_v6_streaming.bin");
        if (!File.Exists(vocoderBin))
        {
            throw new NaturalVoiceUnavailableException(
                $"Voice '{voice.DisplayName}' package at {voice.InstalledPath} is missing required file 'hd_device_vocoder_v6_streaming.bin'.");
        }

        byte[] rewritten;
        int count;
        try
        {
            var rawOnnx = ModelExtractor.ExtractOnnx(vocoderBin);
            rewritten = StreamingOpRewriter.Rewrite(rawOnnx, out count);
        }
        catch (Exception ex) when (ex is not NaturalVoiceException)
        {
            throw new VoicePackageFormatException(
                $"Could not prepare the vocoder for voice '{voice.DisplayName}' from {voice.InstalledPath}.", ex);
        }

        using var sessionOptions = new SessionOptions
        {
            LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_WARNING,
            GraphOptimizationLevel = options.GraphOptimizationLevel,
        };

        InferenceSession? session = null;
        try
        {
            session = new InferenceSession(rewritten, sessionOptions);
            return new Vocoder(session, count);
        }
        catch (Exception ex)
        {
            session?.Dispose();
            if (ex is NaturalVoiceException) throw;
            throw new VoicePackageFormatException(
                $"Could not load the vocoder for voice '{voice.DisplayName}'.", ex);
        }
    }

    /// <summary>
    /// Run the vocoder over the two codec token streams the acoustic model
    /// emits and return mono PCM samples at <see cref="NativeSampleRate"/>.
    /// The result is peak-normalized to 0.9 to match the reference pipeline.
    /// </summary>
    public WaveformResult Synthesize(CodecTokens tokens)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (tokens.C20Hz.Length == 0)
        {
            return new WaveformResult(Array.Empty<float>(), NativeSampleRate);
        }

        try
        {
            // Codec tokens come out of the decoder interleaved by step
            // (step0_ch0, step0_ch1, step1_ch0, step1_ch1, ...). The vocoder
            // expects a channel-major layout of shape [1, 2, steps]. Rearrange.
            var token1 = ToChannelMajor(tokens.C20Hz);
            var steps1 = tokens.C20Hz.Length / 2;

            var t1 = new DenseTensor<long>(token1, new[] { 1, 2, steps1 });
            var t2 = new DenseTensor<long>(tokens.C40Hz, new[] { 1, 1, tokens.C40Hz.Length });

            using var results = _session.Run(new[]
            {
                NamedOnnxValue.CreateFromTensor("token1", t1),
                NamedOnnxValue.CreateFromTensor("token2", t2),
            });

            var wave = results.First().AsTensor<float>().ToArray();
            Normalize(wave, 0.9f);
            return new WaveformResult(wave, NativeSampleRate);
        }
        catch (Exception ex) when (ex is not NaturalVoiceException)
        {
            throw new SpeechSynthesisException("Vocoder inference failed.", ex);
        }
    }

    internal static long[] ToChannelMajor(long[] interleaved)
    {
        var steps = interleaved.Length / 2;
        var result = new long[interleaved.Length];
        for (var i = 0; i < steps; i++)
        {
            result[i] = interleaved[2 * i];
            result[steps + i] = interleaved[2 * i + 1];
        }
        return result;
    }

    internal static void Normalize(float[] samples, float peak)
    {
        var max = 0f;
        for (var i = 0; i < samples.Length; i++)
        {
            var a = Math.Abs(samples[i]);
            if (a > max) max = a;
        }
        if (max <= 0f) return;
        var scale = peak / max;
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] *= scale;
        }
    }

    /// <summary>
    /// Releases the ONNX Runtime inference session backing this vocoder. Safe
    /// to call more than once.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _session.Dispose();
    }
}
