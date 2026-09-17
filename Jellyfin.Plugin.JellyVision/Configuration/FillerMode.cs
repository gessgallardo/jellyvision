namespace Jellyfin.Plugin.JellyVision.Configuration;

/// <summary>
/// Where filler is inserted on a channel.
/// </summary>
public enum FillerMode
{
    /// <summary>No filler.</summary>
    None = 0,

    /// <summary>Play filler between programmes.</summary>
    BetweenProgrammes = 1,
}
