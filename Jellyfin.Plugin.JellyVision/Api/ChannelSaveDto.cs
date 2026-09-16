using System;
using System.Collections.Generic;

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
    public int Mode { get; set; }

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
}
