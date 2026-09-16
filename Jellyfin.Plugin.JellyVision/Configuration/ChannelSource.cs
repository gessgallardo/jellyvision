using System.Collections.Generic;

namespace Jellyfin.Plugin.JellyVision.Configuration;

/// <summary>
/// One opted-in piece of content feeding a channel.
/// </summary>
public class ChannelSource
{
    /// <summary>
    /// Gets or sets the kind of selector.
    /// </summary>
    public SourceKind Kind { get; set; }

    /// <summary>
    /// Gets or sets the library item id, for item-based kinds.
    /// </summary>
    public string ItemId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a human readable label, shown in the configuration page.
    /// </summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// Gets the genres a <see cref="SourceKind.Filter"/> source matches.
    /// </summary>
    public IList<string> Genres { get; } = new List<string>();

    /// <summary>
    /// Gets the tags a <see cref="SourceKind.Filter"/> source matches.
    /// </summary>
    public IList<string> Tags { get; } = new List<string>();

    /// <summary>
    /// Gets or sets a value indicating whether episodes are included for filter sources.
    /// </summary>
    public bool IncludeEpisodes { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether movies are included for filter sources.
    /// </summary>
    public bool IncludeMovies { get; set; } = true;
}
