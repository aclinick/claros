using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using WindowsNaturalVoices.Internal;

namespace WindowsNaturalVoices;

/// <summary>
/// Waveform samples returned by <see cref="Vocoder.Synthesize"/> and
/// <see cref="NaturalVoiceSpeaker.SpeakAsync"/>.
/// </summary>
/// <param name="Samples">
/// Mono PCM samples in the range roughly <c>[-1, 1]</c>. Callers that need
/// integer PCM should scale by 32767 and clamp before writing to disk.
/// </param>
/// <param name="SampleRate">
/// The native sample rate the vocoder produced. The Microsoft HD vocoder
/// emits audio at 26000 Hz. Playing this buffer back at 24000 Hz (write a
/// WAV header claiming 24000 Hz over the same samples) slows and lowers
/// the pitch by roughly 8 percent and matches the Azure Ava reference
/// timing more closely.
/// </param>
public sealed record WaveformResult(float[] Samples, int SampleRate);

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
        options ??= new NaturalVoiceEngineOptions();
        var vocoderBin = Path.Combine(voice.InstalledPath, "hd_device_vocoder_v6_streaming.bin");
        if (!File.Exists(vocoderBin))
        {
            throw new FileNotFoundException(
                $"Voice package at {voice.InstalledPath} does not contain the HD vocoder binary.");
        }

        var rawOnnx = ModelExtractor.ExtractOnnx(vocoderBin);
        var rewritten = StreamingOpRewriter.Rewrite(rawOnnx, out var count);

        var sessionOptions = new SessionOptions
        {
            LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_WARNING,
            GraphOptimizationLevel = options.GraphOptimizationLevel,
        };
        var session = new InferenceSession(rewritten, sessionOptions);
        return new Vocoder(session, count);
    }

    /// <summary>
    /// Run the vocoder over the two codec token streams the acoustic model
    /// emits and return mono PCM samples at <see cref="NativeSampleRate"/>.
    /// The result is peak-normalized to 0.9 to match the reference pipeline.
    /// </summary>
    public WaveformResult Synthesize(CodecTokens tokens)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        if (tokens.C20Hz.Length == 0)
        {
            return new WaveformResult(Array.Empty<float>(), NativeSampleRate);
        }

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

    public void Dispose() => _session.Dispose();
}
