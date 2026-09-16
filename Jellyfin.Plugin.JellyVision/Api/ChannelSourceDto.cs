using System.Collections.Generic;

namespace Jellyfin.Plugin.JellyVision.Api;

/// <summary>
/// A channel source as sent by the configuration page.
/// </summary>
public class ChannelSourceDto
{
    /// <summary>
    /// Gets or sets the source kind.
    /// </summary>
    public int Kind { get; set; }

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
