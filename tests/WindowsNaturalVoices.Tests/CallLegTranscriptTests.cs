using WindowsNaturalVoices.Internal;

namespace WindowsNaturalVoices.Tests;

public class CallLegTranscriptTests
{
    private static readonly DateTimeOffset At = new(2026, 7, 20, 12, 0, 0, TimeSpan.Zero);

    private static TranscriptionSegment Seg(string text) =>
        new(text, TimeSpan.Zero, TimeSpan.Zero);

    [Fact]
    public void Append_MapsSentenceToSpeakerLabeledChunk()
    {
        var log = new CallLegTranscript("advisor", "Anna");

        var added = log.Append(new[] { Seg("Hello there.") }, At);

        var chunk = Assert.Single(added);
        Assert.Equal("Hello there.", chunk.Content);
        Assert.Equal("Anna", chunk.Speaker);
        Assert.Equal("advisor", chunk.SpeakerType);
        Assert.True(chunk.IsFinal);
        Assert.Equal(At, chunk.Timestamp);
    }

    [Fact]
    public void Append_TrimsWhitespaceAndSkipsBlankSentences()
    {
        var log = new CallLegTranscript("customer", "Mark");

        var added = log.Append(new[] { Seg("  Good morning.  "), Seg("   "), Seg("") }, At);

        var chunk = Assert.Single(added);
        Assert.Equal("Good morning.", chunk.Content);
    }

    [Fact]
    public void Append_ReturnsOnlyNewlyAddedChunksButAccumulatesAll()
    {
        var log = new CallLegTranscript("advisor", "Anna");

        var first = log.Append(new[] { Seg("One.") }, At);
        var second = log.Append(new[] { Seg("Two."), Seg("Three.") }, At);

        Assert.Equal(new[] { "One." }, first.Select(c => c.Content));
        Assert.Equal(new[] { "Two.", "Three." }, second.Select(c => c.Content));
        Assert.Equal(new[] { "One.", "Two.", "Three." }, log.Chunks.Select(c => c.Content));
    }

    [Fact]
    public void Append_EmptyInputReturnsEmptyAndAddsNothing()
    {
        var log = new CallLegTranscript("advisor", "Anna");
        log.Append(new[] { Seg("One.") }, At);

        var added = log.Append(Array.Empty<TranscriptionSegment>(), At);

        Assert.Empty(added);
        Assert.Single(log.Chunks);
    }

    [Fact]
    public void Append_AllBlankInputReturnsEmpty()
    {
        var log = new CallLegTranscript("advisor", "Anna");

        var added = log.Append(new[] { Seg("   "), Seg("") }, At);

        Assert.Empty(added);
        Assert.Empty(log.Chunks);
    }

    [Fact]
    public void Clear_DropsAllAccumulatedChunks()
    {
        var log = new CallLegTranscript("advisor", "Anna");
        log.Append(new[] { Seg("One."), Seg("Two.") }, At);

        log.Clear();

        Assert.Empty(log.Chunks);
    }

    [Fact]
    public void Append_StampsEachBatchWithItsOwnTimestamp()
    {
        var log = new CallLegTranscript("advisor", "Anna");
        var later = At.AddSeconds(5);

        log.Append(new[] { Seg("First.") }, At);
        log.Append(new[] { Seg("Second.") }, later);

        Assert.Equal(At, log.Chunks[0].Timestamp);
        Assert.Equal(later, log.Chunks[1].Timestamp);
    }
}
