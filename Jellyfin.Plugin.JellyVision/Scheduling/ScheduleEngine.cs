using System;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Plugin.JellyVision.Configuration;

namespace Jellyfin.Plugin.JellyVision.Scheduling;

/// <summary>
/// Deterministic linear-TV schedule engine.
/// </summary>
/// <remarks>
/// The schedule is a pure function of (item list, mode, anchor, wall clock).
/// Nothing is persisted, so a server restart, a redeploy or a second process
/// all compute the identical timeline and the guide can never drift from the
/// stream.
/// </remarks>
public static class ScheduleEngine
{
    /// <summary>
    /// Computes what is airing at <paramref name="nowUtc"/>, including how far into it we are.
    /// </summary>
    /// <param name="items">The channel's item list, in natural order.</param>
    /// <param name="mode">The ordering mode.</param>
    /// <param name="anchorUtc">The channel's broadcast anchor.</param>
    /// <param name="nowUtc">The current time.</param>
    /// <param name="seed">A per-channel seed, normally derived from the channel id.</param>
    /// <param name="offset">The elapsed time into the returned programme.</param>
    /// <returns>The current programme, or null when the channel has no playable items.</returns>
    public static ProgramSlot? GetCurrent(
        IReadOnlyList<ScheduleItem> items,
        ScheduleMode mode,
        DateTime anchorUtc,
        DateTime nowUtc,
        int seed,
        out TimeSpan offset)
    {
        offset = TimeSpan.Zero;
        var cycle = TotalDuration(items);
        if (cycle <= TimeSpan.Zero)
        {
            return null;
        }

        var elapsed = nowUtc - anchorUtc;
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        var cycleTicks = cycle.Ticks;
        var cycleIndex = elapsed.Ticks / cycleTicks;
        var intoCycle = elapsed.Ticks % cycleTicks;

        var order = Order(items, mode, seed, cycleIndex);
        var cursor = 0L;
        foreach (var item in order)
        {
            var next = cursor + item.Duration.Ticks;
            if (intoCycle < next)
            {
                offset = TimeSpan.FromTicks(intoCycle - cursor);
                var startUtc = anchorUtc.AddTicks((cycleIndex * cycleTicks) + cursor);
                return new ProgramSlot(item, startUtc, startUtc + item.Duration);
            }

            cursor = next;
        }

        return null;
    }

    /// <summary>
    /// Builds the guide for a window starting at <paramref name="fromUtc"/>.
    /// </summary>
    /// <param name="items">The channel's item list, in natural order.</param>
    /// <param name="mode">The ordering mode.</param>
    /// <param name="anchorUtc">The channel's broadcast anchor.</param>
    /// <param name="fromUtc">Window start.</param>
    /// <param name="window">Window length.</param>
    /// <param name="seed">A per-channel seed.</param>
    /// <returns>The programmes overlapping the window, in order.</returns>
    public static IReadOnlyList<ProgramSlot> GetGuide(
        IReadOnlyList<ScheduleItem> items,
        ScheduleMode mode,
        DateTime anchorUtc,
        DateTime fromUtc,
        TimeSpan window,
        int seed)
    {
        var slots = new List<ProgramSlot>();
        var cycle = TotalDuration(items);
        if (cycle <= TimeSpan.Zero || window <= TimeSpan.Zero)
        {
            return slots;
        }

        var endUtc = fromUtc + window;
        var cycleTicks = cycle.Ticks;

        // Start at the boundary of the cycle containing fromUtc and walk the
        // order from its first item. Anchoring the cursor at the *current*
        // programme's start while iterating the order from index 0 would pair
        // the current start time with the wrong item, and the guide would
        // disagree with what is actually airing.
        var elapsedTicks = (fromUtc - anchorUtc).Ticks;
        if (elapsedTicks < 0)
        {
            elapsedTicks = 0;
        }

        var startCycle = elapsedTicks / cycleTicks;
        var cursorUtc = anchorUtc.AddTicks(startCycle * cycleTicks);

        for (var cycleIndex = startCycle; cursorUtc < endUtc; cycleIndex++)
        {
            var order = Order(items, mode, seed, cycleIndex);
            foreach (var item in order)
            {
                if (cursorUtc >= endUtc)
                {
                    break;
                }

                var slotEnd = cursorUtc + item.Duration;
                if (slotEnd > fromUtc)
                {
                    slots.Add(new ProgramSlot(item, cursorUtc, slotEnd));
                }

                cursorUtc = slotEnd;
            }
        }

        return slots;
    }

