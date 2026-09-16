namespace Jellyfin.Plugin.JellyVision.Api;

/// <summary>
/// The result of saving a channel.
/// </summary>
/// <param name="Id">The channel id.</param>
/// <param name="ItemCount">How many items the channel resolves to.</param>
public record ChannelSavedDto(string Id, int ItemCount);
