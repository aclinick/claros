using System.Runtime.Versioning;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using WindowsNaturalVoices.Internal;

namespace WindowsNaturalVoices;

/// <summary>
/// Offline speech-to-text: transcribes audio with the same on-device recognition
/// model that powers Windows Live Captions, through Microsoft's own Azure
/// Embedded Speech runtime. This is the recognition counterpart to
/// <see cref="EmbeddedVoiceSpeaker"/>: it hands audio to Microsoft's exact
/// streaming conformer-transducer engine, so the text, punctuation,
/// capitalization, and inverse text normalization match what Live Captions
/// produces. Everything runs locally; no network call is made.
///
/// Instances are thread hostile; construct one per model and serialize calls.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class EmbeddedTranscriber : IDisposable
{
    private readonly EmbeddedSpeechConfig _config;
    private readonly EmbeddedTranscriberOptions _options;
    private readonly List<LiveTranscriptionSession> _sessions = new();
    private bool _disposed;

    /// <summary>The recognition model this transcriber is bound to.</summary>
    public TranscriptionModelInfo Model { get; }

    private EmbeddedTranscriber(
        TranscriptionModelInfo model,
        EmbeddedSpeechConfig config,
        EmbeddedTranscriberOptions options)
    {
        Model = model;
        _config = config;
        _options = options;
    }

    /// <summary>
    /// Load <paramref name="model"/> for recognition through the Embedded Speech
    /// runtime. <paramref name="license"/> is the Microsoft-issued license string
    /// for the on-device models; when it is <c>null</c> or empty the license
    /// notice embedded in the installed model package is used automatically.
    /// </summary>
    /// <exception cref="NaturalVoiceUnavailableException">
    /// The model package, the gated recognition runtime, or the license could
    /// not be found on this machine.
    /// </exception>
    /// <exception cref="SpeechSynthesisException">
    /// The Embedded Speech runtime failed to initialize for the model.
    /// </exception>
    public static EmbeddedTranscriber Load(
        TranscriptionModelInfo model,
        string? license = null,
        EmbeddedTranscriberOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        options ??= new EmbeddedTranscriberOptions();

        if (!Directory.Exists(model.InstalledPath))
        {
            throw new NaturalVoiceUnavailableException(
                $"Recognition model package for '{model.Locale}' was not found at '{model.InstalledPath}'.");
        }

        license = string.IsNullOrEmpty(license)
            ? EmbeddedSpeechLicense.ResolveFromPackage(model.InstalledPath)
            : license;

        if (options.StageNativeRuntime)
        {
            EmbeddedSpeechRuntime.StageRecognition(AppContext.BaseDirectory);
        }

        try
        {
            var config = EmbeddedSpeechConfig.FromPath(model.InstalledPath);
            config.SetSpeechRecognitionModel(model.ModelName, license);
            config.SetProfanity(options.MaskProfanity ? ProfanityOption.Masked : ProfanityOption.Raw);
            // Keep the engine in a continuous streaming state; see the option docs.
            config.SetProperty(
                PropertyId.Speech_SegmentationSilenceTimeoutMs,
                options.SegmentationSilenceTimeoutMs.ToString(System.Globalization.CultureInfo.InvariantCulture));
            return new EmbeddedTranscriber(model, config, options);
        }
        catch (Exception ex) when (ex is not NaturalVoiceException)
        {
            throw new SpeechSynthesisException(
                $"Failed to initialize the Embedded Speech recognition runtime for model '{model.ModelName}'. " +
                "Confirm the native runtime is staged and the license is valid.", ex);
        }
    }

    /// <summary>
    /// Starts a live, push-driven transcription session. Write 16-bit mono PCM
    /// audio (at <see cref="EmbeddedTranscriberOptions.SampleRate"/>) as it
    /// arrives, observe the streaming text through the returned session, and call
    /// <see cref="LiveTranscriptionSession.Commit"/> at each turn boundary. Ideal
    /// for a two-party call with one audio channel per speaker: start one session
    /// per channel.
    /// </summary>
    public LiveTranscriptionSession StartSession()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var session = new LiveTranscriptionSession(_config, _options.SampleRate);
        lock (_sessions) _sessions.Add(session);
        session.StartAsync().GetAwaiter().GetResult();
        return session;
    }

    /// <summary>
    /// Starts a <see cref="CallLegTranscriber"/> for one call leg (one audio
    /// source / speaker). Feed this leg's mono 16-bit PCM as it arrives; each
    /// completed sentence is attributed to <paramref name="sourceLabel"/> and
    /// raised through <see cref="CallLegTranscriber.TranscriptFinalized"/>. Start
    /// one leg per speaker (for example one for the local microphone and one for
    /// the incoming/far-end stream) to get an exactly-attributed, finals-only
    /// multi-speaker transcript, matching the Contoso-Finance Mac listener.
    /// </summary>
    public CallLegTranscriber StartLeg(string sourceId, string sourceLabel)
    {
        ArgumentException.ThrowIfNullOrEmpty(sourceId);
        ArgumentException.ThrowIfNullOrEmpty(sourceLabel);
        return new CallLegTranscriber(sourceId, sourceLabel, StartSession());
    }

    /// <summary>
    /// Transcribe a mono 16-bit PCM WAV file and return its text. Audio is fed
    /// through a streaming session (the crash-prone native end-of-utterance
    /// finalizer is bypassed); when <paramref name="onPartial"/> is supplied and
    /// <see cref="EmbeddedTranscriberOptions.EmitPartialResults"/> is enabled,
    /// in-progress hypotheses are raised as the audio is consumed. The session is
    /// created and released within this call, so it is safe to call repeatedly.
    /// The result is split into sentence-level segments.
    /// </summary>
    public async Task<TranscriptionResult> TranscribeFileAsync(
        string wavPath,
        Action<string>? onPartial = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(wavPath);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!File.Exists(wavPath))
        {
            throw new FileNotFoundException("Audio file not found.", wavPath);
        }

        var (samples, sampleRate) = WaveFile.ReadMono16(await File.ReadAllBytesAsync(wavPath, cancellationToken)
            .ConfigureAwait(false));
        var pcm = ToPcm16(samples);

        var session = new LiveTranscriptionSession(_config, sampleRate);
        if (onPartial is not null && _options.EmitPartialResults) session.PartialUpdated += onPartial;
        try
        {
            await session.StartAsync().ConfigureAwait(false);

            // Feed the whole file in blocks, then wait for the hypothesis to settle.
            const int block = 32_000; // ~1s at 16k/16-bit
            for (var offset = 0; offset < pcm.Length; offset += block)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var length = Math.Min(block, pcm.Length - offset);
                session.Write(pcm.AsSpan(offset, length));
            }

            var audioSeconds = (double)pcm.Length / (sampleRate * 2);
            var text = await WaitForStableTextAsync(session, audioSeconds, cancellationToken)
                .ConfigureAwait(false);
            return TranscriptSegmenter.Split(text);
        }
        finally
        {
            session.Dispose();
        }
    }

    private static async Task<string> WaitForStableTextAsync(
        LiveTranscriptionSession session,
        double audioSeconds,
        CancellationToken cancellationToken)
    {
        // Give the engine time to consume the whole (bulk-written) stream, then
        // poll until the text stops changing. The ceiling scales with the audio
        // length so long files are fully drained, while a silent file that never
        // produces text still returns promptly.
        var ceiling = (int)Math.Ceiling(audioSeconds * 10) + 40; // ~audio + 4s
        var previous = string.Empty;
        var stableFor = 0;
        for (var i = 0; i < ceiling; i++)
        {
            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            var current = session.CurrentText;
            if (current == previous)
            {
                // Only treat non-empty text as "settled"; keep waiting through
                // leading silence until the first words appear.
                if (current.Length > 0 && ++stableFor >= 10) break; // ~1s unchanged
            }
            else
            {
                stableFor = 0;
                previous = current;
            }
        }
        return session.CurrentText;
    }

    private static byte[] ToPcm16(float[] samples)
    {
        var bytes = new byte[samples.Length * 2];
        for (var i = 0; i < samples.Length; i++)
        {
            var clamped = Math.Clamp(samples[i], -1f, 1f);
            var value = (short)Math.Round(clamped * short.MaxValue);
            bytes[i * 2] = (byte)(value & 0xFF);
            bytes[i * 2 + 1] = (byte)((value >> 8) & 0xFF);
        }
        return bytes;
    }

    /// <summary>
    /// Disposes the sessions created by this transcriber. Safe to call more than
    /// once. Do not dispose while a transcription is still running; let
    /// outstanding calls complete first. On some devices the native engine may
    /// fault during teardown, so prefer letting the process exit after you have
    /// read or persisted the transcript.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        LiveTranscriptionSession[] sessions;
        lock (_sessions)
        {
            sessions = _sessions.ToArray();
            _sessions.Clear();
        }
        foreach (var session in sessions)
        {
            try { session.Dispose(); }
            catch { /* native teardown may fault */ }
        }
    }
}
