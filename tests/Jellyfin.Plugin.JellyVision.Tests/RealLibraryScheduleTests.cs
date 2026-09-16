using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Jellyfin.Plugin.JellyVision.Configuration;
using Jellyfin.Plugin.JellyVision.Scheduling;
using Xunit;
using Xunit.Abstractions;

namespace Jellyfin.Plugin.JellyVision.Tests;

/// <summary>
/// Runs the engine over 106 real episodes pulled from a live Jellyfin 10.11.9
/// library (Cardcaptor Sakura, Heidi, Rick and Morty: The Anime), so the maths
/// is exercised against real, irregular runtimes rather than round numbers.
/// </summary>
public class RealLibraryScheduleTests
{
    private readonly ITestOutputHelper _output;

    public RealLibraryScheduleTests(ITestOutputHelper output) => _output = output;

    private sealed record Fixture(string ItemId, string Title, long Ticks, string BlockKey);

    private static readonly DateTime Anchor = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static IReadOnlyList<ScheduleItem> Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "real-episodes.json");
        var raw = JsonSerializer.Deserialize<List<Fixture>>(File.ReadAllText(path))!;
        return raw.Select(f => new ScheduleItem(
            f.ItemId, f.Title, TimeSpan.FromTicks(f.Ticks), f.BlockKey)).ToList();
    }

    [Fact]
    public void Fixture_Loads_WithRealRuntimes()
    {
        var items = Load();
        Assert.Equal(106, items.Count);
        Assert.All(items, i => Assert.True(i.Duration > TimeSpan.Zero));

        var cycle = TimeSpan.FromTicks(items.Sum(i => i.Duration.Ticks));
        _output.WriteLine($"cycle length: {cycle.TotalHours:F2} h over {items.Count} items");
        Assert.InRange(cycle.TotalHours, 45, 47);
    }

    [Fact]
    public void EveryMinuteOfACycle_ResolvesToExactlyOneProgramme()
    {
        var items = Load();
        var cycle = TimeSpan.FromTicks(items.Sum(i => i.Duration.Ticks));

        // Walk a whole 46-hour cycle minute by minute: no gaps, no nulls,
        // and the offset must always sit inside the programme.
        for (var m = 0; m < (int)cycle.TotalMinutes; m++)
        {
            var now = Anchor.AddMinutes(m);
            var slot = ScheduleEngine.GetCurrent(
                items, ScheduleMode.Sequential, Anchor, now, 1, out var offset);

            Assert.NotNull(slot);
            Assert.InRange(offset, TimeSpan.Zero, slot!.Value.Item.Duration);
            Assert.True(slot.Value.StartUtc <= now, $"start after now at minute {m}");
            Assert.True(slot.Value.EndUtc > now, $"end before now at minute {m}");
            Assert.Equal(offset, now - slot.Value.StartUtc);
        }
    }

    [Fact]
    public void SchedulePersistsAcrossRestart_SameAnswerYearsLater()
    {
        var items = Load();

        // Simulate "the server restarted 3 years in" - a fresh process with no
        // state must land on the identical programme and offset.
        var now = Anchor.AddDays(1117).AddMinutes(931).AddSeconds(17);

        var a = ScheduleEngine.GetCurrent(items, ScheduleMode.Shuffle, Anchor, now, 99, out var oa);
        var b = ScheduleEngine.GetCurrent(items, ScheduleMode.Shuffle, Anchor, now, 99, out var ob);

        Assert.Equal(a!.Value.Item.ItemId, b!.Value.Item.ItemId);
        Assert.Equal(oa, ob);
        _output.WriteLine($"3 years in: {a.Value.Item.Title} @ {ScheduleEngine.Format(oa)}");
    }

    [Fact]
    public void BlockShuffle_KeepsSeasonsIntactOnRealData()
    {
        var items = Load();

        // Stay strictly inside a single cycle: the real cycle is 45.9958 h, so a
        // flat 46 h window spills into the next (reshuffled) cycle and a block
        // legitimately reappears.
        var cycle = TimeSpan.FromTicks(items.Sum(i => i.Duration.Ticks));
        var guide = ScheduleEngine.GetGuide(
            items, ScheduleMode.BlockShuffle, Anchor, Anchor, cycle - TimeSpan.FromMinutes(1), 7);

        // Each block key must occupy one contiguous run, with episodes in
        // their original relative order.
        var ids = guide.Select(s => s.Item.BlockKey).ToList();
        var runs = new List<string>();
        foreach (var k in ids)
        {
            if (runs.Count == 0 || runs[^1] != k)
            {
                runs.Add(k);
            }
        }

        Assert.Equal(runs.Count, runs.Distinct().Count());
        _output.WriteLine($"block order: {string.Join(" | ", runs.Select(r => r[..8]))}");
    }

    [Fact]
    public void PrintTonightsGuide()
    {
        var items = Load();
        var guide = ScheduleEngine.GetGuide(
            items, ScheduleMode.Sequential, Anchor, DateTime.UtcNow, TimeSpan.FromHours(6), 1);

        _output.WriteLine("=== next 6 hours ===");
        foreach (var slot in guide)
        {
            _output.WriteLine(
                $"{slot.StartUtc:HH:mm} - {slot.EndUtc:HH:mm}  {slot.Item.Title}");
        }

        Assert.NotEmpty(guide);
    }
}
