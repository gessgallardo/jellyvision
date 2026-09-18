using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.JellyVision.Configuration;
using Jellyfin.Plugin.JellyVision.Scheduling;
using Xunit;

namespace Jellyfin.Plugin.JellyVision.Tests;

/// <summary>
/// Filler plays but must never appear in the guide, and must not break the
/// determinism the whole schedule depends on.
/// </summary>
public class FillerTests
{
    private static readonly DateTime Anchor = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static IReadOnlyList<ScheduleItem> Programmes() =>
    [
        new("p1", "Show A - E1", TimeSpan.FromMinutes(30), "a"),
        new("p2", "Show A - E2", TimeSpan.FromMinutes(30), "a"),
        new("p3", "Show A - E3", TimeSpan.FromMinutes(30), "a"),
    ];

    private static IReadOnlyList<ScheduleItem> Bumpers() =>
    [
        new("b1", "Bumper 1", TimeSpan.FromMinutes(1), "f", "f", null, true),
        new("b2", "Bumper 2", TimeSpan.FromMinutes(2), "f", "f", null, true),
    ];

    [Fact]
    public void Interleave_PutsFillerBetweenProgrammesOnly()
    {
        var combined = ScheduleEngine.Interleave(Programmes(), Bumpers(), 1);

        Assert.Equal(
            ["p1", "b1", "p2", "b2", "p3"],
            combined.Select(i => i.ItemId));

        // No trailing filler: the cycle wraps straight into the next programme,
        // and the break after it comes from the following cycle.
        Assert.False(combined[^1].IsFiller);
    }

    [Fact]
    public void Interleave_HonoursTheCountBetweenProgrammes()
    {
        var combined = ScheduleEngine.Interleave(Programmes(), Bumpers(), 2);

        Assert.Equal(
            ["p1", "b1", "b2", "p2", "b1", "b2", "p3"],
            combined.Select(i => i.ItemId));
    }

    [Fact]
    public void Interleave_WithNoFiller_IsAPassThrough()
    {
        var items = Programmes();
        Assert.Same(items, ScheduleEngine.Interleave(items, [], 3));
        Assert.Same(items, ScheduleEngine.Interleave(items, Bumpers(), 0));
    }

    [Fact]
    public void Filler_ExtendsTheCycleByItsDuration()
    {
        // 3 x 30 min programmes, plus a 1 min and a 2 min bumper.
        var combined = ScheduleEngine.Interleave(Programmes(), Bumpers(), 1);
        var total = TimeSpan.FromTicks(combined.Sum(i => i.Duration.Ticks));

        Assert.Equal(TimeSpan.FromMinutes(93), total);
    }

    [Fact]
    public void Filler_ActuallyAirsInTheSchedule()
    {
        var combined = ScheduleEngine.Interleave(Programmes(), Bumpers(), 1);

        // 30 minutes in, the first bumper should be on.
        var slot = ScheduleEngine.GetCurrent(
            combined, ScheduleMode.Sequential, Anchor, Anchor.AddMinutes(30), 1, out var offset);

        Assert.Equal("b1", slot!.Value.Item.ItemId);
        Assert.True(slot.Value.Item.IsFiller);
        Assert.Equal(TimeSpan.Zero, offset);
    }

    [Theory]
    [InlineData(ScheduleMode.Sequential)]
    [InlineData(ScheduleMode.Shuffle)]
    [InlineData(ScheduleMode.BlockShuffle)]
    [InlineData(ScheduleMode.RoundRobin)]
    public void FilledSchedule_KeepsBreaksBetweenProgrammesForEveryMode(ScheduleMode mode)
    {
        var combined = ScheduleEngine.Interleave(Programmes(), Bumpers(), 1);
        var cycle = TimeSpan.FromTicks(combined.Sum(i => i.Duration.Ticks));
        var guide = ScheduleEngine.GetGuide(combined, mode, Anchor, Anchor, cycle, 1);

        for (var i = 1; i < guide.Count; i++)
        {
            Assert.False(
                guide[i - 1].Item.IsFiller && guide[i].Item.IsFiller,
                $"adjacent fillers at {i - 1} and {i}: {guide[i - 1].Item.ItemId}, {guide[i].Item.ItemId}");
        }
    }

    [Fact]
    public void FilledSchedule_RotatesTheLeadingBumperAcrossCycles()
    {
        var combined = ScheduleEngine.Interleave(Programmes(), Bumpers(), 1);
        var cycle = TimeSpan.FromTicks(combined.Sum(i => i.Duration.Ticks));
        var firstFillerByCycle = Enumerable.Range(0, 2)
            .Select(c => ScheduleEngine.GetGuide(
                combined,
                ScheduleMode.Sequential,
                Anchor,
                Anchor + (cycle * c),
                cycle,
                1)
                .First(slot => slot.Item.IsFiller)
                .Item.ItemId)
            .ToList();

        Assert.NotEqual(firstFillerByCycle[0], firstFillerByCycle[1]);
    }

