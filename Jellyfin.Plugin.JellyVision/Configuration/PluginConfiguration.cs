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

    /// <summary>
    /// Gets or sets the absolute base URL clients should use to reach this
    /// server, for example <c>https://jellyfin.example.com/jellyfin</c>.
    /// </summary>
    /// <remarks>
    /// The stream URLs in the M3U playlist are built from the incoming request
    /// by default, which is wrong behind a reverse proxy that terminates TLS
    /// without sending X-Forwarded-Proto: the playlist then advertises http://
    /// and clients may refuse to load it from an https page. Set this when the
    /// generated URLs do not match how clients actually reach the server.
    /// </remarks>
    public string PublicBaseUrl { get; set; } = string.Empty;
}
