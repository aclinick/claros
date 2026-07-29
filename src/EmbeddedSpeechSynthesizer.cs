using System.Runtime.Versioning;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using Claros.Internal;

namespace Claros;

/// <summary>
/// Flagship offline text-to-speech engine: drives a Windows Natural Voice
/// through Microsoft's own on-device Azure Embedded Speech runtime. Unlike
/// <see cref="NaturalVoiceSynthesizer"/> (which reconstructs the pipeline from the
/// raw ONNX models and the SAPI text preprocessor), this synthesizer hands text to
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
/// to <see cref="SynthesizeAsync(SpeechSynthesisRequest, CancellationToken)"/>.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class EmbeddedSpeechSynthesizer : ISpeechSynthesizer
{
    private readonly EmbeddedSpeechConfig _config;
    private readonly SpeechSynthesizer _synth;
    private readonly SingleFlightGate _gate = new();
    private Task<SpeechSynthesisResult>? _pending;
    private bool _disposed;

    /// <summary>The Natural Voice this synthesizer is bound to.</summary>
    public VoiceInfo Voice { get; }

    private EmbeddedSpeechSynthesizer(
        VoiceInfo voice, EmbeddedSpeechConfig config, SpeechSynthesizer synth, AudioFormat outputFormat)
    {
        Voice = voice;
        _config = config;
        _synth = synth;
        OutputFormat = outputFormat;
    }

    /// <summary>
    /// The audio this voice produces: mono 16-bit PCM at
    /// <see cref="EmbeddedVoiceOptions.SampleRate"/>, fixed when the synthesizer is
    /// loaded.
    /// </summary>
    public AudioFormat OutputFormat { get; }

    /// <summary>
    /// Load <paramref name="voice"/> for synthesis through the Embedded Speech
    /// runtime. <paramref name="license"/> is the Microsoft-issued license
    /// string for the on-device models; when it is <c>null</c> or empty the
    /// license notice embedded in the installed voice package is used
    /// automatically. When <see cref="EmbeddedVoiceOptions.ForceHd"/> is set, a
    /// threshold-patched overlay of the voice package is materialized so every
    /// utterance uses the HD model.
    /// </summary>
    public static EmbeddedSpeechSynthesizer Load(
        VoiceInfo voice,
        string? license = null,
        EmbeddedVoiceOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(voice);
        DeviceVoiceGuard.RequireOnDevice(voice, nameof(voice));

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

        var format = OutputFormats.Resolve(options.SampleRate);

        SpeechSynthesizer? synth = null;
        try
        {
            var config = EmbeddedSpeechConfig.FromPath(packagePath);
            config.SetSpeechSynthesisOutputFormat(format);
            config.SetSpeechSynthesisVoice(voice.DisplayName, license);
            synth = new SpeechSynthesizer(config, (AudioConfig?)null);
            return new EmbeddedSpeechSynthesizer(
                voice, config, synth, AudioFormat.Pcm16Mono(options.SampleRate));
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
    /// Synthesizes <paramref name="request"/> — plain text, prosody-shaped text,
    /// or raw SSML — into a complete waveform. Prosody-shaped text is rendered
    /// through generated SSML (the on-device runtime applies prosody only via
    /// SSML). Cancellation stops the in-flight synthesis.
    /// </summary>
    /// <remarks>
    /// A plain <see cref="string"/> converts implicitly to a text request, so
    /// <c>SynthesizeAsync("hello")</c> is the simple case. This produces audio but
    /// does not play it; use <see cref="SpeakToDefaultOutputAsync"/> to play.
    /// </remarks>
    public Task<WaveformResult> SynthesizeAsync(
        SpeechSynthesisRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();

        if (request.RequiresSsml)
        {
            var ssml = request.IsSsml
                ? request.Content
                : SsmlBuilder.BuildTextSsml(
                    request.Content, request.Prosody, Voice.DisplayName, Voice.Locale);
            return RunAsync(s => s.SpeakSsmlAsync(ssml), cancellationToken);
        }

        return RunAsync(s => s.SpeakTextAsync(request.Content), cancellationToken);
    }

    /// <summary>
    /// Synthesizes <paramref name="request"/> in full, then writes the audio to
    /// <paramref name="sink"/> in ~100 ms <see cref="AudioBuffer"/> chunks so
    /// consumers receive uniform buffers and can cancel between chunks. Synthesis
    /// is buffered before the first write (the embedded engine returns a complete
    /// waveform), so this decouples the consumer rather than lowering
    /// first-audio latency. The sink is not completed; the caller owns its
    /// lifetime. The sink's format must match this voice's output (mono at its
    /// sample rate). When <paramref name="onWord"/> is supplied it is raised for
    /// each word as its audio is produced.
    /// </summary>
    public async Task SynthesizeToSinkAsync(
        SpeechSynthesisRequest request,
        IAudioSink sink,
        Action<SpokenWord>? onWord = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(sink);
        ObjectDisposedException.ThrowIf(_disposed, this);

        void OnWord(object? sender, SpeechSynthesisWordBoundaryEventArgs e)
        {
            if (string.IsNullOrEmpty(e.Text)) return;
            onWord!(new SpokenWord(e.Text, TimeSpan.FromTicks((long)e.AudioOffset), e.Duration));
        }

        if (onWord is not null) _synth.WordBoundary += OnWord;
        WaveformResult waveform;
        try
        {
            waveform = await SynthesizeAsync(request, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (onWord is not null) _synth.WordBoundary -= OnWord;
        }

        await SinkWriter.WriteAsync(sink, waveform, nameof(sink), cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<WaveformResult> RunAsync(
        Func<SpeechSynthesizer, Task<SpeechSynthesisResult>> speak,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        // The _pending drain below sequences one call after another, but only if
        // the calls are actually sequential. This catches genuine concurrency,
        // which would otherwise race the native engine.
        _gate.Enter(nameof(EmbeddedSpeechSynthesizer), "a synthesis request");
        try
        {
            return await SpeakAndReadAsync(speak, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Exit();
        }
    }

    private async Task<WaveformResult> SpeakAndReadAsync(
        Func<SpeechSynthesizer, Task<SpeechSynthesisResult>> speak,
        CancellationToken cancellationToken)
    {
        // A previously cancelled call may have abandoned an in-flight synthesis
        // that is still draining on the runtime's own thread. Never touch the
        // native engine concurrently: wait for it to go idle before starting the
        // next request.
        if (_pending is { } previous)
        {
            _pending = null;
            try { await previous.ConfigureAwait(false); }
            catch { /* result of an abandoned call; already surfaced to its caller */ }
        }

        // Draining the previous call may have taken a while; if we were cancelled
        // meanwhile, don't bother starting native synthesis just to abandon it.
        cancellationToken.ThrowIfCancellationRequested();

        var speakTask = speak(_synth);
        _pending = speakTask;

        // Cooperative cancellation only. This runtime crashes if StopSpeakingAsync
        // is invoked cross-thread from the cancellation callback (see the
        // VideoVoiceover sample's VoiceoverController), so on cancellation we stop
        // awaiting and leave the in-flight synthesis to complete on its own thread;
        // the _pending gate above keeps the next call from racing it.
        if (cancellationToken.CanBeCanceled)
        {
            var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var registration = cancellationToken.Register(
                static s => ((TaskCompletionSource)s!).TrySetResult(), cancelled);
            var finished = await Task.WhenAny(speakTask, cancelled.Task).ConfigureAwait(false);
            if (finished != speakTask)
                cancellationToken.ThrowIfCancellationRequested();
        }

        using var result = await speakTask.ConfigureAwait(false);
        _pending = null;

        if (result.Reason == ResultReason.Canceled)
        {
            var details = SpeechSynthesisCancellationDetails.FromResult(result);
            cancellationToken.ThrowIfCancellationRequested();
            throw new SpeechSynthesisException(
                $"Embedded synthesis was canceled ({details.ErrorCode}): {details.ErrorDetails}");
        }

        var (samples, sampleRate) = WaveFile.ReadMono16(result.AudioData);
        return new WaveformResult(samples, sampleRate);
    }

    /// <summary>
    /// Synthesize <paramref name="text"/> and play it live through the default
    /// audio output as it is produced, so narration begins immediately and
    /// streams in real time rather than being buffered to a file first. When
    /// <paramref name="onWord"/> is supplied it is raised for each word as the
    /// audio for that word is synthesized; because synthesis can run ahead of
    /// playback, treat these as synthesis-time boundaries (see
    /// <see cref="SpokenWord.Offset"/>) rather than exact playback cues.
    /// Cancellation stops playback in flight.
    /// </summary>
    public async Task SpeakToDefaultOutputAsync(
        string text,
        Action<SpokenWord>? onWord = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        using var audio = AudioConfig.FromDefaultSpeakerOutput();
        using var synth = new SpeechSynthesizer(_config, audio);

        void OnWord(object? sender, SpeechSynthesisWordBoundaryEventArgs e)
        {
            if (string.IsNullOrEmpty(e.Text)) return;
            onWord!(new SpokenWord(
                e.Text,
                TimeSpan.FromTicks((long)e.AudioOffset),
                e.Duration));
        }

        if (onWord is not null) synth.WordBoundary += OnWord;

        using var registration = cancellationToken.CanBeCanceled
            ? cancellationToken.Register(static s => ((SpeechSynthesizer)s!).StopSpeakingAsync(), synth)
            : default;

        try
        {
            using var result = await synth.SpeakTextAsync(text).ConfigureAwait(false);

            if (result.Reason == ResultReason.Canceled)
            {
                var details = SpeechSynthesisCancellationDetails.FromResult(result);
                cancellationToken.ThrowIfCancellationRequested();
                throw new SpeechSynthesisException(
                    $"Embedded synthesis was canceled ({details.ErrorCode}): {details.ErrorDetails}");
            }

            cancellationToken.ThrowIfCancellationRequested();
        }
        finally
        {
            if (onWord is not null) synth.WordBoundary -= OnWord;
        }
    }

    private static string BuildOverlay(VoiceInfo voice, EmbeddedVoiceOptions options)
    {
        var root = options.OverlayRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Claros", "hd-overlays");

        var name = new DirectoryInfo(voice.InstalledPath.TrimEnd(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)).Name;
        var overlayDir = Path.Combine(root, name);

        return HdVoiceOverlay.Create(
            voice.InstalledPath, overlayDir, options.HdThreshold, options.PreferSymlink);
    }

    /// <summary>
    /// Releases the underlying Embedded Speech synthesizer and its native
    /// resources. Safe to call more than once. Do not dispose while an
    /// utterance is still playing; let outstanding calls to
    /// <see cref="SpeakToDefaultOutputAsync"/> complete first.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // A cancelled call may have left synthesis running on the runtime's own
        // thread. Drain it (best effort) before disposing the native engine so we
        // never free it out from under an in-flight operation.
        if (_pending is { } pending)
        {
            _pending = null;
            try { pending.Wait(TimeSpan.FromSeconds(5)); }
            catch { /* abandoned call; its result was already surfaced or discarded */ }
        }

        _synth.Dispose();
    }
}
