using System;
using Jellyfin.Plugin.JellyVision.LiveTv;
using Xunit;

namespace Jellyfin.Plugin.JellyVision.Tests;

public class ConcatListBuilderTests
{
    [Fact]
    public void WritesDeclaredDurationsSoEpisodesAdvanceAtScheduleBoundaries()
    {
        var result = ConcatListBuilder.Build(
        [
            ("/media/episode-1.mkv", TimeSpan.FromMinutes(42), TimeSpan.FromMinutes(5)),
            ("/media/episode-2.mkv", TimeSpan.FromMinutes(43), TimeSpan.Zero),
        ]);

        Assert.Contains("file '/media/episode-1.mkv'\n", result.Text, StringComparison.Ordinal);
        Assert.Contains("duration 2220.000\n", result.Text, StringComparison.Ordinal);
        Assert.Contains("file '/media/episode-2.mkv'\n", result.Text, StringComparison.Ordinal);
        Assert.Contains("duration 2580.000\n", result.Text, StringComparison.Ordinal);
        Assert.Equal(TimeSpan.FromMinutes(80), result.Duration);
    }

    [Fact]
    public void EscapesQuotesAndSubtractsJoinOffset()
    {
        var result = ConcatListBuilder.Build(
        [
            ("/media/show's-episode.mkv", TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(2)),
        ]);

        Assert.Contains("file '/media/show'\\''s-episode.mkv'\n", result.Text, StringComparison.Ordinal);
        Assert.Contains("inpoint 120.000\n", result.Text, StringComparison.Ordinal);
        Assert.Contains("duration 480.000\n", result.Text, StringComparison.Ordinal);
        Assert.Equal(TimeSpan.FromMinutes(8), result.Duration);
    }
}
