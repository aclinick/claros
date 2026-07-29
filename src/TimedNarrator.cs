using System.Runtime.Versioning;
using Claros.Internal;

namespace Claros;

/// <summary>
/// Turns <see cref="TimedCue"/>s into speech aligned to a timeline, on top of any
/// <see cref="ISpeechSynthesizer"/>. It has two modes:
/// <list type="bullet">
/// <item><description>
/// <see cref="RenderAsync"/> — offline: synthesize every cue and mix it onto one
/// silent track at each cue's start time, producing a single voiceover
/// <see cref="WaveformResult"/> you can drop back onto a video or clip.
/// </description></item>
/// <item><description>
/// <see cref="NarrateAsync"/> / <see cref="NarrateStreamAsync"/> — live: speak
/// each cue as a caller-supplied monotonic <c>playhead</c> reaches it (minus a
/// lead), one utterance at a time, skipping cues the playhead has already passed.
/// The playhead can be a media position (subtitle voiceover) or any app/web
/// content position (captions or a stock-ticker feed synced to content).
/// </description></item>
/// </list>
/// The narrator <em>borrows</em> a synthesizer passed to its constructor — it
/// neither owns nor disposes it, so one warm synthesizer can be reused across
/// renders. A narrator obtained from <see cref="SpeechPlatform.CreateNarrator"/>
/// instead owns the speaker created for it and releases it on
/// <see cref="Dispose"/>. Like the synthesizer, it is thread-hostile: serialize
/// calls.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class TimedNarrator : IDisposable
{
    private const int PollMilliseconds = 50;

    private readonly ISpeechSynthesizer _synthesizer;
    // Non-null only when this narrator created the synthesizer and must release
    // it; a borrowed synthesizer leaves this null and is never disposed here.
    private readonly IDisposable? _owned;
    private bool _disposed;

    /// <summary>
    /// Creates a narrator that speaks through <paramref name="synthesizer"/>,
    /// which the caller continues to own and dispose.
    /// </summary>
    public TimedNarrator(ISpeechSynthesizer synthesizer)
        : this(synthesizer, owned: null)
    {
    }

    internal TimedNarrator(ISpeechSynthesizer synthesizer, IDisposable? owned)
    {
        ArgumentNullException.ThrowIfNull(synthesizer);
        _synthesizer = synthesizer;
        _owned = owned;
    }

    /// <summary>
    /// Releases the synthesizer this narrator created, if any. A narrator built
    /// over a borrowed synthesizer does nothing here. Safe to call more than once.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _owned?.Dispose();
    }

    /// <summary>The voice the narrator speaks with (the borrowed synthesizer's voice).</summary>
    public VoiceInfo Voice => _synthesizer.Voice;

    /// <summary>
    /// Synthesizes every cue and mixes it onto one silent timeline at the cue's
    /// start time, returning the complete voiceover track. Overlapping clips are
    /// additively mixed; the track runs at least until the last cue's end. Returns
    /// an empty waveform when there is nothing to speak.
    /// </summary>
    public async Task<WaveformResult> RenderAsync(
        IReadOnlyList<TimedCue> cues,
        TimedNarrationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cues);
        options ??= TimedNarrationOptions.Default;

        var utterances = options.GroupIntoSentences
            ? CueSentenceGrouper.GroupIntoSentences(cues, options.MaxGap)
            : cues;

        var placements = new List<NarrationTimeline.Placement>(utterances.Count);
        // The timeline is built at the rate the engine declares, so offsets are
        // known before a single clip is synthesized.
        var sampleRate = _synthesizer.OutputFormat.SampleRate;
        var lastEnd = TimeSpan.Zero;

        foreach (var cue in utterances)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (cue.End > lastEnd) lastEnd = cue.End;
            if (string.IsNullOrWhiteSpace(cue.Text)) continue;

            var wave = await _synthesizer.SynthesizeAsync(cue.Text, cancellationToken)
                .ConfigureAwait(false);

            if (wave.SampleRate != sampleRate)
            {
                // Placement offsets and the final track are computed in samples, so
                // every clip must share the one rate the engine promised through
                // OutputFormat. Reaching here means the engine broke that promise.
                throw new SpeechSynthesisException(
                    $"Voice '{_synthesizer.Voice.DisplayName}' declares {sampleRate} Hz output " +
                    $"but returned {wave.SampleRate} Hz, so its clips cannot be mixed onto one " +
                    "timeline.");
            }

            var samples = (float[])wave.Samples.Clone(); // don't mutate the engine's buffer
            if (options.FadeEdges) NarrationTimeline.ApplyEdgeFade(samples, sampleRate);

            var startSample = NarrationTimeline.ToSample(cue.Start, sampleRate);
            placements.Add(new NarrationTimeline.Placement(startSample, samples));
        }

        var minLength = NarrationTimeline.ToSample(lastEnd, sampleRate);
        var timeline = NarrationTimeline.Mix(placements, minLength);
        return new WaveformResult(timeline, sampleRate);
    }

    /// <summary>
    /// Speaks <paramref name="cues"/> live into <paramref name="sink"/>, using
    /// <paramref name="playhead"/> as the master clock: each cue is spoken once the
    /// playhead reaches its start minus <see cref="TimedNarrationOptions.Lead"/>,
    /// one utterance at a time. Cues the playhead has already passed (beyond
    /// <see cref="TimedNarrationOptions.StaleGrace"/>) are skipped, so after a
    /// forward seek narration catches up instead of running behind. The in-flight
    /// utterance is never interrupted. Returns when every cue has been spoken or
    /// skipped. The sink is not completed — the caller owns its lifetime.
    /// </summary>
    public async Task NarrateAsync(
        IReadOnlyList<TimedCue> cues,
        Func<TimeSpan> playhead,
        IAudioSink sink,
        TimedNarrationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cues);
        ArgumentNullException.ThrowIfNull(playhead);
        ArgumentNullException.ThrowIfNull(sink);
        options ??= TimedNarrationOptions.Default;

        var utterances = options.GroupIntoSentences
            ? CueSentenceGrouper.GroupIntoSentences(cues, options.MaxGap)
            : cues;

        var scheduler = new NarrationScheduler(utterances);
        while (!scheduler.IsExhausted)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var cue = scheduler.TakeDue(playhead(), options.Lead, options.StaleGrace);
            if (cue is null)
            {
                await Task.Delay(PollMilliseconds, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (string.IsNullOrWhiteSpace(cue.Text)) continue;
            await _synthesizer.SynthesizeToSinkAsync(cue.Text, sink, onWord: null, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Speaks cues arriving from a live <paramref name="cues"/> stream (for example
    /// a stock-ticker feed) into <paramref name="sink"/>, serialized one utterance
    /// at a time. Each cue waits until <paramref name="playhead"/> reaches its start
    /// minus <see cref="TimedNarrationOptions.Lead"/>, then is spoken — unless the
    /// playhead has already passed it beyond <see cref="TimedNarrationOptions.StaleGrace"/>,
    /// in which case it is dropped so the narration stays current. Give cues a
    /// <see cref="TimedCue.Start"/> of zero to speak them the moment they arrive.
    /// Streamed cues are spoken as-is (no sentence grouping). The sink is not
    /// completed — the caller owns its lifetime.
    /// </summary>
    public async Task NarrateStreamAsync(
        IAsyncEnumerable<TimedCue> cues,
        Func<TimeSpan> playhead,
        IAudioSink sink,
        TimedNarrationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cues);
        ArgumentNullException.ThrowIfNull(playhead);
        ArgumentNullException.ThrowIfNull(sink);
        options ??= TimedNarrationOptions.Default;

        await foreach (var cue in cues.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (string.IsNullOrWhiteSpace(cue.Text)) continue;

            // A cue whose start is still ahead of the playhead is a scheduled cue:
            // wait for it, and if we blew past it (because an earlier utterance kept
            // us busy) drop it as stale so the narration stays current. A cue already
            // due on arrival — including a zero-start "speak now" ticker event — is
            // spoken immediately and never treated as stale.
            if (cue.Start - options.Lead > playhead())
            {
                while (playhead() < cue.Start - options.Lead)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await Task.Delay(PollMilliseconds, cancellationToken).ConfigureAwait(false);
                }

                if (playhead() > cue.End + options.StaleGrace) continue; // missed while busy
            }

            await _synthesizer.SynthesizeToSinkAsync(cue.Text, sink, onWord: null, cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
