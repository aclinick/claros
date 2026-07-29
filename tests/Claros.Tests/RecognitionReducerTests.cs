using Claros.Internal;

namespace Claros.Tests;

public class RecognitionReducerTests
{
    private static IReadOnlyList<RecognitionEvent> Finals(IEnumerable<RecognitionEvent> events) =>
        events.Where(e => e.IsFinal).ToArray();

    [Fact]
    public void Observe_EmitsTrailingHypothesisAsPartialWithNegativeIndex()
    {
        var reducer = new RecognitionReducer();

        var events = reducer.Observe("I am still talk");

        var evt = Assert.Single(events);
        Assert.Equal(RecognitionEventKind.Partial, evt.Kind);
        Assert.Equal("I am still talk", evt.Text);
        Assert.Equal(-1, evt.SentenceIndex);
        Assert.False(evt.IsFinal);
    }

    [Fact]
    public void Observe_WithholdsTrailingSentenceUntilANewOneBegins()
    {
        var reducer = new RecognitionReducer();

        // A single terminated sentence with nothing after it is still the trailing
        // sentence, so it is only a partial, not a final.
        var first = reducer.Observe("Hello world.");
        var partial = Assert.Single(first);
        Assert.Equal(RecognitionEventKind.Partial, partial.Kind);
        Assert.Equal("Hello world.", partial.Text);

        // Once a later sentence begins, the first sentence's boundary is trusted.
        var next = reducer.Observe("Hello world. I am talk");
        Assert.Contains(next, e => e.Kind == RecognitionEventKind.Final
                                   && e.Text == "Hello world." && e.SentenceIndex == 0);
        Assert.Contains(next, e => e.Kind == RecognitionEventKind.Partial && e.Text == "I am talk");
    }

    [Fact]
    public void Observe_AssignsAscendingStableSentenceIndices()
    {
        var reducer = new RecognitionReducer();

        reducer.Observe("One. Two. Three");
        var finals = Finals(reducer.Observe("One. Two. Three. Four"));

        Assert.Equal(new[] { "Three." }, finals.Select(e => e.Text));
        Assert.Equal(2, finals[0].SentenceIndex);
        Assert.All(finals, e => Assert.Equal(RecognitionEventKind.Final, e.Kind));
    }

    [Fact]
    public void Observe_ReemitsRevisedSentenceAsCorrectionAtSameIndex()
    {
        var reducer = new RecognitionReducer();

        // The recognizer briefly splits the trailing region into a short sentence
        // that becomes non-trailing (so it is finalized), then revises it. The
        // revision must re-surface as a Correction at the same index, not be dropped.
        var first = reducer.Observe("Let's check the numbers. You have six. Hundred thousand");
        var firstFinals = Finals(first);
        Assert.Equal(new[] { "Let's check the numbers.", "You have six." },
            firstFinals.Select(e => e.Text));
        Assert.All(firstFinals, e => Assert.Equal(RecognitionEventKind.Final, e.Kind));

        var recovered = reducer.Observe(
            "Let's check the numbers. You have $610,000 in stocks that reset. Understandable.");
        var correction = Assert.Single(Finals(recovered));
        Assert.Equal(RecognitionEventKind.Correction, correction.Kind);
        Assert.Equal("You have $610,000 in stocks that reset.", correction.Text);
        Assert.Equal(1, correction.SentenceIndex); // same index as the "You have six." final
    }

    [Fact]
    public void Observe_IdenticalReobservationEmitsNoFinalTwice()
    {
        var reducer = new RecognitionReducer();

        reducer.Observe("Alpha. Bravo. Charlie");
        var revised = Finals(reducer.Observe("Alpha. Bravo revised. Charlie"));
        Assert.Equal(new[] { "Bravo revised." }, revised.Select(e => e.Text));

        // Re-observing the same stable segmentation must not re-finalize anything.
        Assert.Empty(Finals(reducer.Observe("Alpha. Bravo revised. Charlie")));
        Assert.Empty(Finals(reducer.Observe("Alpha. Bravo revised. Charlie and more")));
    }

