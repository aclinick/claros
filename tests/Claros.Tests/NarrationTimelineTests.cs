using Claros.Internal;

namespace Claros.Tests;

public class NarrationTimelineTests
{
    [Fact]
    public void Mix_PlacesClipsAtOffsets()
    {
        var placements = new[]
        {
            new NarrationTimeline.Placement(0, [1f, 1f]),
            new NarrationTimeline.Placement(5, [2f, 2f]),
        };

        var timeline = NarrationTimeline.Mix(placements, 0);

        Assert.Equal(7, timeline.Length);
        Assert.Equal(1f, timeline[0]);
        Assert.Equal(0f, timeline[2]);
        Assert.Equal(2f, timeline[5]);
        Assert.Equal(2f, timeline[6]);
    }

    [Fact]
    public void Mix_AdditivelyMixesOverlaps()
    {
        var placements = new[]
        {
            new NarrationTimeline.Placement(0, [1f, 1f, 1f]),
            new NarrationTimeline.Placement(1, [1f, 1f, 1f]),
        };

        var timeline = NarrationTimeline.Mix(placements, 0);

        Assert.Equal(1f, timeline[0]);
        Assert.Equal(2f, timeline[1]);
        Assert.Equal(2f, timeline[2]);
        Assert.Equal(1f, timeline[3]);
    }

    [Fact]
    public void Mix_HonorsMinimumLength()
    {
        var placements = new[] { new NarrationTimeline.Placement(0, [1f]) };

        var timeline = NarrationTimeline.Mix(placements, 10);

        Assert.Equal(10, timeline.Length);
    }

    [Fact]
    public void Mix_ClampsNegativeOffsetToZero()
    {
        var placements = new[] { new NarrationTimeline.Placement(-3, [1f, 1f]) };

        var timeline = NarrationTimeline.Mix(placements, 0);

        Assert.Equal(2, timeline.Length);
        Assert.Equal(1f, timeline[0]);
        Assert.Equal(1f, timeline[1]);
    }

    [Fact]
    public void Mix_NoPlacements_ReturnsMinLengthSilence()
    {
        var timeline = NarrationTimeline.Mix([], 4);

        Assert.Equal(4, timeline.Length);
        Assert.All(timeline, s => Assert.Equal(0f, s));
    }

    [Fact]
    public void ApplyEdgeFade_RampsBothEdgesFromZero()
    {
        var samples = new float[100];
        Array.Fill(samples, 1.0f);

        NarrationTimeline.ApplyEdgeFade(samples, sampleRate: 1000, milliseconds: 10);

        Assert.Equal(0f, samples[0]);              // fade-in starts at zero
        Assert.Equal(0f, samples[^1]);             // fade-out ends at zero
        Assert.True(samples[5] > 0f && samples[5] < 1f);
        Assert.Equal(1.0f, samples[50]);           // untouched middle
    }

    [Fact]
    public void ApplyEdgeFade_ShortClip_DoesNotThrow()
    {
        var samples = new[] { 1f };
        NarrationTimeline.ApplyEdgeFade(samples, sampleRate: 1000);
        Assert.Single(samples);
    }

    [Fact]
    public void ToSample_ConvertsTimeToSampleIndex()
    {
        Assert.Equal(48000, NarrationTimeline.ToSample(TimeSpan.FromSeconds(1), 48000));
        Assert.Equal(24000, NarrationTimeline.ToSample(TimeSpan.FromSeconds(0.5), 48000));
    }
}
