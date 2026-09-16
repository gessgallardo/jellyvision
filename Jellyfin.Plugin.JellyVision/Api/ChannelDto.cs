namespace Jellyfin.Plugin.JellyVision.Api;

/// <summary>
/// A channel summary.
/// </summary>
/// <param name="Id">The channel id.</param>
/// <param name="Number">The channel number.</param>
/// <param name="Name">The channel name.</param>
/// <param name="Enabled">Whether the channel is enabled.</param>
/// <param name="ItemCount">How many items currently resolve for it.</param>
public record ChannelDto(string Id, int Number, string Name, bool Enabled, int ItemCount);
