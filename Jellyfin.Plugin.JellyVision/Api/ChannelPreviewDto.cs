using System.Collections.Generic;

namespace Jellyfin.Plugin.JellyVision.Api;

/// <summary>
/// A preview of what a channel would air.
/// </summary>
/// <param name="ItemCount">How many items resolve.</param>
/// <param name="TotalHours">The length of one full cycle, in hours.</param>
/// <param name="Upcoming">The next few programmes.</param>
public record ChannelPreviewDto(
    int ItemCount,
    double TotalHours,
    IReadOnlyList<GuideEntryDto> Upcoming);
