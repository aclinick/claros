using System.Runtime.Versioning;
using System.Threading.Channels;
using Windows.Speech.Internal;

namespace Windows.Speech;

/// <summary>
/// The default <see cref="ISpeechRecognizer"/>: a thin runtime wrapper that drives
/// a <see cref="LiveTranscriptionSession"/> (the on-device Live Captions engine)
/// and folds its ever-growing streaming hypothesis into ordered
/// <see cref="RecognitionEvent"/>s through a <see cref="RecognitionReducer"/>.
/// Pushed audio is written straight to the session; the session's
/// <see cref="LiveTranscriptionSession.PartialUpdated"/> callback runs the reducer
/// and enqueues the resulting events onto a channel that
/// <see cref="ReadEventsAsync"/> drains.
/// </summary>
/// <remarks>
/// All reducer access (from the recognizer's callback thread and from
/// <see cref="CompleteAsync"/>) is serialized under a lock; the channel is the
/// thread-safe hand-off to the consumer. Create one with
/// <see cref="EmbeddedTranscriber.StartRecognizer"/>.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class StreamingRecognizer : ISpeechRecognizer
{
    /// <summary>
    /// How long <see cref="CompleteAsync"/> waits, after the hypothesis stops
    /// growing, before the final flush (the quiet-plateau window). Mirrors
    /// <see cref="CallLegTranscriber.DefaultStopSettle"/>.
    /// </summary>
    public static readonly TimeSpan DefaultSettle = TimeSpan.FromMilliseconds(600);

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

    private readonly LiveTranscriptionSession _session;
    private readonly RecognitionReducer _reducer = new();
    private readonly Channel<RecognitionEvent> _events =
        Channel.CreateUnbounded<RecognitionEvent>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });
    private readonly object _gate = new();

    private Task? _completion;
    private bool _completed;
    private bool _disposed;

    /// <inheritdoc />
    public TranscriptionModelInfo Model { get; }

    /// <inheritdoc />
    public AudioFormat Format { get; }

    internal StreamingRecognizer(TranscriptionModelInfo model, LiveTranscriptionSession session, int sampleRate)
    {
        Model = model;
        _session = session;
        Format = AudioFormat.Pcm16Mono(sampleRate);
        _session.PartialUpdated += OnPartialUpdated;
    }

    private void OnPartialUpdated(string text)
    {
        lock (_gate)
        {
            if (_completed) return;
            foreach (var evt in _reducer.Observe(text))
            {
                _events.Writer.TryWrite(evt);
            }
        }
    }

    /// <inheritdoc />
    public void Write(AudioBuffer audio)
    {
        ArgumentNullException.ThrowIfNull(audio);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_completion is not null || _completed) throw new InvalidOperationException("This recognizer has been completed.");
        if (!audio.Format.Equals(Format))
        {
            throw new ArgumentException(
                $"The recognizer expects {Format.SampleRate} Hz mono 16-bit audio, but the buffer is " +
                $"{audio.Format.SampleRate} Hz / {audio.Format.Channels}-channel / {audio.Format.BitsPerSample}-bit.",
                nameof(audio));
        }
        if (audio.IsEmpty) return;
        _session.Write(audio.Pcm.Span);
    }

    /// <inheritdoc />
    public Task CompleteAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_gate)
        {
            // Idempotent and safe under concurrency: every caller awaits the same
            // completion, so a second call observes actual completion (the closed
            // stream) rather than returning early. The first call's cancellation
            // token governs the settle wait.
            return _completion ??= CompleteCoreAsync(cancellationToken);
        }
    }

    private async Task CompleteCoreAsync(CancellationToken cancellationToken)
    {
        // Wait for the recognizer's hypothesis to settle so the already-written
        // tail is fully transcribed and the closing words are not dropped. The
        // PartialUpdated callback keeps surfacing events meanwhile.
        var timeout = TimeSpan.FromSeconds(Math.Max(DefaultSettle.TotalSeconds * 8, 5));
        var start = DateTime.UtcNow;
        var lastText = _session.CurrentText;
        var lastChange = start;
        while (true)
        {
            try { await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; } // still flush what we have

            var now = DateTime.UtcNow;
            // Absolute-timeout guard first, checked every iteration: a hypothesis
            // that keeps changing must not keep this loop alive past the ceiling.
            if (now - start >= timeout) break;

            var text = _session.CurrentText;
            if (!string.Equals(text, lastText, StringComparison.Ordinal))
            {
                lastText = text;
                lastChange = now;
            }
            else if (now - lastChange >= DefaultSettle)
            {
                break;
            }
        }

        // Flush: finalize the trailing sentence, mark completed, and close the
        // stream. Done under the lock so no in-flight PartialUpdated races the flush.
        lock (_gate)
        {
            foreach (var evt in _reducer.Observe(_session.CurrentText, flush: true))
            {
                _events.Writer.TryWrite(evt);
            }
            _completed = true;
            _events.Writer.TryComplete();
        }
    }

    /// <inheritdoc />
    public IAsyncEnumerable<RecognitionEvent> ReadEventsAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _events.Reader.ReadAllAsync(cancellationToken);
    }

    /// <summary>
    /// Releases the underlying recognition session. Completes the event stream if
    /// it is still open. On some devices the native engine can fault during
    /// teardown; that is swallowed. Safe to call more than once.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _session.PartialUpdated -= OnPartialUpdated;
        _events.Writer.TryComplete();
        try { _session.Dispose(); } catch { /* native teardown may fault */ }
    }
}
