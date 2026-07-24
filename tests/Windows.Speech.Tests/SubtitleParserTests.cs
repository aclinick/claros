namespace Windows.Speech.Tests;

public class SubtitleParserTests
{
    [Fact]
    public void Parse_Srt_ReadsTimingAndText()
    {
        const string srt =
            "1\n00:00:01,000 --> 00:00:03,500\nHello world.\n\n" +
            "2\n00:00:04,000 --> 00:00:05,000\nSecond line.\n";

        var cues = SubtitleParser.Parse(srt);

        Assert.Equal(2, cues.Count);
        Assert.Equal(TimeSpan.FromMilliseconds(1000), cues[0].Start);
        Assert.Equal(TimeSpan.FromMilliseconds(3500), cues[0].End);
        Assert.Equal("Hello world.", cues[0].Text);
        Assert.Equal(1, cues[0].Index);
        Assert.Equal("Second line.", cues[1].Text);
    }

    [Fact]
    public void Parse_Vtt_HandlesHeaderDotMillisAndCueSettings()
    {
        const string vtt =
            "WEBVTT\n\n" +
            "NOTE this is ignored\n\n" +
            "00:00:01.000 --> 00:00:02.000 line:0 position:50%\nCaption one\n";

        var cues = SubtitleParser.Parse(vtt);

        var cue = Assert.Single(cues);
        Assert.Equal(TimeSpan.FromSeconds(1), cue.Start);
        Assert.Equal(TimeSpan.FromSeconds(2), cue.End);
        Assert.Equal("Caption one", cue.Text);
    }

    [Fact]
    public void Parse_StripsTagsAndDecodesEntities()
    {
        const string vtt =
            "WEBVTT\n\n" +
            "00:00:00.000 --> 00:00:01.000\n<i>A &amp; B</i> <00:00:00.500>test\n";

        var cue = Assert.Single(SubtitleParser.Parse(vtt));

        Assert.Equal("A & B test", cue.Text);
    }

    [Fact]
    public void Parse_JoinsMultiLineCueTextWithSpace()
    {
        const string srt = "1\n00:00:01,000 --> 00:00:03,000\nfirst\nsecond\n";

        var cue = Assert.Single(SubtitleParser.Parse(srt));

        Assert.Equal("first second", cue.Text);
    }

    [Fact]
    public void Parse_SortsCuesByStartTime()
    {
        const string srt =
            "1\n00:00:05,000 --> 00:00:06,000\nlater\n\n" +
            "2\n00:00:01,000 --> 00:00:02,000\nearlier\n";

        var cues = SubtitleParser.Parse(srt);

        Assert.Equal("earlier", cues[0].Text);
        Assert.Equal("later", cues[1].Text);
    }

    [Fact]
    public void Parse_HandlesCrlfNewlines()
    {
        const string srt = "1\r\n00:00:01,000 --> 00:00:02,000\r\nHi.\r\n";

        var cue = Assert.Single(SubtitleParser.Parse(srt));

        Assert.Equal("Hi.", cue.Text);
    }

    [Fact]
    public void Parse_NoTimedCues_ReturnsEmpty()
    {
        Assert.Empty(SubtitleParser.Parse("WEBVTT\n\nNOTE nothing here\n"));
        Assert.Empty(SubtitleParser.Parse(""));
    }

    [Fact]
    public void Parse_SkipsBlockWithEmptyTextAfterStripping()
    {
        const string vtt = "WEBVTT\n\n00:00:01.000 --> 00:00:02.000\n<i></i>\n";

        Assert.Empty(SubtitleParser.Parse(vtt));
    }
}
