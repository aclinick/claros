namespace Claros.Tests;

public class CueSentenceGrouperTests
{
    private static TimedCue Cue(int i, double start, double end, string text) =>
        new(i, TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(end), text);

    [Fact]
    public void GroupIntoSentences_MergesFragmentsUntilSentenceEnd()
    {
        var cues = new[]
        {
            Cue(1, 0, 1, "The quick brown"),
            Cue(2, 1, 2, "fox jumps."),
            Cue(3, 2, 3, "Next sentence."),
        };

        var groups = CueSentenceGrouper.GroupIntoSentences(cues);

        Assert.Equal(2, groups.Count);
        Assert.Equal("The quick brown fox jumps.", groups[0].Text);
        Assert.Equal(1, groups[0].Index);                        // first cue's index
        Assert.Equal(TimeSpan.Zero, groups[0].Start);            // first cue's start
        Assert.Equal(TimeSpan.FromSeconds(2), groups[0].End);    // last merged cue's end
        Assert.Equal("Next sentence.", groups[1].Text);
    }

    [Fact]
    public void GroupIntoSentences_BreaksOnLongGap()
    {
        var cues = new[]
        {
            Cue(1, 0, 1, "Unfinished thought"),
            Cue(2, 5, 6, "resumes later"), // 4s gap > default 1.2s
        };

        var groups = CueSentenceGrouper.GroupIntoSentences(cues);

        Assert.Equal(2, groups.Count);
        Assert.Equal("Unfinished thought", groups[0].Text);
        Assert.Equal("resumes later", groups[1].Text);
    }

    [Fact]
    public void GroupIntoSentences_LooksPastTrailingQuotesForSentenceEnd()
    {
        var cues = new[]
        {
            Cue(1, 0, 1, "She said \"hello.\""),
            Cue(2, 1, 2, "Done."),
        };

        var groups = CueSentenceGrouper.GroupIntoSentences(cues);

        Assert.Equal(2, groups.Count);
        Assert.Equal("She said \"hello.\"", groups[0].Text);
    }

    [Fact]
    public void GroupIntoSentences_FlushesTrailingFragmentAtEnd()
    {
        var cues = new[] { Cue(1, 0, 1, "no terminator here") };

        var group = Assert.Single(CueSentenceGrouper.GroupIntoSentences(cues));

        Assert.Equal("no terminator here", group.Text);
    }

    [Fact]
    public void GroupIntoSentences_RespectsCustomMaxGap()
    {
        var cues = new[]
        {
            Cue(1, 0, 1, "part one"),
            Cue(2, 3, 4, "part two"), // 2s gap
        };

        // With a 5s gap threshold the 2s gap does NOT break; both merge.
        var group = Assert.Single(
            CueSentenceGrouper.GroupIntoSentences(cues, TimeSpan.FromSeconds(5)));

        Assert.Equal("part one part two", group.Text);
    }

    [Fact]
    public void GroupIntoSentences_Empty_ReturnsEmpty()
    {
        Assert.Empty(CueSentenceGrouper.GroupIntoSentences([]));
    }
}