    [Fact]
    public void Observe_DoesNotRepeatAnUnchangedPartial()
    {
        var reducer = new RecognitionReducer();

        var first = reducer.Observe("still talking");
        Assert.Equal(RecognitionEventKind.Partial, Assert.Single(first).Kind);

        // Same trailing hypothesis again -> no new event at all.
        Assert.Empty(reducer.Observe("still talking"));

        // A changed trailing hypothesis -> a fresh partial.
        var grown = reducer.Observe("still talking now");
        Assert.Equal("still talking now", Assert.Single(grown).Text);
    }

    [Fact]
    public void Observe_FlushFinalizesTrailingSentenceAndEmitsNoPartial()
    {
        var reducer = new RecognitionReducer();

        reducer.Observe("Hello world. Goodbye now");
        var flushed = reducer.Observe("Hello world. Goodbye now.", flush: true);

        // The withheld trailing sentence is finalized; nothing is left as a partial.
        Assert.DoesNotContain(flushed, e => e.Kind == RecognitionEventKind.Partial);
        var final = Assert.Single(Finals(flushed));
        Assert.Equal(RecognitionEventKind.Final, final.Kind);
        Assert.Equal("Goodbye now.", final.Text);
        Assert.Equal(1, final.SentenceIndex);
    }

    [Fact]
    public void Observe_EmitsNewSentencePartialEvenWhenTextMatchesPreviousPartial()
    {
        var reducer = new RecognitionReducer();

        var first = reducer.Observe("Hello");
        Assert.Equal("Hello", Assert.Single(first).Text); // a partial

        // "Hello." finalizes at index 0; the new trailing sentence "Hello" must
        // still surface as a partial even though its text equals the prior partial.
        var second = reducer.Observe("Hello. Hello");
        Assert.Contains(second, e => e.Kind == RecognitionEventKind.Final
                                     && e.Text == "Hello." && e.SentenceIndex == 0);
        Assert.Contains(second, e => e.Kind == RecognitionEventKind.Partial && e.Text == "Hello");
    }

    [Fact]
    public void Observe_DoesNotReemitFinalWhenTerminatorFlickers()
    {
        var reducer = new RecognitionReducer();

        // Period present and a later sentence has started -> "Meet Sava." finalizes.
        var a = reducer.Observe("Meet Sava. Sign in");
        Assert.Contains(a, e => e.Kind == RecognitionEventKind.Final
                                && e.Text == "Meet Sava." && e.SentenceIndex == 0);

        // The terminator flickers away: the hypothesis collapses to one growing
        // sentence, dropping the stable sentence count. Nothing may re-finalize and
        // the already-final sentence must not be forgotten.
        var b = reducer.Observe("Meet Sava sign in without");
        Assert.DoesNotContain(b, e => e.IsFinal);

        // The period returns with a new sentence after it. The already-final
        // sentence must NOT be surfaced a second time (the observed 3x duplicate).
        var c = reducer.Observe("Meet Sava. Sign in without a password");
        Assert.DoesNotContain(c, e => e.IsFinal);
    }

    [Fact]
    public void Observe_EmptyOrWhitespaceHypothesisEmitsNothing()
    {
        var reducer = new RecognitionReducer();

        Assert.Empty(reducer.Observe(null));
        Assert.Empty(reducer.Observe(string.Empty));
        Assert.Empty(reducer.Observe("   "));
        Assert.Empty(reducer.Observe(string.Empty, flush: true));
    }

    [Fact]
    public void Observe_OrdersCorrectionsAndFinalsByIndexThenPartialLast()
    {
        var reducer = new RecognitionReducer();

        // Establish two finals.
        reducer.Observe("First one. Second one. tail");
        // Now revise the second (correction at index 1), add a new third (final at
        // index 2), and leave a fresh trailing partial.
        var events = reducer.Observe("First one. Second revised. Third one. new tail");

        Assert.Collection(events,
            e =>
            {
                Assert.Equal(RecognitionEventKind.Correction, e.Kind);
                Assert.Equal(1, e.SentenceIndex);
                Assert.Equal("Second revised.", e.Text);
            },
            e =>
            {
                Assert.Equal(RecognitionEventKind.Final, e.Kind);
                Assert.Equal(2, e.SentenceIndex);
                Assert.Equal("Third one.", e.Text);
            },
            e =>
            {
                Assert.Equal(RecognitionEventKind.Partial, e.Kind);
                Assert.Equal("new tail", e.Text);
            });
    }
}
