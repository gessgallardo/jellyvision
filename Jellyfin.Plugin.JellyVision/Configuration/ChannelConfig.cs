using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.JellyVision.Configuration;

/// <summary>
/// A virtual channel definition.
/// </summary>
public class ChannelConfig
{
    /// <summary>
    /// Gets or sets the stable channel id.
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// Gets or sets the channel number shown in the guide.
    /// </summary>
    public int Number { get; set; }

    /// <summary>
    /// Gets or sets the channel name.
    /// </summary>
    public string Name { get; set; } = "Channel";

    /// <summary>
    /// Gets or sets a value indicating whether the channel is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the ordering mode.
    /// </summary>
    public ScheduleMode Mode { get; set; } = ScheduleMode.Sequential;

    /// <summary>
    /// Gets or sets a value indicating whether tuning in joins the programme already in
    /// progress (true, like cable) or always starts the next item from the beginning.
    /// </summary>
    public bool LiveWallClock { get; set; } = true;

    /// <summary>
    /// Gets or sets the UTC instant the channel is considered to have started broadcasting.
    /// The whole schedule is a pure function of this anchor, so restarts never desync it.
    /// </summary>
    public DateTime AnchorUtc { get; set; } = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// Gets the sources feeding this channel.
    /// </summary>
    public IList<ChannelSource> Sources { get; } = new List<ChannelSource>();
}
