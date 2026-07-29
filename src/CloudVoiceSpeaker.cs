using Microsoft.CognitiveServices.Speech;
using Claros.Internal;

namespace Claros;

/// <summary>
/// Speaks with a <em>hosted</em> voice — an Azure neural, HD, or MAI-Voice model —
/// through the same <see cref="ISpeechSynthesizer"/> contract the on-device
/// engines implement, so a caller can move a single voice to the cloud tier
/// without restructuring anything around it.
/// </summary>
/// <remarks>
/// <para>
/// This is strictly opt-in. Nothing in the library constructs one for you and no
/// code path falls back to it: on-device remains the default, and reaching a
/// hosted voice requires deliberately supplying a key and region in
/// <see cref="CloudVoiceOptions"/>. Use it when you need something the installed
/// voices cannot give you — a brand voice, or a locale that is not installed —
/// not as a substitute for them.
/// </para>
/// <para>
/// The trade is real and is reported honestly through
/// <see cref="Capabilities"/>: this engine is not <see cref="SynthesizerCapabilities.Offline"/>
/// and is <see cref="SynthesizerCapabilities.Metered"/>. Requests cost money,
/// first-audio latency depends on the network rather than on a warm local model,
/// and cancelling in flight does not reliably avoid the charge for work the
/// service has already done.
/// </para>
/// <para>
/// Instances are thread hostile; construct one per voice, keep it warm, and
/// serialize calls.
/// </para>
/// </remarks>
public sealed class CloudVoiceSpeaker : ISpeechSynthesizer
{
    private readonly SpeechConfig _config;
    private readonly SpeechSynthesizer _synth;
    private readonly string _locale;
    private readonly SingleFlightGate _gate = new();
    private bool _disposed;

    /// <summary>The hosted voice this speaker is bound to.</summary>
    public VoiceInfo Voice { get; }

    /// <summary>
    /// Networked and billed, but still word-boundary capable and fixed-format, so
    /// caption highlighting and timeline mixing both work.
    /// </summary>
    public SynthesizerCapabilities Capabilities => SynthesizerCapabilities.Hosted;

    private CloudVoiceSpeaker(
        VoiceInfo voice, SpeechConfig config, SpeechSynthesizer synth, string locale, AudioFormat outputFormat)
    {
        Voice = voice;
        _config = config;
        _synth = synth;
        _locale = locale;
        OutputFormat = outputFormat;
    }

    /// <summary>
    /// The audio this engine returns: mono 16-bit PCM at
    /// <see cref="CloudVoiceOptions.SampleRate"/>, fixed when the speaker is
    /// created. Knowing this without a request matters more here than on-device,
    /// because probing would be billed.
    /// </summary>
    public AudioFormat OutputFormat { get; }

    /// <summary>
    /// Connects to the hosted voice described by <paramref name="options"/>. This
    /// validates the settings but does not contact the service, so the first
    /// request is where credentials are actually proven.
    /// </summary>
    public static CloudVoiceSpeaker Connect(CloudVoiceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        var config = SpeechConfig.FromSubscription(options.SubscriptionKey, options.Region);
        config.SpeechSynthesisVoiceName = options.VoiceName;
        // Always explicit: results are parsed as a WAV container, so relying on
        // whatever the SDK happens to default to would risk reading raw samples
        // as though their first bytes were a RIFF header.
        config.SetSpeechSynthesisOutputFormat(OutputFormats.Resolve(options.SampleRate));

        SpeechSynthesizer? synth = null;
        try
        {
            // Null audio output: we want the samples back, not playback on whatever
            // device the machine happens to have.
            synth = new SpeechSynthesizer(config, audioConfig: null);
            var voice = VoiceInfo.Cloud(
                id: options.VoiceName,
                displayName: options.VoiceName,
                locale: options.Locale);
            return new CloudVoiceSpeaker(
                voice, config, synth, options.Locale, AudioFormat.Pcm16Mono(options.SampleRate));
        }
        catch (Exception ex)
        {
            synth?.Dispose();
            throw new SpeechSynthesisException(
                $"Could not create a hosted synthesizer for voice '{options.VoiceName}' " +
                $"in region '{options.Region}'.", ex);
        }
    }

    /// <summary>
    /// Synthesizes <paramref name="request"/> into a complete waveform. Plain text
    /// and prosody-shaped text are rendered through generated SSML so the hosted
    /// voice is selected by name; raw SSML is sent as supplied, in which case the
    /// document's own <c>&lt;voice&gt;</c> element decides who speaks.
    /// </summary>
    public Task<WaveformResult> SynthesizeAsync(
        SpeechSynthesisRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();
        ObjectDisposedException.ThrowIf(_disposed, this);

        var ssml = request.IsSsml
            ? request.Content
            : SsmlBuilder.BuildTextSsml(request.Content, request.Prosody, Voice.DisplayName, _locale);

        return RunAsync(ssml, cancellationToken);
    }

    /// <summary>
    /// Synthesizes <paramref name="request"/> in full, then writes the audio to
    /// <paramref name="sink"/> in ~100 ms chunks. Synthesis is buffered before the
    /// first write, so this decouples the consumer rather than lowering
    /// first-audio latency. The sink is not completed; the caller owns it.
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

    private async Task<WaveformResult> RunAsync(string ssml, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _gate.Enter(nameof(CloudVoiceSpeaker), "a synthesis request");
        try
        {
            return await SpeakAndReadAsync(ssml, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Exit();
        }
    }

    private async Task<WaveformResult> SpeakAndReadAsync(string ssml, CancellationToken cancellationToken)
    {
        var speakTask = _synth.SpeakSsmlAsync(ssml);

        // Unlike the embedded runtime — which crashes if stopped cross-thread and
        // therefore has to abandon work in place — a hosted request can and should
        // be stopped promptly, so a cancelled barge-in stops streaming billed audio
        // instead of running to completion in the background.
        if (cancellationToken.CanBeCanceled)
        {
            var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var registration = cancellationToken.Register(
                static s => ((TaskCompletionSource)s!).TrySetResult(), cancelled);

            if (await Task.WhenAny(speakTask, cancelled.Task).ConfigureAwait(false) != speakTask)
            {
                try { await _synth.StopSpeakingAsync().ConfigureAwait(false); }
                catch { /* best effort; the request may already have completed */ }

                // Drain the stopped request before unwinding. Its native result is
                // disposable, and leaving the task in flight would let the next
                // call - or Dispose - race an operation that is still active.
                try { using var abandoned = await speakTask.ConfigureAwait(false); }
                catch { /* the abandoned request's own failure; cancellation wins below */ }

                cancellationToken.ThrowIfCancellationRequested();
            }
        }

        using var result = await speakTask.ConfigureAwait(false);

        if (result.Reason == ResultReason.Canceled)
        {
            var details = SpeechSynthesisCancellationDetails.FromResult(result);
            cancellationToken.ThrowIfCancellationRequested();
            throw new SpeechSynthesisException(
                $"Hosted synthesis was canceled ({details.ErrorCode}): {details.ErrorDetails}");
        }

        var (samples, sampleRate) = WaveFile.ReadMono16(result.AudioData);
        return new WaveformResult(samples, sampleRate);
    }

    /// <summary>Releases the underlying hosted synthesizer. Safe to call twice.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _synth.Dispose();
    }
}
