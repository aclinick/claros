using System.Runtime.Versioning;
using System.Text;
using System.Threading.Channels;

namespace Claros;

/// <summary>
/// Handles one recognized user turn and returns what the assistant should say, or
/// <see langword="null"/> to stay silent. This is the conversation's natural AI
/// extensibility point: the platform does everything around it — capturing audio,
/// endpointing turns, recognizing the utterance, speaking the response, handling
/// barge-in — and this delegate is where a caller plugs in the intelligence that
/// decides <em>what</em> to say (an on-device LLM, a rules engine, a retrieval bot,
/// or a trivial echo). It is deliberately free of any language-model coupling so
/// the reusable speech plumbing stays generic and the opinionated part stays the
/// caller's choice.
/// </summary>
/// <param name="utterance">The recognized text of the user's turn.</param>
/// <param name="cancellationToken">Cancelled when the conversation stops.</param>
public delegate Task<SpeechSynthesisRequest?> ConversationTurnHandler(
    string utterance, CancellationToken cancellationToken);

/// <summary>
/// The on-device, barge-in conversation loop. It wires the streaming pieces
/// together: microphone audio (<see cref="IAudioSource"/>) is pushed to both a
/// recognizer (<see cref="ISpeechRecognizer"/>) and a voice-activity detector
/// (<see cref="ISpeechActivityDetector"/>); when the user finishes a turn the
/// recognized text is handed to a caller-supplied <see cref="ConversationTurnHandler"/>,
/// and its response is synthesized (<see cref="ISpeechSynthesizer"/>) to the
/// speaker (<see cref="IAudioSink"/>). If the user starts speaking while the
/// assistant is talking, synthesis is cancelled (barge-in) and the loop listens
/// again.
/// </summary>
/// <remarks>
/// The conversation <em>borrows</em> all five components and does not dispose them;
/// the caller owns their lifetimes. A turn boundary is signalled by the activity
/// detector's <c>SpeechEnded</c>, and the turn's text is whatever the recognizer
/// finalized during that speech. Because synthesis is cancelled cooperatively
/// through <see cref="ISpeechSynthesizer.SynthesizeToSinkAsync"/>, barge-in never
/// issues a cross-thread native stop.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class SpeechConversation : IDisposable, IAsyncDisposable
{
    // How long teardown waits for a running loop to unwind before releasing the
    // components anyway, so a wedged loop cannot hang disposal forever.
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(5);

    // Identifies the conversation whose loop owns the current async flow, so a
    // disposal attempted from inside a turn handler or event callback is rejected
    // instead of dead-locking on itself. It holds the instance rather than a flag
    // so that code running inside one conversation's handler can still dispose a
    // different, unrelated conversation.
    private static readonly AsyncLocal<SpeechConversation?> ActiveLoop = new();

    private readonly IAudioSource _microphone;
    private readonly ISpeechRecognizer _recognizer;
    private readonly ISpeechActivityDetector _activityDetector;
    private readonly ISpeechSynthesizer _synthesizer;
    private readonly IAudioSink _speaker;
    private readonly ConversationTurnHandler _turnHandler;

    // Components this conversation created and must therefore dispose. Empty when
    // the caller built the components themselves, so the public constructor keeps
    // its borrow-only contract and never disposes something it did not create.
    private readonly IReadOnlyList<IDisposable> _owned;

    // Tracks the in-flight RunAsync so disposal can stop it before tearing down
    // the components it is still using.
    private readonly object _runGate = new();
    private CancellationTokenSource? _runCts;
    private TaskCompletionSource? _runExited;
    private bool _disposed;

    // Serializes teardown so concurrent disposers observe the real outcome rather
    // than one returning success while another is still trying (and may time out).
    // Never disposed itself: its wait handle is unused, and keeping it alive lets
    // a timed-out disposal be retried safely.
    private readonly SemaphoreSlim _disposeGate = new(1, 1);
    private bool _released;

    private readonly Channel<string> _turns =
        Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });

    // Finalized sentences of the turn being assembled, keyed by the recognizer's
    // session-stable SentenceIndex. Keyed rather than concatenated because a
    // Correction revises the sentence already surfaced at that index; appending it
    // would leave the turn holding both the original and the correction.
    private readonly SortedDictionary<int, string> _sentences = [];

    // Highest sentence index already dispatched as a turn. A correction that
    // arrives after its turn has gone to the handler cannot be applied
    // retroactively, and must not be resurrected as a new turn either - that would
    // make the assistant answer a stray fragment.
    private int _dispatchedThrough = -1;
    private readonly object _gate = new();

    private volatile bool _speaking;
    private volatile CancellationTokenSource? _speakCts;

    /// <summary>
    /// Creates a conversation over streaming components the <em>caller</em> owns.
    /// Nothing passed here is disposed by the conversation, so a warm synthesizer
    /// or recognizer can be reused across several conversations. Use
    /// <see cref="SpeechPlatform.CreateConversation(VoiceInfo, TranscriptionModelInfo, IAudioSource, IAudioSink, ConversationTurnHandler, VoiceActivityOptions?, string?, string?, EmbeddedVoiceOptions?, EmbeddedTranscriberOptions?)"/>
    /// instead when you want the components created and disposed for you.
    /// </summary>
    public SpeechConversation(
        IAudioSource microphone,
        ISpeechRecognizer recognizer,
        ISpeechActivityDetector activityDetector,
        ISpeechSynthesizer synthesizer,
        IAudioSink speaker,
        ConversationTurnHandler turnHandler)
        : this(microphone, recognizer, activityDetector, synthesizer, speaker, turnHandler, owned: [])
    {
    }

    // Ownership-taking constructor used by the SpeechPlatform factories. Anything
    // in `owned` was created on the caller's behalf and is disposed with the
    // conversation; anything absent from it is borrowed and left alone.
    internal SpeechConversation(
        IAudioSource microphone,
        ISpeechRecognizer recognizer,
        ISpeechActivityDetector activityDetector,
        ISpeechSynthesizer synthesizer,
        IAudioSink speaker,
        ConversationTurnHandler turnHandler,
        IReadOnlyList<IDisposable> owned)
    {
        ArgumentNullException.ThrowIfNull(microphone);
        ArgumentNullException.ThrowIfNull(recognizer);
        ArgumentNullException.ThrowIfNull(activityDetector);
        ArgumentNullException.ThrowIfNull(synthesizer);
        ArgumentNullException.ThrowIfNull(speaker);
        ArgumentNullException.ThrowIfNull(turnHandler);
        ArgumentNullException.ThrowIfNull(owned);

        _microphone = microphone;
        _recognizer = recognizer;
        _activityDetector = activityDetector;
        _synthesizer = synthesizer;
        _speaker = speaker;
        _turnHandler = turnHandler;
        _owned = owned;
    }

    /// <summary>Raised with the recognized text each time a user turn completes.</summary>
    public event Action<string>? TurnRecognized;

    /// <summary>Raised when the user barges in and the assistant's speech is cut short.</summary>
    public event Action? BargedIn;

    /// <summary>Whether the assistant is currently speaking.</summary>
    public bool IsSpeaking => _speaking;

    /// <summary>
    /// Runs the loop until <paramref name="cancellationToken"/> is cancelled or the
    /// microphone source ends. Drives audio capture, recognition, turn dispatch, and
    /// spoken responses concurrently.
    /// </summary>
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        using var runCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = runCts.Token;

        var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_runGate)
        {
            // Re-checked under the lock so a Dispose racing with this call cannot
            // miss the run and tear the components down beneath it.
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_runCts is not null)
            {
                throw new InvalidOperationException(
                    "The conversation is already running. A second concurrent run would " +
                    "overwrite the first one's lifecycle tracking and let disposal release " +
                    "components while it is still active.");
            }
            _runCts = runCts;
            _runExited = exited;
        }

        ActiveLoop.Value = this;
        try
        {
            await RunCoreAsync(runCts, token, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ActiveLoop.Value = null;
            lock (_runGate)
            {
                _runCts = null;
                _runExited = null;
            }
            exited.TrySetResult();
        }
    }

    private async Task RunCoreAsync(
        CancellationTokenSource runCts,
        CancellationToken token,
        CancellationToken cancellationToken)
    {
        _activityDetector.SpeechStarted += OnSpeechStarted;
        _activityDetector.SpeechEnded += OnSpeechEnded;

        var reader = Task.Run(() => ReadRecognitionAsync(token), CancellationToken.None);
        var pump = Task.Run(() => PumpAsync(token), CancellationToken.None);

        // Coordinator: once capture has ended and every final has drained, flush
        // any trailing utterance as a closing turn (so speech that runs to the end
        // of the audio without a final silence is not lost) and complete the turn
        // stream. A worker fault instead cancels the run so the main loop stops
        // promptly, and is surfaced after cleanup rather than reported as success.
        Exception? workerFault = null;
        var closer = Task.Run(async () =>
        {
            workerFault = await DrainWorkerAsync(pump, token).ConfigureAwait(false)
                ?? await DrainWorkerAsync(reader, token).ConfigureAwait(false);
            if (workerFault is not null) runCts.Cancel();
            else FlushUtteranceAsTurn();
            _turns.Writer.TryComplete();
        }, CancellationToken.None);

        try
        {
            await foreach (var turn in _turns.Reader.ReadAllAsync(token).ConfigureAwait(false))
            {
                TurnRecognized?.Invoke(turn);

                var response = await _turnHandler(turn, token).ConfigureAwait(false);
                if (response is not null)
                {
                    await SpeakAsync(response, token).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Normal shutdown (the conversation was cancelled).
        }
        finally
        {
            runCts.Cancel();
            _activityDetector.SpeechStarted -= OnSpeechStarted;
            _activityDetector.SpeechEnded -= OnSpeechEnded;
            _turns.Writer.TryComplete();
            await SafeAwait(pump).ConfigureAwait(false);
            await SafeAwait(reader).ConfigureAwait(false);
            await SafeAwait(closer).ConfigureAwait(false);
        }

        // Surface a capture/recognition failure that ended the loop, unless the
        // caller asked to stop (in which case shutdown is the expected outcome).
        if (workerFault is not null && !cancellationToken.IsCancellationRequested)
        {
            throw new SpeechRecognitionException(
                "The conversation's audio capture or recognition pipeline failed.", workerFault);
        }
    }

    // Pump: microphone -> activity detector + recognizer, until the source ends or
    // the conversation is cancelled. Always finalizes the recognizer on the way
    // out so its trailing finals drain; the coordinator (not the pump) owns turn-
    // stream completion.
    private async Task PumpAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var buffer in _microphone.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                _activityDetector.Process(buffer);
                _recognizer.Write(buffer);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when the conversation stops.
        }
        finally
        {
            try { await _recognizer.CompleteAsync(CancellationToken.None).ConfigureAwait(false); }
            catch { /* draining best-effort */ }
        }
    }

    // Reader: accumulate finalized recognition text into the current utterance. The
    // activity detector's SpeechEnded (and, at end of audio, the coordinator)
    // snapshots and enqueues it as a turn.
    private async Task ReadRecognitionAsync(CancellationToken cancellationToken)
    {
        await foreach (var evt in _recognizer.ReadEventsAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!evt.IsFinal) continue;
            lock (_gate)
            {
                // Too late: the turn holding this sentence has already been
                // dispatched, so a revision of it can no longer change anything.
                if (evt.SentenceIndex <= _dispatchedThrough) continue;

                // Assignment, not append: a Final introduces the sentence at this
                // index and a Correction replaces it.
                _sentences[evt.SentenceIndex] = evt.Text;
            }
        }
    }

    private static async Task<Exception?> DrainWorkerAsync(Task worker, CancellationToken token)
    {
        try
        {
            await worker.ConfigureAwait(false);
            return null;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    private void OnSpeechStarted(object? sender, SpeechActivityEventArgs e)
    {
        // Barge-in: if the assistant is talking, cut the synthesis short.
        if (_speaking)
        {
            _speakCts?.Cancel();
            BargedIn?.Invoke();
        }
    }

    private void OnSpeechEnded(object? sender, SpeechActivityEventArgs e) => FlushUtteranceAsTurn();

    // Snapshots the accumulated recognition text and, if it holds anything,
    // enqueues it as one user turn. Called at each endpoint boundary and once more
    // when the audio ends so a final utterance without trailing silence is spoken.
    private void FlushUtteranceAsTurn()
    {
        string turn;
        lock (_gate)
        {
            // Sorted by sentence index, so the turn reads in the order it was
            // spoken regardless of when a correction arrived.
            turn = string.Join(' ', _sentences.Values);
            if (_sentences.Count > 0) _dispatchedThrough = _sentences.Keys.Max();
            _sentences.Clear();
        }

        if (!string.IsNullOrWhiteSpace(turn)) _turns.Writer.TryWrite(turn);
    }

    private async Task SpeakAsync(SpeechSynthesisRequest response, CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _speakCts = linked;
        _speaking = true;
        try
        {
            await _synthesizer.SynthesizeToSinkAsync(response, _speaker, onWord: null, linked.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Barge-in cancelled this response; swallow and listen again.
        }
        finally
        {
            _speaking = false;
            _speakCts = null;
        }
    }

    private static async Task SafeAwait(Task task)
    {
        try { await task.ConfigureAwait(false); }
        catch { /* faults surfaced by shutdown are ignored */ }
    }

    /// <summary>
    /// Stops a running loop and disposes the components this conversation created
    /// on the caller's behalf, in reverse creation order. Components supplied
    /// through the public constructor are borrowed and are never disposed here, so
    /// disposing a hand-wired conversation only stops the loop. Safe to call more
    /// than once.
    /// </summary>
    /// <remarks>
    /// Prefer <see cref="DisposeAsync"/>. This synchronous path has to block while
    /// the loop unwinds. If the loop does not stop within a few seconds, nothing is
    /// released and a <see cref="TimeoutException"/> is thrown, because tearing the
    /// native sessions out from under a live loop is worse than leaving them; stop
    /// the loop and dispose again.
    /// </remarks>
    /// <exception cref="InvalidOperationException">Called from inside the conversation's own loop.</exception>
    /// <exception cref="TimeoutException">The loop did not stop; nothing was released.</exception>
    public void Dispose()
    {
        ThrowIfInsideOwnLoop();

        // Serialized so a second caller cannot return "disposed" while the first
        // attempt is still running - and may yet time out and release nothing.
        _disposeGate.Wait();
        try
        {
            if (_released) return;
            var (exited, cancelFailure) = BeginStop();
            var stopped = exited is null || exited.Task.Wait(StopTimeout);
            FinishDispose(stopped, cancelFailure);
        }
        finally
        {
            _disposeGate.Release();
        }
    }

    /// <summary>
    /// Asynchronous teardown: cancels a running loop, waits for it to unwind, then
    /// disposes the components this conversation owns. Preferred over
    /// <see cref="Dispose"/> because draining the loop is inherently asynchronous.
    /// Safe to call more than once.
    /// </summary>
    /// <exception cref="InvalidOperationException">Called from inside this conversation's own loop.</exception>
    /// <exception cref="TimeoutException">The loop did not stop; nothing was released.</exception>
    public async ValueTask DisposeAsync()
    {
        ThrowIfInsideOwnLoop();

        await _disposeGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_released) return;

            var (exited, cancelFailure) = BeginStop();
            var stopped = true;
            if (exited is not null)
            {
                try { await exited.Task.WaitAsync(StopTimeout).ConfigureAwait(false); }
                catch (TimeoutException) { stopped = false; }
                catch { /* the loop's own fault surfaces through RunAsync */ }
            }

            FinishDispose(stopped, cancelFailure);
        }
        finally
        {
            _disposeGate.Release();
        }
    }

    private void ThrowIfInsideOwnLoop()
    {
        if (ReferenceEquals(ActiveLoop.Value, this))
        {
            throw new InvalidOperationException(
                "Cannot dispose a SpeechConversation from inside its own loop (for example " +
                "from a turn handler). The loop cannot finish until the callback returns, so " +
                "disposal would stall and then release components while it is still running. " +
                "Cancel the token passed to RunAsync instead, and dispose once it has completed.");
        }
    }

    // Marks the conversation stopping and cancels any in-flight run, reporting a
    // cancellation failure rather than throwing, so teardown always continues.
    private (TaskCompletionSource? Exited, Exception? CancelFailure) BeginStop()
    {
        CancellationTokenSource? cts;
        TaskCompletionSource? exited;
        lock (_runGate)
        {
            _disposed = true;
            cts = _runCts;
            exited = _runExited;
        }

        Exception? cancelFailure = null;
        try
        {
            cts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The run completed and disposed its own source between the lock and
            // here, which is benign.
        }
        catch (Exception ex)
        {
            // A registered cancellation callback threw. Keep tearing down rather
            // than abandoning the components, and surface it at the end.
            cancelFailure = ex;
        }

        return (exited, cancelFailure);
    }

    private void FinishDispose(bool stopped, Exception? cancelFailure)
    {
        if (!stopped)
        {
            // The loop is still live. Releasing now would pull native sessions out
            // from under it, so leave everything intact and let the caller retry
            // once the loop has actually stopped.
            lock (_runGate) _disposed = false;
            throw new TimeoutException(
                $"The conversation loop did not stop within {StopTimeout.TotalSeconds:0} seconds, " +
                "so its components were left intact. Cancel the token passed to RunAsync, await " +
                "it, then dispose again.");
        }

        try
        {
            ReleaseOwned();
        }
        catch (AggregateException ex) when (cancelFailure is not null)
        {
            throw new AggregateException(
                "The conversation failed to cancel and to dispose cleanly.",
                [cancelFailure, .. ex.InnerExceptions]);
        }

        if (cancelFailure is not null)
        {
            throw new AggregateException(
                "The conversation's components were released, but cancelling the running loop failed.",
                cancelFailure);
        }
    }

    private void ReleaseOwned()
    {
        List<Exception>? failures = null;
        for (var i = _owned.Count - 1; i >= 0; i--)
        {
            // Keep releasing the rest even if one component throws, so a single
            // bad disposal cannot leak the native resources behind the others.
            try { _owned[i].Dispose(); }
            catch (Exception ex) { (failures ??= []).Add(ex); }
        }

        // Recorded before any failure is surfaced, so a partially failed release is
        // never retried and cannot double-dispose the components that did succeed.
        _released = true;

        if (failures is not null)
        {
            throw new AggregateException(
                "One or more conversation components failed to dispose.", failures);
        }
    }
}
