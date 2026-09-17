using System.Collections.Generic;
using Jellyfin.Plugin.JellyVision.Configuration;

namespace Jellyfin.Plugin.JellyVision.Api;

/// <summary>
/// A channel source as sent by the configuration page.
/// </summary>
public class ChannelSourceDto
{
    /// <summary>
    /// Gets or sets the source kind.
    /// </summary>
    /// <remarks>
    /// Typed as the enum so the payload may use either the name ("Series") or
    /// the number (0); see <see cref="ChannelSaveDto.Mode"/>.
    /// </remarks>
    public SourceKind Kind { get; set; }

    /// <summary>
    /// Gets or sets the library item id, for item-based kinds.
    /// </summary>
    public string ItemId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the display label.
    /// </summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the genres matched by a filter source.
    /// </summary>
    public IReadOnlyList<string> Genres { get; set; } = [];

    /// <summary>
    /// Gets or sets the tags matched by a filter source.
    /// </summary>
    public IReadOnlyList<string> Tags { get; set; } = [];

    /// <summary>
    /// Gets or sets a value indicating whether episodes are included.
    /// </summary>
    public bool IncludeEpisodes { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether movies are included.
    /// </summary>
    public bool IncludeMovies { get; set; } = true;
}
