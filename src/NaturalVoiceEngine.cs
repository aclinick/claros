using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using WindowsNaturalVoices.Internal;

namespace WindowsNaturalVoices;

/// <summary>
/// Loads a Natural Voice acoustic model (encoder plus autoregressive
/// decoder) from a shipped voice package and runs inference on a caller
/// supplied phoneme sequence. Returns the discrete codec tokens the
/// decoder emits; a separate vocoder converts tokens to waveform audio.
///
/// The engine is disposable and thread hostile; construct one per voice
/// and serialize calls to <see cref="SynthesizeAsync"/>.
/// </summary>
public sealed class NaturalVoiceEngine : IDisposable
{
    private readonly InferenceSession _encoder;
    private readonly InferenceSession _decoder;

    public VoiceInfo Voice { get; }
    public PhonemeTable Phonemes { get; }

    private NaturalVoiceEngine(
        VoiceInfo voice,
        InferenceSession encoder,
        InferenceSession decoder,
        PhonemeTable phonemes)
    {
        Voice = voice;
        _encoder = encoder;
        _decoder = decoder;
        Phonemes = phonemes;
    }

    /// <summary>
    /// Open the given voice for synthesis. Extracts the encoder and decoder
    /// ONNX payloads from the shipped model binaries in a temp directory and
    /// hands them to ONNX Runtime. Blocks on file IO but not on GPU init.
    /// </summary>
    public static NaturalVoiceEngine Load(VoiceInfo voice, NaturalVoiceEngineOptions? options = null)
    {
        options ??= new NaturalVoiceEngineOptions();

        var encoderBin = Path.Combine(voice.InstalledPath, "hd_am_v5_encoder.bin");
        var decoderBin = Path.Combine(voice.InstalledPath, "hd_am_v5_decoder.bin");
        var phonesPath = Path.Combine(voice.InstalledPath, "hd_phones.txt");

        if (!File.Exists(encoderBin) || !File.Exists(decoderBin) || !File.Exists(phonesPath))
        {
            throw new FileNotFoundException(
                $"Voice package at {voice.InstalledPath} does not contain the expected HD acoustic model files.");
        }

        var encoderOnnx = ModelExtractor.ExtractOnnx(encoderBin);
        var decoderOnnx = ModelExtractor.ExtractOnnx(decoderBin);

        var sessionOptions = new SessionOptions
        {
            LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_WARNING,
            GraphOptimizationLevel = options.GraphOptimizationLevel,
        };

        var encoder = new InferenceSession(encoderOnnx, sessionOptions);
        var decoder = new InferenceSession(decoderOnnx, sessionOptions);
        var phonemes = PhonemeTable.Load(phonesPath);

        return new NaturalVoiceEngine(voice, encoder, decoder, phonemes);
    }

    /// <summary>
    /// Run the encoder plus autoregressive decoder loop over a phoneme id
    /// sequence and return the discrete codec tokens the decoder emits.
    /// The sequence should start with <see cref="PhonemeTable.Bos"/> and end
    /// with <see cref="PhonemeTable.Eos"/>; the caller is responsible for
    /// grapheme to phoneme conversion.
    /// </summary>
    public Task<CodecTokens> SynthesizeAsync(
        IReadOnlyList<int> phonemeIds,
        SynthesisOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new SynthesisOptions();
        return Task.Run(() => SynthesizeCore(phonemeIds, options, cancellationToken), cancellationToken);
    }

