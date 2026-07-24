using Windows.Speech.Internal;

namespace Windows.Speech.Tests;

public class SentenceCommitterTests
{
    [Fact]
    public void Take_RecoversTrailingRevisionAfterTransientSentenceAdvancedThePosition()
    {
        var committer = new SentenceCommitter();

        // The recognizer briefly splits the trailing region into an extra short
        // sentence ("You have six.") which — because a genuinely new sentence
        // ("Hundred thousand...") has started after it — becomes non-trailing and
        // is surfaced. It then revises that same region into the correct, longer
        // sentence. A count-based tracker would treat the corrected sentence as
        // "already past" and drop it; prefix reconciliation must re-surface it.
        var first = committer.Take("Let's check the numbers. You have six. Hundred thousand");
        Assert.Equal(new[] { "Let's check the numbers.", "You have six." }, first);

        var recovered = committer.Take(
            "Let's check the numbers. You have $610,000 in stocks that reset. Understandable.");

        // The 14-second financial sentence is recovered, not silently dropped.
        Assert.Equal(new[] { "You have $610,000 in stocks that reset." }, recovered);
    }

    [Fact]
    public void Take_IdenticalReobservationOfARevisedTrailingEmitsNothingTwice()
    {
        var committer = new SentenceCommitter();

        committer.Take("Alpha. Bravo. Charlie");
        var revised = committer.Take("Alpha. Bravo revised entirely. Charlie");
        Assert.Equal(new[] { "Bravo revised entirely." }, revised);

        // Re-observing the same stable segmentation must not duplicate anything.
        Assert.Empty(committer.Take("Alpha. Bravo revised entirely. Charlie"));
        Assert.Empty(committer.Take("Alpha. Bravo revised entirely. Charlie and more"));
    }

    [Fact]
    public void Take_WithholdsUnterminatedTrailingFragment()
    {
        var committer = new SentenceCommitter();

        var first = committer.Take("Hello world. I am still talk");

        // Only the completed sentence is emitted; the trailing fragment waits.
        Assert.Equal(new[] { "Hello world." }, first);
    }

    [Fact]
    public void Take_EmitsSentenceOnceItCompletes()
    {
        var committer = new SentenceCommitter();

        committer.Take("Hello world. I am still talk");
        var next = committer.Take("Hello world. I am still talking now. And more");

        Assert.Equal(new[] { "I am still talking now." }, next);
    }

    [Fact]
    public void Take_NeverReemitsAnAlreadyEmittedSentence()
    {
        var committer = new SentenceCommitter();

        committer.Take("One. Two. Three still going");
        var next = committer.Take("One. Two. Three still going and going");

        Assert.Empty(next);
    }

    [Fact]
    public void Take_LatePunctuationRevisionDoesNotDuplicatePriorSentences()
    {
        var committer = new SentenceCommitter();

        // Engine revises "resets understandable" into two sentences.
        committer.Take("when it resets understandable if you");
        var next = committer.Take("when it resets. Understandable. If you paid it off next");

        // "when it resets." was the withheld fragment before, so it and the newly
        // completed "Understandable." are emitted now; nothing is duplicated.
        Assert.Equal(new[] { "when it resets.", "Understandable." }, next);
    }

    [Fact]
    public void Take_Flush_EmitsRemainingTrailingFragment()
    {
        var committer = new SentenceCommitter();

        committer.Take("All done. Almost there");
        var flushed = committer.Take("All done. Almost there", flush: true);

        Assert.Equal(new[] { "Almost there" }, flushed);
    }

    [Fact]
    public void Take_Flush_ReleasesTerminatedTrailingSentenceOnlyOnce()
    {
        var committer = new SentenceCommitter();

        // A lone terminated sentence is still withheld (it may yet be revised),
        // so nothing is emitted until the flush releases it.
        Assert.Empty(committer.Take("Complete sentence."));
        var flushed = committer.Take("Complete sentence.", flush: true);
        Assert.Equal(new[] { "Complete sentence." }, flushed);

        // Once released it is never re-emitted.
        Assert.Empty(committer.Take("Complete sentence.", flush: true));
    }

    [Fact]
    public void Take_TransientTerminatorOnTrailingFragmentIsNotEmittedPrematurely()
    {
        var committer = new SentenceCommitter();

        // The recognizer briefly ends the trailing fragment with a period, then
        // revises it in place with the full number. Because the trailing sentence
        // is withheld until a later one starts, the premature "You have six." is
        // never surfaced; the corrected sentence is emitted once confirmed.
        Assert.Empty(committer.Take("You have six."));
        var next = committer.Take("You have $610,000 in stocks. And a loan of");

        Assert.Equal(new[] { "You have $610,000 in stocks." }, next);
    }

    [Fact]
    public void Take_HandlesQuestionAndExclamation()
    {
        var committer = new SentenceCommitter();

        var emitted = committer.Take("Really? Yes! Now what", flush: false);

        Assert.Equal(new[] { "Really?", "Yes!" }, emitted);
    }

    [Fact]
    public void Take_EmptyOrNull_ReturnsEmpty()
    {
        var committer = new SentenceCommitter();

        Assert.Empty(committer.Take(null));
        Assert.Empty(committer.Take(""));
        Assert.Empty(committer.Take("   "));
    }

    [Fact]
    public void Take_MultipleSentencesCompletingAtOnce()
    {
        var committer = new SentenceCommitter();

        // The last sentence is withheld until confirmed; a flush releases it.
        var emitted = committer.Take("First. Second. Third.");
        Assert.Equal(new[] { "First.", "Second." }, emitted);

        Assert.Equal(new[] { "Third." }, committer.Take("First. Second. Third.", flush: true));
    }
}
