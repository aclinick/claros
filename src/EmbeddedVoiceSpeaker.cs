using System.Runtime.Versioning;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using WindowsNaturalVoices.Internal;

namespace WindowsNaturalVoices;

/// <summary>
/// Flagship offline text-to-speech engine: drives a Windows Natural Voice
/// through Microsoft's own on-device Azure Embedded Speech runtime. Unlike
/// <see cref="NaturalVoiceSpeaker"/> (which reconstructs the pipeline from the
/// raw ONNX models and the SAPI text preprocessor), this speaker hands text to
/// Microsoft's exact neural front end and acoustic engine, so cadence,
/// punctuation, and pronunciation match what the OS itself produces.
///
/// By default it forces the high-fidelity HD acoustic model for every
/// utterance (see <see cref="EmbeddedVoiceOptions.ForceHd"/>) and stages the
/// gated native runtime out of the OS on load. Everything runs locally; no
/// network call is made. Using the embedded runtime requires a valid Microsoft
/// license string for the on-device models.
///
/// Instances are thread hostile; construct one per voice and serialize calls
/// to <see cref="SpeakAsync"/>.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class EmbeddedVoiceSpeaker : IDisposable
{
    private readonly SpeechSynthesizer _synth;
    private bool _disposed;

    /// <summary>The Natural Voice this speaker is bound to.</summary>
    public VoiceInfo Voice { get; }

    private EmbeddedVoiceSpeaker(VoiceInfo voice, SpeechSynthesizer synth)
    {
        Voice = voice;
        _synth = synth;
    }

    /// <summary>
    /// Load <paramref name="voice"/> for synthesis through the Embedded Speech
    /// runtime. <paramref name="license"/> is the Microsoft-issued license
    /// string for the on-device models; when it is <c>null</c> or empty the
    /// license notice embedded in the installed voice package is used
    /// automatically. When <see cref="EmbeddedVoiceOptions.ForceHd"/> is set, a
    /// threshold-patched overlay of the voice package is materialized so every
    /// utterance uses the HD model.
    /// </summary>
    public static EmbeddedVoiceSpeaker Load(
        VoiceInfo voice,
        string? license = null,
        EmbeddedVoiceOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(voice);
        options ??= new EmbeddedVoiceOptions();

        license = string.IsNullOrEmpty(license)
            ? EmbeddedSpeechLicense.ResolveFromPackage(voice.InstalledPath)
            : license;

        if (options.StageNativeRuntime)
        {
            EmbeddedSpeechRuntime.Stage(AppContext.BaseDirectory);
        }

        var packagePath = options.ForceHd ? BuildOverlay(voice, options) : voice.InstalledPath;
        if (!Directory.Exists(packagePath))
        {
            throw new NaturalVoiceUnavailableException(
                $"Voice package for '{voice.DisplayName}' was not found at '{packagePath}'.");
        }

        var format = ResolveFormat(options.SampleRate);

        SpeechSynthesizer? synth = null;
        try
        {
            var config = EmbeddedSpeechConfig.FromPath(packagePath);
            config.SetSpeechSynthesisOutputFormat(format);
            config.SetSpeechSynthesisVoice(voice.DisplayName, license);
            synth = new SpeechSynthesizer(config, (AudioConfig?)null);
            return new EmbeddedVoiceSpeaker(voice, synth);
        }
        catch (Exception ex) when (ex is not NaturalVoiceException)
        {
            synth?.Dispose();
            throw new SpeechSynthesisException(
                $"Failed to initialize the Embedded Speech runtime for voice '{voice.DisplayName}'. " +
                "Confirm the native runtime is staged and the license is valid.", ex);
        }
    }

    /// <summary>
    /// Convert <paramref name="text"/> to a waveform. Cancellation stops the
    /// in-flight synthesis. Throws <see cref="SpeechSynthesisException"/> when
    /// the runtime cancels the request.
    /// </summary>
    public async Task<WaveformResult> SpeakAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        using var registration = cancellationToken.CanBeCanceled
            ? cancellationToken.Register(static s => ((SpeechSynthesizer)s!).StopSpeakingAsync(), _synth)
            : default;

        using var result = await _synth.SpeakTextAsync(text).ConfigureAwait(false);

        if (result.Reason == ResultReason.Canceled)
        {
            var details = SpeechSynthesisCancellationDetails.FromResult(result);
            cancellationToken.ThrowIfCancellationRequested();
            throw new SpeechSynthesisException(
                $"Embedded synthesis was canceled ({details.ErrorCode}): {details.ErrorDetails}");
        }

        // The stop request can race the completion: if cancellation fired after
        // synthesis started but the result still came back as completed, honor it.
        cancellationToken.ThrowIfCancellationRequested();

        var (samples, sampleRate) = WaveFile.ReadMono16(result.AudioData);
        return new WaveformResult(samples, sampleRate);
    }

    private static string BuildOverlay(VoiceInfo voice, EmbeddedVoiceOptions options)
    {
        var root = options.OverlayRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WindowsNaturalVoices", "hd-overlays");

        var name = new DirectoryInfo(voice.InstalledPath.TrimEnd(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)).Name;
        var overlayDir = Path.Combine(root, name);

        return HdVoiceOverlay.Create(
            voice.InstalledPath, overlayDir, options.HdThreshold, options.PreferSymlink);
    }

    private static SpeechSynthesisOutputFormat ResolveFormat(int sampleRate) => sampleRate switch
    {
        24_000 => SpeechSynthesisOutputFormat.Riff24Khz16BitMonoPcm,
        16_000 => SpeechSynthesisOutputFormat.Riff16Khz16BitMonoPcm,
        48_000 => SpeechSynthesisOutputFormat.Riff48Khz16BitMonoPcm,
        _ => throw new ArgumentOutOfRangeException(
            nameof(sampleRate), sampleRate,
            "The on-device HD models support 24000 Hz output; 16000 and 48000 are also selectable."),
    };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _synth.Dispose();
    }
}
