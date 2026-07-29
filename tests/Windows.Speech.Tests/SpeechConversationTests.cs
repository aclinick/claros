using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace Windows.Speech.Tests;

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

        public void Dispose() { }
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
}