    /// <summary>
    /// Derives a stable integer seed from a channel id.
    /// </summary>
    /// <param name="channelId">The channel id.</param>
    /// <returns>A stable seed.</returns>
    public static int SeedFor(string channelId)
    {
        ArgumentNullException.ThrowIfNull(channelId);
        unchecked
        {
            var hash = 17;
            foreach (var c in channelId)
            {
                hash = (hash * 31) + c;
            }

            return hash;
        }
    }

    private static TimeSpan TotalDuration(IReadOnlyList<ScheduleItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var ticks = 0L;
        for (var i = 0; i < items.Count; i++)
        {
            if (items[i].Duration > TimeSpan.Zero)
            {
                ticks += items[i].Duration.Ticks;
            }
        }

        return TimeSpan.FromTicks(ticks);
    }

    private static List<ScheduleItem> Order(
        IReadOnlyList<ScheduleItem> items,
        ScheduleMode mode,
        int seed,
        long cycleIndex)
    {
        var playable = new List<ScheduleItem>(items.Count);
        for (var i = 0; i < items.Count; i++)
        {
            if (items[i].Duration > TimeSpan.Zero)
            {
                playable.Add(items[i]);
            }
        }

        switch (mode)
        {
            case ScheduleMode.Shuffle:
                Shuffle(playable, CycleSeed(seed, cycleIndex));
                return playable;

            case ScheduleMode.BlockShuffle:
                return BlockShuffle(playable, CycleSeed(seed, cycleIndex));

            case ScheduleMode.RoundRobin:
                return RoundRobin(playable);

            default:
                return playable;
        }
    }

    private static List<ScheduleItem> BlockShuffle(List<ScheduleItem> items, int seed)
    {
        var blocks = new List<List<ScheduleItem>>();
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            var key = string.IsNullOrEmpty(item.BlockKey) ? item.ItemId : item.BlockKey;
            if (!index.TryGetValue(key, out var pos))
            {
                pos = blocks.Count;
                index[key] = pos;
                blocks.Add([]);
            }

            blocks[pos].Add(item);
        }

        Shuffle(blocks, seed);

        var result = new List<ScheduleItem>(items.Count);
        foreach (var block in blocks)
        {
            result.AddRange(block);
        }

        return result;
    }

    private static List<ScheduleItem> RoundRobin(List<ScheduleItem> items)
    {
        var series = new List<List<ScheduleItem>>();
        var index = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var item in items)
        {
            var key = string.IsNullOrEmpty(item.SeriesKey)
                ? item.BlockKey
                : item.SeriesKey;

            if (!index.TryGetValue(key, out var position))
            {
                position = series.Count;
                index[key] = position;
                series.Add([]);
            }

            series[position].Add(item);
        }

        var result = new List<ScheduleItem>(items.Count);
        var round = 0;
        while (true)
        {
            var added = false;
            foreach (var episodes in series)
            {
                if (round < episodes.Count)
                {
                    result.Add(episodes[round]);
                    added = true;
                }
            }

            if (!added)
            {
                return result;
            }

            round++;
        }
    }

    private static void Shuffle<T>(IList<T> list, int seed)
    {
        // Fisher-Yates driven by a seeded PRNG: same seed, same order, everywhere.
        var rng = new Random(seed);
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    private static int CycleSeed(int seed, long cycleIndex)
    {
        unchecked
        {
            return seed ^ (int)(cycleIndex * 2654435761L);
        }
    }

    /// <summary>
    /// Formats a duration for logs and the guide.
    /// </summary>
    /// <param name="value">The duration.</param>
    /// <returns>An invariant hh:mm:ss string.</returns>
    public static string Format(TimeSpan value)
        => value.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
}