    [Fact]
    public void FilledSchedule_PreservesAnUnevenFillerPool()
    {
        IReadOnlyList<ScheduleItem> combined =
        [
            .. Programmes(),
            .. Bumpers(),
            new("b3", "Bumper 3", TimeSpan.FromMinutes(1), "f", "f", null, true),
        ];
        var cycle = TimeSpan.FromTicks(combined.Sum(i => i.Duration.Ticks));
        var guide = ScheduleEngine.GetGuide(
            combined, ScheduleMode.BlockShuffle, Anchor, Anchor, cycle, 1);

        Assert.Equal(combined.Count, guide.Count);
        Assert.Equal(cycle, guide[^1].EndUtc - guide[0].StartUtc);
        Assert.Equal(
            3,
            guide.Count(slot => slot.Item.IsFiller));
    }

    [Fact]
    public void Guide_HidesFillerAndStaysContiguous()
    {
        var combined = ScheduleEngine.Interleave(Programmes(), Bumpers(), 1);
        var raw = ScheduleEngine.GetGuide(
            combined, ScheduleMode.Sequential, Anchor, Anchor, TimeSpan.FromMinutes(93), 1);

        var merged = ChannelTimeline.MergeFiller(raw);

        Assert.All(merged, s => Assert.False(s.Item.IsFiller));
        Assert.Equal(["p1", "p2", "p3"], merged.Select(s => s.Item.ItemId));

        // The break is absorbed into the following programme, so the guide has
        // no holes: each entry starts exactly where the previous one ended.
        for (var i = 1; i < merged.Count; i++)
        {
            Assert.Equal(merged[i - 1].EndUtc, merged[i].StartUtc);
        }
    }

    [Fact]
    public void Guide_ProgrammeAfterABreak_StartsWhenTheBreakStarted()
    {
        var combined = ScheduleEngine.Interleave(Programmes(), Bumpers(), 1);
        var raw = ScheduleEngine.GetGuide(
            combined, ScheduleMode.Sequential, Anchor, Anchor, TimeSpan.FromMinutes(93), 1);

        var merged = ChannelTimeline.MergeFiller(raw);

        // p1 runs 0-30, bumper 30-31, so p2 is listed from 30 rather than 31.
        Assert.Equal(Anchor.AddMinutes(30), merged[1].StartUtc);
        Assert.Equal(Anchor.AddMinutes(61), merged[1].EndUtc);
    }

    [Fact]
    public void MergeFiller_OnAScheduleWithoutFiller_ChangesNothing()
    {
        var raw = ScheduleEngine.GetGuide(
            Programmes(), ScheduleMode.Sequential, Anchor, Anchor, TimeSpan.FromMinutes(90), 1);

        var merged = ChannelTimeline.MergeFiller(raw);

        Assert.Equal(raw.Count, merged.Count);
        for (var i = 0; i < raw.Count; i++)
        {
            Assert.Equal(raw[i].StartUtc, merged[i].StartUtc);
            Assert.Equal(raw[i].Item.ItemId, merged[i].Item.ItemId);
        }
    }

    [Fact]
    public void FilledSchedule_IsStillDeterministic()
    {
        var combined = ScheduleEngine.Interleave(Programmes(), Bumpers(), 1);
        var now = Anchor.AddDays(500).AddMinutes(317);

        var a = ScheduleEngine.GetCurrent(
            combined, ScheduleMode.Shuffle, Anchor, now, 11, out var oa);
        var b = ScheduleEngine.GetCurrent(
            combined, ScheduleMode.Shuffle, Anchor, now, 11, out var ob);

        Assert.Equal(a!.Value.Item.ItemId, b!.Value.Item.ItemId);
        Assert.Equal(oa, ob);
    }

    [Fact]
    public void MergeFiller_HandlesAGuideThatOpensMidBreak()
    {
        // Tuning in during a bumper should still show the upcoming programme
        // rather than an empty guide.
        var combined = ScheduleEngine.Interleave(Programmes(), Bumpers(), 1);
        var from = Anchor.AddMinutes(30).AddSeconds(20);

        var raw = ScheduleEngine.GetGuide(
            combined, ScheduleMode.Sequential, Anchor, from, TimeSpan.FromMinutes(60), 1);

        var merged = ChannelTimeline.MergeFiller(raw);

        Assert.NotEmpty(merged);
        Assert.False(merged[0].Item.IsFiller);
        Assert.Equal("p2", merged[0].Item.ItemId);
    }
}
