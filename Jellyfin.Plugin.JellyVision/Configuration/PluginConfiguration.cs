using System.Collections.Generic;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.JellyVision.Configuration;

/// <summary>
/// Plugin configuration.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets the configured channels.
    /// </summary>
    public List<ChannelConfig> Channels { get; set; } = new List<ChannelConfig>();

    /// <summary>
    /// Gets or sets how many hours of guide data to generate on request.
    /// </summary>
    public int GuideHours { get; set; } = 24;
}
