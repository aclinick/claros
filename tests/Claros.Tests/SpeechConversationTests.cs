using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace Claros.Tests;

public class SpeechConversationTests
{
    private static readonly AudioFormat MicFormat = AudioFormat.Pcm16Mono16k;
    private static readonly AudioFormat SpeakerFormat = AudioFormat.Pcm16Mono24k;

    // Microphone that never yields but stays alive until the conversation stops, so
    // the pump keeps running while the test drives recognizer/VAD events directly.
    private sealed class IdleMic : IAudioSource
    {
        public AudioFormat Format => MicFormat;
        public async IAsyncEnumerable<AudioBuffer> ReadAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            try { await Task.Delay(Timeout.Infinite, cancellationToken); }
            catch (OperationCanceledException) { }
            yield break;
        }
    }

    private sealed class FakeRecognizer : ISpeechRecognizer
    {
        private readonly Channel<(RecognitionEvent Evt, TaskCompletionSource Consumed)> _events =
            Channel.CreateUnbounded<(RecognitionEvent, TaskCompletionSource)>();
        private int _index;

        public TranscriptionModelInfo Model { get; } =
            new("en-US", "fake", "pfn", "pfull", "path");
        public AudioFormat Format => MicFormat;
        public int Writes { get; private set; }

        public void Write(AudioBuffer audio) => Writes++;

        // Completes only after the conversation's reader has appended the final.
        public Task EmitFinalAsync(string text)
        {
            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _events.Writer.TryWrite((RecognitionEvent.Final(text, _index++), tcs));
            return tcs.Task;
        }

        // Revises a sentence already surfaced as final, at its original index.
        public Task EmitCorrectionAsync(string text, int sentenceIndex)
        {
            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _events.Writer.TryWrite((RecognitionEvent.Correction(text, sentenceIndex), tcs));
            return tcs.Task;
        }

        public Task CompleteAsync(CancellationToken cancellationToken = default)
        {
            _events.Writer.TryComplete();
            return Task.CompletedTask;
        }

        public async IAsyncEnumerable<RecognitionEvent> ReadEventsAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await foreach (var (evt, consumed) in _events.Reader.ReadAllAsync(cancellationToken))
            {
                yield return evt;
                consumed.TrySetResult(); // resumes here after the consumer processed evt
            }
        }

        public void Dispose() { }
    }

    private sealed class FakeVad : ISpeechActivityDetector
    {
        public AudioFormat Format => MicFormat;
        public bool IsSpeaking { get; private set; }
        public event EventHandler<SpeechActivityEventArgs>? SpeechStarted;
        public event EventHandler<SpeechActivityEventArgs>? SpeechEnded;
        public void Process(AudioBuffer audio) { }
        public void Reset() { }
        public void Dispose() { }

        public void RaiseStarted()
        {
            IsSpeaking = true;
            SpeechStarted?.Invoke(this, new SpeechActivityEventArgs(TimeSpan.Zero));
        }

        public void RaiseEnded()
        {
            IsSpeaking = false;
            SpeechEnded?.Invoke(this, new SpeechActivityEventArgs(TimeSpan.Zero));
        }
    }

    private sealed class FakeSynthesizer : ISpeechSynthesizer
    {
        private readonly bool _block;
        public FakeSynthesizer(bool block = false) => _block = block;

        public VoiceInfo Voice { get; } = new(
            "id", "Fake", "en-US", "Female", "Adult", "Test", "1", "pfn", "pfull", "path");
        public List<string> Spoken { get; } = [];
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Finished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool WasCancelled { get; private set; }
        public bool WasDisposed { get; private set; }

        public Task<WaveformResult> SynthesizeAsync(
            SpeechSynthesisRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public async Task SynthesizeToSinkAsync(
            SpeechSynthesisRequest request, IAudioSink sink,
            Action<SpokenWord>? onWord = null, CancellationToken cancellationToken = default)
        {
            Spoken.Add(request.Content);
            await sink.WriteAsync(AudioBuffer.FromSamples(new float[240], sink.Format), cancellationToken);
            Started.TrySetResult();
            try
            {
                if (_block) await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                WasCancelled = true;
                throw;
            }
            finally
            {
                Finished.TrySetResult();
            }
        }

        public void Dispose() { WasDisposed = true; }
    }

    private sealed class CollectingSink : IAudioSink
    {
        public AudioFormat Format => SpeakerFormat;
        public int Writes { get; private set; }
        public ValueTask WriteAsync(AudioBuffer buffer, CancellationToken cancellationToken = default)
        {
            Writes++;
            return ValueTask.CompletedTask;
        }
        public ValueTask CompleteAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    [Fact]
    public async Task Turn_RecognizedText_FlowsToHandlerAndResponseIsSpoken()
    {
        var mic = new IdleMic();
        var recog = new FakeRecognizer();
        var vad = new FakeVad();
        var synth = new FakeSynthesizer();
        var sink = new CollectingSink();
        var handled = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        ConversationTurnHandler handler = (utterance, _) =>
        {
            handled.TrySetResult(utterance);
            return Task.FromResult<SpeechSynthesisRequest?>("You said " + utterance);
        };

        using var cts = new CancellationTokenSource();
        var convo = new SpeechConversation(mic, recog, vad, synth, sink, handler);
        var run = convo.RunAsync(cts.Token);

        await recog.EmitFinalAsync("hello there");
        vad.RaiseEnded();

        var got = await handled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await synth.Finished.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("hello there", got);
        Assert.Equal(["You said hello there"], synth.Spoken);
        Assert.Equal(1, sink.Writes);

        await cts.CancelAsync();
        await run;
    }

    [Fact]
    public async Task Handler_ReturningNull_SpeaksNothing()
    {
        var recog = new FakeRecognizer();
        var vad = new FakeVad();
        var synth = new FakeSynthesizer();
        var handled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        ConversationTurnHandler handler = (_, _) =>
        {
            handled.TrySetResult();
            return Task.FromResult<SpeechSynthesisRequest?>(null);
        };

        using var cts = new CancellationTokenSource();
        var convo = new SpeechConversation(new IdleMic(), recog, vad, synth, new CollectingSink(), handler);
        var run = convo.RunAsync(cts.Token);

        await recog.EmitFinalAsync("ignore me");
        vad.RaiseEnded();
        await handled.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Empty(synth.Spoken);

        await cts.CancelAsync();
        await run;
    }

    [Fact]
    public async Task MultipleFinals_AreCombinedIntoOneTurn()
    {
        var recog = new FakeRecognizer();
        var vad = new FakeVad();
        var handled = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        ConversationTurnHandler handler = (utterance, _) =>
        {
            handled.TrySetResult(utterance);
            return Task.FromResult<SpeechSynthesisRequest?>(null);
        };

        using var cts = new CancellationTokenSource();
        var convo = new SpeechConversation(
            new IdleMic(), recog, vad, new FakeSynthesizer(), new CollectingSink(), handler);
        var run = convo.RunAsync(cts.Token);

        await recog.EmitFinalAsync("First sentence.");
        await recog.EmitFinalAsync("Second sentence.");
        vad.RaiseEnded();

        var got = await handled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("First sentence. Second sentence.", got);

        await cts.CancelAsync();
        await run;
    }

    [Fact]
    public async Task SpeechEndedWithNoRecognizedText_DoesNotDispatchTurn()
    {
        var recog = new FakeRecognizer();
        var vad = new FakeVad();
        var handlerCalls = 0;

        ConversationTurnHandler handler = (_, _) =>
        {
            Interlocked.Increment(ref handlerCalls);
            return Task.FromResult<SpeechSynthesisRequest?>(null);
        };

        using var cts = new CancellationTokenSource();
        var convo = new SpeechConversation(
            new IdleMic(), recog, vad, new FakeSynthesizer(), new CollectingSink(), handler);
        var run = convo.RunAsync(cts.Token);

        vad.RaiseEnded();          // no finals accumulated
        await Task.Delay(100);

        Assert.Equal(0, handlerCalls);

        await cts.CancelAsync();
        await run;
    }

    [Fact]
    public async Task BargeIn_DuringResponse_CancelsSynthesis()
    {
        var recog = new FakeRecognizer();
        var vad = new FakeVad();
        var synth = new FakeSynthesizer(block: true); // response synthesis blocks until cancelled
        var barged = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        ConversationTurnHandler handler = (_, _) =>
            Task.FromResult<SpeechSynthesisRequest?>("a long spoken response");

        using var cts = new CancellationTokenSource();
        var convo = new SpeechConversation(new IdleMic(), recog, vad, synth, new CollectingSink(), handler);
        convo.BargedIn += () => barged.TrySetResult();
        var run = convo.RunAsync(cts.Token);

        await recog.EmitFinalAsync("start talking");
        vad.RaiseEnded();

        await synth.Started.Task.WaitAsync(TimeSpan.FromSeconds(5)); // assistant is speaking
        Assert.True(convo.IsSpeaking);

        vad.RaiseStarted(); // user barges in

        await barged.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await synth.Finished.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(synth.WasCancelled);

        // Finished is signalled from inside the synthesizer's own finally block, so
        // it fires while the cancellation is still unwinding — before the
        // conversation's SpeakAsync finally has cleared the speaking flag. Barge-in
        // cancellation is cooperative and therefore asynchronous, so observe the
        // transition instead of assuming it is already visible.
        await WaitUntilAsync(() => !convo.IsSpeaking, TimeSpan.FromSeconds(5));

        await cts.CancelAsync();
        await run;
    }

    // Polls until <paramref name="condition"/> holds, so tests observe an
    // asynchronous state transition rather than racing it.
    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return;
            await Task.Delay(10);
        }

        Assert.True(condition(), "Condition was still not satisfied after " + timeout + ".");
    }

    [Fact]
    public async Task EndOfAudio_FlushesTrailingUtterance_WithoutSpeechEnded()
    {
        // A finite mic that ends after a couple buffers, with a final recognized
        // but no VAD SpeechEnded — the loop must still dispatch the trailing turn.
        var recog = new FakeRecognizer();
        var vad = new FakeVad();
        var handled = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        ConversationTurnHandler handler = (utterance, _) =>
        {
            handled.TrySetResult(utterance);
            return Task.FromResult<SpeechSynthesisRequest?>(null);
        };

        var convo = new SpeechConversation(
            new FiniteMic(recog), recog, vad, new FakeSynthesizer(), new CollectingSink(), handler);
        var run = convo.RunAsync();

        var got = await handled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("closing words", got);

        await run; // completes naturally when the mic is exhausted
    }

    // Yields a few buffers, pushes a final into the recognizer as it "hears" audio,
    // then ends — with no trailing SpeechEnded event.
    private sealed class FiniteMic : IAudioSource
    {
        private readonly FakeRecognizer _recognizer;
        public FiniteMic(FakeRecognizer recognizer) => _recognizer = recognizer;
        public AudioFormat Format => MicFormat;

        public async IAsyncEnumerable<AudioBuffer> ReadAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            for (var i = 0; i < 3; i++)
            {
                yield return AudioBuffer.FromSamples(new float[160], MicFormat);
                await Task.Yield();
            }
            await _recognizer.EmitFinalAsync("closing words");
        }
    }

    private sealed class TrackingDisposable : IDisposable
    {
        private readonly List<string>? _order;
        private readonly string _name;

        public TrackingDisposable(string name = "d", List<string>? order = null)
        {
            _name = name;
            _order = order;
        }

        public int DisposeCount { get; private set; }

        public void Dispose()
        {
            DisposeCount++;
            _order?.Add(_name);
        }
    }

    private sealed class ThrowingDisposable : IDisposable
    {
        public void Dispose() => throw new InvalidOperationException("boom");
    }

    private static SpeechConversation Hand(ISpeechSynthesizer synth) => new(
        new IdleMic(), new FakeRecognizer(), new FakeVad(), synth,
        new CollectingSink(), (_, _) => Task.FromResult<SpeechSynthesisRequest?>(null));

    [Fact]
    public async Task Correction_ReplacesTheSentenceItRevises_RatherThanAppending()
    {
        // A Correction is IsFinal, so a consumer that only checks IsFinal appends it
        // and the turn ends up holding both the original and its revision.
        var recog = new FakeRecognizer();
        var vad = new FakeVad();
        using var synth = new FakeSynthesizer();
        var turns = new List<string>();

        var convo = new SpeechConversation(
            new IdleMic(), recog, vad, synth, new CollectingSink(),
            (_, _) => Task.FromResult<SpeechSynthesisRequest?>(null));
        convo.TurnRecognized += t => turns.Add(t);
        var run = convo.RunAsync();

        await recog.EmitFinalAsync("i have twenty");     // sentence 0
        await recog.EmitFinalAsync("dollars left");      // sentence 1
        await recog.EmitCorrectionAsync("I have $20", 0); // revises sentence 0
        vad.RaiseEnded();

        await WaitUntilAsync(() => turns.Count == 1, TimeSpan.FromSeconds(5));

        Assert.Equal("I have $20 dollars left", turns[0]);
        Assert.DoesNotContain("i have twenty", turns[0], StringComparison.Ordinal);

        await convo.DisposeAsync();
        await run;
    }

    [Fact]
    public async Task Correction_KeepsSpokenOrderRegardlessOfArrivalOrder()
    {
        var recog = new FakeRecognizer();
        var vad = new FakeVad();
        using var synth = new FakeSynthesizer();
        var turns = new List<string>();

        var convo = new SpeechConversation(
            new IdleMic(), recog, vad, synth, new CollectingSink(),
            (_, _) => Task.FromResult<SpeechSynthesisRequest?>(null));
        convo.TurnRecognized += t => turns.Add(t);
        var run = convo.RunAsync();

        await recog.EmitFinalAsync("one");
        await recog.EmitFinalAsync("two");
        await recog.EmitFinalAsync("three");
        // A late revision of the FIRST sentence must not move it to the end.
        await recog.EmitCorrectionAsync("ONE", 0);
        vad.RaiseEnded();

        await WaitUntilAsync(() => turns.Count == 1, TimeSpan.FromSeconds(5));

        Assert.Equal("ONE two three", turns[0]);

        await convo.DisposeAsync();
        await run;
    }

    [Fact]
    public async Task Correction_ArrivingAfterItsTurnWasDispatched_IsDropped()
    {
        // The turn already went to the handler, so the revision cannot be applied.
        // It must not resurface as a new turn either - the assistant would then be
        // answering a stray fragment of a sentence it already handled.
        var recog = new FakeRecognizer();
        var vad = new FakeVad();
        using var synth = new FakeSynthesizer();
        var turns = new List<string>();

        var convo = new SpeechConversation(
            new IdleMic(), recog, vad, synth, new CollectingSink(),
            (_, _) => Task.FromResult<SpeechSynthesisRequest?>(null));
        convo.TurnRecognized += t => turns.Add(t);
        var run = convo.RunAsync();

        await recog.EmitFinalAsync("book a table");
        vad.RaiseEnded();
        await WaitUntilAsync(() => turns.Count == 1, TimeSpan.FromSeconds(5));

        await recog.EmitCorrectionAsync("book a cable", 0); // too late
        vad.RaiseEnded();
        await Task.Delay(150);

        Assert.Single(turns);
        Assert.Equal("book a table", turns[0]);

        await convo.DisposeAsync();
        await run;
    }

    [Fact]
    public async Task DisposeAsync_StopsARunningLoopBeforeReleasingComponents()
    {
        // The components must not be torn out from under a live loop, so teardown
        // has to cancel and drain RunAsync first.
        var order = new List<string>();
        var component = new TrackingDisposable("component", order);
        using var synth = new FakeSynthesizer();
        var recog = new FakeRecognizer();

        var convo = new SpeechConversation(
            new IdleMic(), recog, new FakeVad(), synth,
            new CollectingSink(), (_, _) => Task.FromResult<SpeechSynthesisRequest?>(null),
            owned: [component]);

        var run = convo.RunAsync();
        await Task.Delay(50);
        Assert.Equal(0, component.DisposeCount);

        await convo.DisposeAsync();

        // The loop is finished by the time the component was released.
        Assert.True(run.IsCompleted);
        Assert.Equal(1, component.DisposeCount);
        await run;
    }

    [Fact]
    public async Task RunAsync_AfterDisposal_Throws()
    {
        using var synth = new FakeSynthesizer();
        var convo = Hand(synth);

        await convo.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => convo.RunAsync());
    }

    [Fact]
    public async Task RunAsync_WhileAlreadyRunning_IsRejected()
    {
        // A second run would overwrite the first one's lifecycle tracking, letting
        // disposal release components while the first is still active.
        using var synth = new FakeSynthesizer();
        var convo = Hand(synth);

        var run = convo.RunAsync();
        await Task.Delay(50);

        await Assert.ThrowsAsync<InvalidOperationException>(() => convo.RunAsync());

        await convo.DisposeAsync();
        await run;
    }

    [Fact]
    public async Task DisposeAsync_FromInsideTheTurnHandler_IsRejected()
    {
        // The loop cannot finish until the handler returns, so disposing here would
        // stall and then release components out from under the running loop.
        using var synth = new FakeSynthesizer();
        var recog = new FakeRecognizer();
        var vad = new FakeVad();
        SpeechConversation? convo = null;
        var caught = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);

        ConversationTurnHandler handler = async (_, _) =>
        {
            try
            {
                await convo!.DisposeAsync();
                caught.TrySetResult(null);
            }
            catch (Exception ex)
            {
                caught.TrySetResult(ex);
            }
            return null;
        };

        convo = new SpeechConversation(
            new IdleMic(), recog, vad, synth, new CollectingSink(), handler);
        var run = convo.RunAsync();

        await recog.EmitFinalAsync("hello");
        vad.RaiseEnded();

        var ex = await caught.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.IsType<InvalidOperationException>(ex);

        await convo.DisposeAsync();
        await run;
    }

    [Fact]
    public async Task DisposeAsync_FromAnotherConversationsHandler_IsAllowed()
    {
        // The reentrancy guard must identify WHICH conversation owns the loop, so
        // a handler in one conversation can still dispose an unrelated one.
        using var synthA = new FakeSynthesizer();
        using var synthB = new FakeSynthesizer();
        var recogA = new FakeRecognizer();
        var vadA = new FakeVad();

        var componentB = new TrackingDisposable();
        var convoB = new SpeechConversation(
            new IdleMic(), new FakeRecognizer(), new FakeVad(), synthB,
            new CollectingSink(), (_, _) => Task.FromResult<SpeechSynthesisRequest?>(null),
            owned: [componentB]);

        var done = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
        ConversationTurnHandler handler = async (_, _) =>
        {
            try { await convoB.DisposeAsync(); done.TrySetResult(null); }
            catch (Exception ex) { done.TrySetResult(ex); }
            return null;
        };

        var convoA = new SpeechConversation(
            new IdleMic(), recogA, vadA, synthA, new CollectingSink(), handler);
        var run = convoA.RunAsync();

        await recogA.EmitFinalAsync("hello");
        vadA.RaiseEnded();

        Assert.Null(await done.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, componentB.DisposeCount);

        await convoA.DisposeAsync();
        await run;
    }

    [Fact]
    public void Dispose_OnAHandWiredConversation_DisposesNothing()
    {
        // The public constructor borrows: a warm synthesizer must survive so it can
        // be reused across conversations.
        using var synth = new FakeSynthesizer();
        var convo = Hand(synth);

        convo.Dispose();
        convo.Dispose();

        Assert.False(synth.WasDisposed);
    }

    [Fact]
    public void Dispose_ReleasesOwnedComponentsOnceInReverseOrder()
    {
        var order = new List<string>();
        var first = new TrackingDisposable("first", order);
        var second = new TrackingDisposable("second", order);
        using var synth = new FakeSynthesizer();

        var convo = new SpeechConversation(
            new IdleMic(), new FakeRecognizer(), new FakeVad(), synth,
            new CollectingSink(), (_, _) => Task.FromResult<SpeechSynthesisRequest?>(null),
            owned: [first, second]);

        convo.Dispose();
        convo.Dispose();

        Assert.Equal(1, first.DisposeCount);
        Assert.Equal(1, second.DisposeCount);
        Assert.Equal(["second", "first"], order);
    }

    [Fact]
    public void Dispose_KeepsReleasingAfterAComponentThrows()
    {
        // One bad component must not strand the native resources behind the others.
        var survivor = new TrackingDisposable();
        using var synth = new FakeSynthesizer();

        var convo = new SpeechConversation(
            new IdleMic(), new FakeRecognizer(), new FakeVad(), synth,
            new CollectingSink(), (_, _) => Task.FromResult<SpeechSynthesisRequest?>(null),
            owned: [survivor, new ThrowingDisposable()]);

        var ex = Assert.Throws<AggregateException>(convo.Dispose);

        Assert.Single(ex.InnerExceptions);
        Assert.Equal(1, survivor.DisposeCount);
    }
}
