using System.Collections.Generic;

namespace Jellyfin.Plugin.JellyVision.Configuration;

/// <summary>
/// Bumpers, idents and fake commercials played between programmes.
/// </summary>
public class FillerConfig
{
    /// <summary>
    /// Gets or sets a value indicating where filler is inserted.
    /// </summary>
    public FillerMode Mode { get; set; } = FillerMode.None;

    /// <summary>
    /// Gets or sets how many filler items play between programmes.
    /// </summary>
    public int CountBetweenProgrammes { get; set; } = 1;

    /// <summary>
    /// Gets or sets the sources filler is drawn from.
    /// </summary>
    public List<ChannelSource> Sources { get; set; } = new List<ChannelSource>();
}
