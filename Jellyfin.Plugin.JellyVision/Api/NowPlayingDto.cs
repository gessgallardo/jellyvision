using System;

namespace Jellyfin.Plugin.JellyVision.Api;

/// <summary>
/// What a channel is airing right now.
/// </summary>
/// <param name="ChannelId">The channel id.</param>
/// <param name="ChannelName">The channel name.</param>
/// <param name="ItemId">The library item to play.</param>
/// <param name="Title">The programme title.</param>
/// <param name="StartUtc">When the programme started.</param>
/// <param name="EndUtc">When the programme ends.</param>
/// <param name="OffsetTicks">How far to seek into the item when tuning in.</param>
public record NowPlayingDto(
    string ChannelId,
    string ChannelName,
    string ItemId,
    string Title,
    DateTime StartUtc,
    DateTime EndUtc,
    long OffsetTicks);
