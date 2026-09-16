namespace Jellyfin.Plugin.JellyVision.Configuration;

/// <summary>
/// How a channel orders its items across a broadcast cycle.
/// </summary>
public enum ScheduleMode
{
    /// <summary>Play items in their natural order (series, season, episode).</summary>
    Sequential = 0,

    /// <summary>Shuffle all items; reshuffled deterministically each cycle.</summary>
    Shuffle = 1,

    /// <summary>Shuffle whole blocks (a season or a series), keeping each block in order.</summary>
    BlockShuffle = 2,
}
