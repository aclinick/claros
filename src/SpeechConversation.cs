using System.Runtime.Versioning;
using System.Text;
using System.Threading.Channels;

namespace WindowsNaturalVoices;

/// <summary>
/// Handles one recognized user turn and returns what the assistant should say, or
/// <see langword="null"/> to stay silent. Deliberately free of any language-model
/// coupling: the platform recognizes the utterance and speaks the response, and
/// this delegate is the only seam where a caller plugs in their own logic (an LLM,
/// a rules engine, an echo, …).
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
public sealed class SpeechConversation
{
    private readonly IAudioSource _microphone;
    private readonly ISpeechRecognizer _recognizer;
    private readonly ISpeechActivityDetector _activityDetector;
    private readonly ISpeechSynthesizer _synthesizer;
    private readonly IAudioSink _speaker;
    private readonly ConversationTurnHandler _turnHandler;

    private readonly Channel<string> _turns =
        Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
    private readonly StringBuilder _utterance = new();
    private readonly object _gate = new();

    private volatile bool _speaking;
    private volatile CancellationTokenSource? _speakCts;

    /// <summary>Creates a conversation over the supplied streaming components.</summary>
    public SpeechConversation(
        IAudioSource microphone,
        ISpeechRecognizer recognizer,
        ISpeechActivityDetector activityDetector,
        ISpeechSynthesizer synthesizer,
        IAudioSink speaker,
        ConversationTurnHandler turnHandler)
    {
        ArgumentNullException.ThrowIfNull(microphone);
        ArgumentNullException.ThrowIfNull(recognizer);
        ArgumentNullException.ThrowIfNull(activityDetector);
        ArgumentNullException.ThrowIfNull(synthesizer);
        ArgumentNullException.ThrowIfNull(speaker);
        ArgumentNullException.ThrowIfNull(turnHandler);

        _microphone = microphone;
        _recognizer = recognizer;
        _activityDetector = activityDetector;
        _synthesizer = synthesizer;
        _speaker = speaker;
        _turnHandler = turnHandler;
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
        using var runCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = runCts.Token;

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
                if (_utterance.Length > 0) _utterance.Append(' ');
                _utterance.Append(evt.Text);
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
            turn = _utterance.ToString();
            _utterance.Clear();
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
}
