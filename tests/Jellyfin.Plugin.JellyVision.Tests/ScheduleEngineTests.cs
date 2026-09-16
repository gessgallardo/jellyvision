using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.JellyVision.Configuration;
using Jellyfin.Plugin.JellyVision.Scheduling;
using Xunit;

namespace Jellyfin.Plugin.JellyVision.Tests;

public class ScheduleEngineTests
{
    private static readonly DateTime Anchor = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static IReadOnlyList<ScheduleItem> Sample() =>
    [
        new("a", "S01E01", TimeSpan.FromMinutes(20), "s1"),
        new("b", "S01E02", TimeSpan.FromMinutes(30), "s1"),
        new("c", "S02E01", TimeSpan.FromMinutes(25), "s2"),
        new("d", "S02E02", TimeSpan.FromMinutes(45), "s2"),
    ];

    [Theory]
    [InlineData(0, "a", 0)]
    [InlineData(19, "a", 19)]
    [InlineData(20, "b", 0)]
    [InlineData(55, "c", 5)]
    [InlineData(119, "d", 44)]
    public void Sequential_MapsWallClockToItemAndOffset(int minutes, string expectedId, int expectedOffset)
    {
        var now = Anchor.AddMinutes(minutes);
        var slot = ScheduleEngine.GetCurrent(Sample(), ScheduleMode.Sequential, Anchor, now, 1, out var offset);

        Assert.NotNull(slot);
        Assert.Equal(expectedId, slot!.Value.Item.ItemId);
        Assert.Equal(expectedOffset, (int)offset.TotalMinutes);
    }

    [Fact]
    public void Cycle_WrapsAround()
    {
        // Total cycle is 120 minutes; 120 must land back on the first item.
        var slot = ScheduleEngine.GetCurrent(
            Sample(), ScheduleMode.Sequential, Anchor, Anchor.AddMinutes(120), 1, out var offset);

        Assert.Equal("a", slot!.Value.Item.ItemId);
        Assert.Equal(TimeSpan.Zero, offset);
    }

    [Fact]
    public void Schedule_IsDeterministicAcrossCalls()
    {
        var now = Anchor.AddHours(5000).AddMinutes(37);
        var first = ScheduleEngine.GetCurrent(Sample(), ScheduleMode.Shuffle, Anchor, now, 42, out var o1);
        var second = ScheduleEngine.GetCurrent(Sample(), ScheduleMode.Shuffle, Anchor, now, 42, out var o2);

        Assert.Equal(first!.Value.Item.ItemId, second!.Value.Item.ItemId);
        Assert.Equal(o1, o2);
        Assert.Equal(first.Value.StartUtc, second.Value.StartUtc);
    }

    [Fact]
    public void Shuffle_ReordersBetweenCycles()
    {
        var seed = ScheduleEngine.SeedFor("channel-one");
        var cycle = TimeSpan.FromMinutes(120);

        var orders = Enumerable.Range(0, 6)
            .Select(i => string.Join(
                ",",
                ScheduleEngine.GetGuide(
                    Sample(),
                    ScheduleMode.Shuffle,
                    Anchor,
                    Anchor + (cycle * i),
                    cycle,
                    seed).Select(s => s.Item.ItemId)))
            .ToList();

        Assert.True(orders.Distinct().Count() > 1, "shuffle must vary between cycles");
    }

    [Fact]
    public void BlockShuffle_KeepsBlocksContiguousAndInOrder()
    {
        var guide = ScheduleEngine.GetGuide(
            Sample(), ScheduleMode.BlockShuffle, Anchor, Anchor, TimeSpan.FromMinutes(120), 7);

        var ids = guide.Select(s => s.Item.ItemId).ToList();
        Assert.Equal(4, ids.Count);

        // Whichever block comes first, its two episodes must be adjacent and ordered.
        var ab = new[] { ids.IndexOf("a"), ids.IndexOf("b") };
        var cd = new[] { ids.IndexOf("c"), ids.IndexOf("d") };
        Assert.Equal(1, ab[1] - ab[0]);
        Assert.Equal(1, cd[1] - cd[0]);
    }

    [Fact]
    public void Guide_IsContiguousAndCoversWindow()
    {
        var from = Anchor.AddMinutes(43);
        var window = TimeSpan.FromHours(6);
        var guide = ScheduleEngine.GetGuide(Sample(), ScheduleMode.Sequential, Anchor, from, window, 1);

        Assert.NotEmpty(guide);
        for (var i = 1; i < guide.Count; i++)
        {
            Assert.Equal(guide[i - 1].EndUtc, guide[i].StartUtc);
        }

        Assert.True(guide[0].StartUtc <= from);
        Assert.True(guide[^1].EndUtc >= from + window);
    }

    [Fact]
    public void ZeroLengthItems_AreSkipped()
    {
        IReadOnlyList<ScheduleItem> items =
        [
            new("bad", "no runtime", TimeSpan.Zero, "x"),
            new("good", "ok", TimeSpan.FromMinutes(10), "x"),
        ];

        var slot = ScheduleEngine.GetCurrent(items, ScheduleMode.Sequential, Anchor, Anchor, 1, out _);
        Assert.Equal("good", slot!.Value.Item.ItemId);
    }

    [Fact]
    public void EmptyChannel_ReturnsNull()
    {
        Assert.Null(ScheduleEngine.GetCurrent([], ScheduleMode.Sequential, Anchor, Anchor, 1, out _));
    }
}
