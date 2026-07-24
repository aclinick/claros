using System.Runtime.Versioning;
using Windows.Speech.Internal;

namespace Windows.Speech;

/// <summary>
/// A single call "leg": one live recognizer bound to one audio source, such as
/// the local microphone or the far-end/incoming stream of a call. Write 16-bit
/// mono PCM as it arrives; each completed, punctuated sentence is raised through
/// <see cref="TranscriptFinalized"/> and accumulated for
/// <see cref="GetTranscript"/>, attributed to this leg's speaker.
///
/// This is the Windows counterpart of the Contoso-Finance Mac listener's
/// per-source <c>AudioService</c> (built on Apple's SpeechAnalyzer): one
/// recognizer per speaker and "finals only" emission (whole sentences, never
/// volatile partials), so two legs (for example advisor and customer) yield a
/// clean, exactly-attributed two-party transcript with no energy-based speaker
/// guessing. Because the on-device Live Captions model is light, running one leg
/// per speaker stays well within a competitive memory budget, unlike heavier
/// single-session engines that cannot afford a recognizer per channel.
///
/// Legs are thread hostile: write audio from one flow. Start one with
/// <see cref="EmbeddedTranscriber.StartLeg"/>.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class CallLegTranscriber : IDisposable
{
    private readonly LiveTranscriptionSession _session;
    private readonly CallLegTranscript _log;
    private readonly Func<DateTimeOffset> _clock;
    private readonly object _gate = new();
    private readonly Queue<TranscriptChunk> _pending = new();
    private bool _dispatching;
    private bool _stopped;
    private bool _disposed;

    /// <summary>
    /// How long <see cref="StopAsync"/> waits, after the recognizer's hypothesis
    /// stops growing, before the final flush (the quiet-plateau window).
    /// </summary>
    public static readonly TimeSpan DefaultStopSettle = TimeSpan.FromMilliseconds(600);

    // How often StopAsync polls the recognizer's hypothesis while waiting for it
    // to settle. Matches the recognizer's roughly half-second revision cadence.
    private static readonly TimeSpan StopPollInterval = TimeSpan.FromMilliseconds(100);

    /// <summary>Stable identifier for this leg's source (for example "advisor").</summary>
    public string SourceId { get; }

    /// <summary>Human-readable speaker label for this leg (for example "Anna").</summary>
    public string SourceLabel { get; }

    /// <summary>
    /// Raised once per completed sentence, in order, with the finalized chunk.
    /// Only fires for whole sentences; the still-in-progress trailing fragment is
    /// withheld until it terminates (or until <see cref="Stop"/> flushes it).
    /// </summary>
    public event Action<TranscriptChunk>? TranscriptFinalized;

    internal CallLegTranscriber(
        string sourceId,
        string sourceLabel,
        LiveTranscriptionSession session,
        Func<DateTimeOffset>? clock = null)
    {
        SourceId = sourceId;
        SourceLabel = sourceLabel;
        _session = session;
        _log = new CallLegTranscript(sourceId, sourceLabel);
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Feeds a block of 16-bit mono PCM (at the transcriber's sample rate) and
    /// emits any sentences that just completed as a result.
    /// </summary>
    public void Write(ReadOnlySpan<byte> pcm)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_stopped) throw new InvalidOperationException("This call leg has been stopped.");
        _session.Write(pcm);
        Drain(flush: false);
    }

    /// <summary>
    /// Signals end of audio for this leg and immediately emits any remaining tail
    /// as final chunks, without waiting for the recognizer to finish transcribing
    /// audio still in flight. Prefer <see cref="StopAsync"/>, which first lets the
    /// last-written audio be recognized so the closing words are not dropped.
    /// Idempotent; after a stop <see cref="Write"/> is no longer allowed.
    /// </summary>
    public void Stop()
    {
        if (_disposed || _stopped) return;
        _stopped = true;
        Drain(flush: true);
    }

    /// <summary>
    /// Marks the leg stopped, then waits for the recognizer's hypothesis to stop
    /// growing (a quiet plateau of <paramref name="settle"/>, default
    /// <see cref="DefaultStopSettle"/>) before emitting the final chunks, so the
    /// already-written tail is fully transcribed and the closing words are not
    /// dropped. Sentences that get confirmed while draining the tail are surfaced
    /// as they complete. This mirrors the Mac listener awaiting end-of-input.
    ///
    /// The wait is bounded by a timeout so a stalled recognizer cannot hang the
    /// stop. The underlying audio stream is intentionally not closed here: on some
    /// devices forcing end-of-input triggers a native fault, so the stream is only
    /// released on <see cref="Dispose"/> (typically at process exit). Idempotent;
    /// after a stop <see cref="Write"/> is no longer allowed.
    /// </summary>
    public async Task StopAsync(TimeSpan? settle = null, CancellationToken cancellationToken = default)
    {
        if (_disposed || _stopped) return;
        _stopped = true;

        var window = settle ?? DefaultStopSettle;
        var timeout = TimeSpan.FromSeconds(Math.Max(window.TotalSeconds * 8, 5));
        var start = DateTime.UtcNow;
        var lastText = _session.CurrentText;
        var lastChange = start;

        while (true)
        {
            try
            {
                await Task.Delay(StopPollInterval, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break; // still flush what we have
            }

            var now = DateTime.UtcNow;
            var text = _session.CurrentText;
            if (!string.Equals(text, lastText, StringComparison.Ordinal))
            {
                // The recognizer is still catching up on the tail; surface any
                // sentences it just confirmed and reset the quiet timer.
                lastText = text;
                lastChange = now;
                Drain(flush: false);
            }
            else if (now - lastChange >= window || now - start >= timeout)
            {
                break; // hypothesis has settled (or we timed out)
            }
        }

        Drain(flush: true);
    }

    /// <summary>A snapshot of this leg's finalized chunks, in order.</summary>
    public IReadOnlyList<TranscriptChunk> GetTranscript()
    {
        lock (_gate) return _log.Chunks.ToArray();
    }

    /// <summary>Clears this leg's accumulated transcript.</summary>
    public void ClearTranscript()
    {
        lock (_gate) _log.Clear();
    }

    private void Drain(bool flush)
    {
        var sentences = _session.CommitSentences(flush);
        IReadOnlyList<TranscriptChunk> added;
        lock (_gate) added = _log.Append(sentences, _clock());
        for (var i = 0; i < added.Count; i++) _pending.Enqueue(added[i]);
        DispatchPending();
    }

    // Drains the pending chunk queue in FIFO order. If a handler re-enters (for
    // example by calling Write from inside TranscriptFinalized), the nested call
    // returns immediately and its newly enqueued chunks are delivered in order by
    // the outermost dispatch loop, so events can never interleave or reorder.
    private void DispatchPending()
    {
        if (_dispatching) return;
        _dispatching = true;
        try
        {
            while (_pending.Count > 0)
            {
                TranscriptFinalized?.Invoke(_pending.Dequeue());
            }
        }
        finally
        {
            _dispatching = false;
        }
    }

    /// <summary>
    /// Releases the underlying recognizer. On some devices the native engine can
    /// fault during teardown; that is swallowed here. Prefer letting the process
    /// exit once the transcript has been read or persisted.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _session.Dispose(); } catch { /* native teardown may fault */ }
    }
}
