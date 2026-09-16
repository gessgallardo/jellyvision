using System.Collections.Generic;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.JellyVision.Configuration;

/// <summary>
/// Plugin configuration.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets the configured channels.
    /// </summary>
    public IList<ChannelConfig> Channels { get; } = new List<ChannelConfig>();

    /// <summary>
    /// Gets or sets how many hours of guide data to generate on request.
    /// </summary>
    public int GuideHours { get; set; } = 24;
}
