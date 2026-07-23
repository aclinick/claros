using WindowsNaturalVoices.Internal;

namespace WindowsNaturalVoices.Tests;

public class NarrationSchedulerTests
{
    private static readonly TimeSpan Lead = TimeSpan.FromMilliseconds(350);
    private static readonly TimeSpan Stale = TimeSpan.FromSeconds(1);

    private static TimedCue Cue(int i, double start, double end, string text = "x") =>
        new(i, TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(end), text);

    private static TimeSpan Sec(double s) => TimeSpan.FromSeconds(s);

    [Fact]
    public void TakeDue_ReturnsNull_WhenFrontierCueNotYetDue()
    {
        var s = new NarrationScheduler([Cue(1, 10, 11)]);

        Assert.Null(s.TakeDue(Sec(5), Lead, Stale));
        Assert.False(s.IsExhausted); // cue still pending
    }

    [Fact]
    public void TakeDue_ReturnsCue_OncePlayheadReachesStartMinusLead()
    {
        var s = new NarrationScheduler([Cue(1, 10, 11)]);

        // 350ms lead => due at 9.65s.
        Assert.Null(s.TakeDue(Sec(9.6), Lead, Stale));
        var cue = s.TakeDue(Sec(9.7), Lead, Stale);

        Assert.NotNull(cue);
        Assert.Equal(1, cue!.Index);
    }

    [Fact]
    public void TakeDue_ConsumesCue_SoNextCallAdvances()
    {
        var s = new NarrationScheduler([Cue(1, 0, 1), Cue(2, 2, 3)]);

        Assert.Equal(1, s.TakeDue(Sec(0), Lead, Stale)!.Index);
        Assert.Null(s.TakeDue(Sec(0), Lead, Stale));       // cue 2 not due yet
        Assert.Equal(2, s.TakeDue(Sec(2), Lead, Stale)!.Index);
        Assert.True(s.IsExhausted);
    }

    [Fact]
    public void TakeDue_SkipsStaleCue_AfterForwardSeek()
    {
        var s = new NarrationScheduler([Cue(1, 0, 1), Cue(2, 10, 11)]);

        // Playhead jumped to 10s: cue 1 ended at 1s, now 9s past (> 1s grace) => skip,
        // and cue 2 is due, so it is returned in the same call.
        var cue = s.TakeDue(Sec(10), Lead, Stale);

        Assert.Equal(2, cue!.Index);
    }

    [Fact]
    public void TakeDue_SpeaksCueStillWithinStaleGrace()
    {
        var s = new NarrationScheduler([Cue(1, 0, 1)]);

        // Playhead 1.5s: 0.5s past the 1s end, within 1s grace => still spoken.
        var cue = s.TakeDue(Sec(1.5), Lead, Stale);

        Assert.Equal(1, cue!.Index);
    }

    [Fact]
    public void TakeDue_SkipsAllStaleCues_ReturnsNullWhenNoneDue()
    {
        var s = new NarrationScheduler([Cue(1, 0, 1), Cue(2, 1, 2)]);

        // Playhead at 100s: both cues long stale, nothing to speak.
        Assert.Null(s.TakeDue(Sec(100), Lead, Stale));
        Assert.True(s.IsExhausted);
    }

    [Fact]
    public void IsExhausted_TrueForEmptyCueList()
    {
        Assert.True(new NarrationScheduler([]).IsExhausted);
    }

    [Fact]
    public void TakeDue_ZeroLengthCue_HonorsStaleGraceFromStart()
    {
        var s = new NarrationScheduler([TimedCue.At(Sec(5), "ping")]);

        // Point event at 5s; within grace at 5.5s.
        Assert.Equal(0, s.TakeDue(Sec(5.5), Lead, Stale)!.Index);
    }
}
