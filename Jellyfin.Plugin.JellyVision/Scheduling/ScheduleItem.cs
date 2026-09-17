using System;

namespace Jellyfin.Plugin.JellyVision.Scheduling;

/// <summary>
/// One schedulable item on a channel, independent of Jellyfin types so the
/// engine stays pure and testable.
/// </summary>
/// <param name="ItemId">Library item id.</param>
/// <param name="Title">Display title.</param>
/// <param name="Duration">Run time.</param>
/// <param name="BlockKey">Grouping key used by block shuffle (for example season id).</param>
/// <param name="SeriesKey">Grouping key used by round-robin scheduling.</param>
public record struct ScheduleItem(
    string ItemId,
    string Title,
    TimeSpan Duration,
    string BlockKey,
    string SeriesKey = "");