    private CodecTokens SynthesizeCore(
        IReadOnlyList<int> phonemeIds,
        SynthesisOptions options,
        CancellationToken cancellationToken)
    {
        if (phonemeIds.Count == 0)
        {
            throw new ArgumentException("Phoneme sequence must contain at least one token.", nameof(phonemeIds));
        }

        var tokens = phonemeIds.ToArray();
        var encoderInput = new DenseTensor<int>(tokens, new[] { 1, tokens.Length });

        using var encoderResults = _encoder.Run(new[]
        {
            NamedOnnxValue.CreateFromTensor("inputs", encoderInput),
        });

        var encoderOutput = encoderResults.First(r => r.Name == "encoder_output").AsTensor<float>();
        var encoderOutputDense = new DenseTensor<float>(
            encoderOutput.ToArray(),
            encoderOutput.Dimensions.ToArray());

        var seqLen = tokens.Length;

        var decoderInputs = new float[4 * 512];
        var state20 = new[]
        {
            (h: new float[1024], c: new float[1024]),
            (h: new float[1024], c: new float[1024]),
            (h: new float[1024], c: new float[1024]),
        };
        var state80 = new[]
        {
            (h: new float[1024], c: new float[1024]),
            (h: new float[1024], c: new float[1024]),
        };
        var attentionHidden = new float[1024];
        var attentionCell = new float[1024];
        var attentionContext = new float[384];
        var attentionWeights = new float[seqLen];
        var attentionWeightsCum = new float[seqLen];
        // Seed a one-hot attention at position 0 so the decoder starts on the
        // BOS phone. Matches the reference Python pipeline.
        attentionWeights[0] = 1f;
        attentionWeightsCum[0] = 1f;

        var c20 = new List<long>(options.MaxDecoderSteps * 2);
        var c40 = new List<long>(options.MaxDecoderSteps * 2);

        var stoppedByGate = false;

        for (var step = 0; step < options.MaxDecoderSteps; step++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var stepInputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("encoder_output", encoderOutputDense),
                Tensor("decoder_inputs", decoderInputs, 4, 512),
                Tensor("decoder_states_20hz_0_h", state20[0].h, 1, 1, 1024),
                Tensor("decoder_states_20hz_0_c", state20[0].c, 1, 1, 1024),
                Tensor("decoder_states_20hz_1_h", state20[1].h, 1, 1, 1024),
                Tensor("decoder_states_20hz_1_c", state20[1].c, 1, 1, 1024),
                Tensor("decoder_states_20hz_2_h", state20[2].h, 1, 1, 1024),
                Tensor("decoder_states_20hz_2_c", state20[2].c, 1, 1, 1024),
                Tensor("decoder_states_80hz_0_h", state80[0].h, 1, 1, 1024),
                Tensor("decoder_states_80hz_0_c", state80[0].c, 1, 1, 1024),
                Tensor("decoder_states_80hz_1_h", state80[1].h, 1, 1, 1024),
                Tensor("decoder_states_80hz_1_c", state80[1].c, 1, 1, 1024),
                Tensor("attention_hidden", attentionHidden, 1, 1024),
                Tensor("attention_cell", attentionCell, 1, 1024),
                Tensor("attention_context", attentionContext, 1, 384),
                Tensor("attention_weights", attentionWeights, 1, seqLen),
                Tensor("attention_weights_cum", attentionWeightsCum, 1, seqLen),
            };

            using var results = _decoder.Run(stepInputs);

            c20.AddRange(results.First(r => r.Name == "c20hz").AsTensor<long>().ToArray());
            c40.AddRange(results.First(r => r.Name == "c40hz").AsTensor<long>().ToArray());

            decoderInputs = results.First(r => r.Name == "decoder_inputs_new").AsTensor<float>().ToArray();
            state20[0] = ReadLstm(results, "decoder_states_20hz_0");
            state20[1] = ReadLstm(results, "decoder_states_20hz_1");
            state20[2] = ReadLstm(results, "decoder_states_20hz_2");
            state80[0] = ReadLstm(results, "decoder_states_80hz_0");
            state80[1] = ReadLstm(results, "decoder_states_80hz_1");
            attentionHidden = results.First(r => r.Name == "attention_hidden_new").AsTensor<float>().ToArray();
            attentionCell = results.First(r => r.Name == "attention_cell_new").AsTensor<float>().ToArray();
            attentionContext = results.First(r => r.Name == "attention_context_new").AsTensor<float>().ToArray();
            attentionWeights = results.First(r => r.Name == "attention_weights_new").AsTensor<float>().ToArray();
            attentionWeightsCum = results.First(r => r.Name == "attention_weights_cum_new").AsTensor<float>().ToArray();

            var gate = results.First(r => r.Name == "gate_prob").AsTensor<float>().ToArray()[0];
            // Match the reference Python pipeline: compare the raw gate scalar
            // (this model already emits a sigmoid-shaped output, not a logit)
            // and apply a warmup guard so early steps cannot trigger stop.
            if (step > options.WarmupSteps && gate > options.StopThreshold)
            {
                stoppedByGate = true;
                break;
            }
        }

        return new CodecTokens(
            C20Hz: c20.ToArray(),
            C40Hz: c40.ToArray(),
            Steps: c20.Count / 2,
            StoppedByGate: stoppedByGate);
    }

    private static NamedOnnxValue Tensor(string name, float[] values, params int[] shape) =>
        NamedOnnxValue.CreateFromTensor(name, new DenseTensor<float>(values, shape));

    private static (float[] h, float[] c) ReadLstm(IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results, string prefix)
    {
        var h = results.First(r => r.Name == prefix + "_new_h").AsTensor<float>().ToArray();
        var c = results.First(r => r.Name == prefix + "_new_c").AsTensor<float>().ToArray();
        return (h, c);
    }

    public void Dispose()
    {
        _encoder.Dispose();
        _decoder.Dispose();
    }
}

public sealed record NaturalVoiceEngineOptions
{
    public GraphOptimizationLevel GraphOptimizationLevel { get; init; } = GraphOptimizationLevel.ORT_ENABLE_BASIC;
}

public sealed record SynthesisOptions
{
    /// <summary>Hard cap on decoder iterations to avoid runaway generation.</summary>
    public int MaxDecoderSteps { get; init; } = 800;

    /// <summary>
    /// Threshold applied to the decoder's raw stop gate output. The model
    /// emits a scalar in the 0-1 range directly, so 0.5 matches the
    /// reference Python pipeline.
    /// </summary>
    public float StopThreshold { get; init; } = 0.5f;

    /// <summary>
    /// Number of decoder steps that must elapse before the stop gate can end
    /// generation. Prevents very short phrases from stopping prematurely.
    /// </summary>
    public int WarmupSteps { get; init; } = 20;
}
