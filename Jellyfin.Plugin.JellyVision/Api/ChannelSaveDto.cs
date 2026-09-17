using System;
using System.Collections.Generic;
using Jellyfin.Plugin.JellyVision.Configuration;

namespace Jellyfin.Plugin.JellyVision.Api;

/// <summary>
/// A channel as sent by the configuration page.
/// </summary>
public class ChannelSaveDto
{
    /// <summary>
    /// Gets or sets the channel id. Empty when creating.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the channel number.
    /// </summary>
    public int Number { get; set; }

    /// <summary>
    /// Gets or sets the channel name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the channel is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the ordering mode.
    /// </summary>
    /// <remarks>
    /// Typed as the enum, not an int, so the payload may use either the name
    /// ("Sequential") or the number (0). The plugin configuration endpoint
    /// serialises enums as names, so a UI that round-trips a channel sends
    /// names back.
    /// </remarks>
    public ScheduleMode Mode { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether tuning in joins mid-programme.
    /// </summary>
    public bool LiveWallClock { get; set; } = true;

    /// <summary>
    /// Gets or sets the broadcast anchor. Null keeps the existing value.
    /// </summary>
    public DateTime? AnchorUtc { get; set; }

    /// <summary>
    /// Gets or sets the channel's sources.
    /// </summary>
    public IReadOnlyList<ChannelSourceDto> Sources { get; set; } = [];

    /// <summary>
    /// Gets or sets where filler is inserted.
    /// </summary>
    public FillerMode FillerMode { get; set; }

    /// <summary>
    /// Gets or sets how many filler items play between programmes.
    /// </summary>
    public int FillerCount { get; set; } = 1;

    /// <summary>
    /// Gets or sets the sources filler is drawn from.
    /// </summary>
    public IReadOnlyList<ChannelSourceDto> FillerSources { get; set; } = [];
}
