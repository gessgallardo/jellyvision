namespace Jellyfin.Plugin.JellyVision.Configuration;

/// <summary>
/// The kind of library selector a <see cref="ChannelSource"/> represents.
/// </summary>
public enum SourceKind
{
    /// <summary>A single series; all of its episodes are used.</summary>
    Series = 0,

    /// <summary>A single movie.</summary>
    Movie = 1,

    /// <summary>A box set / collection; all of its children are used.</summary>
    Collection = 2,

    /// <summary>A playlist; its entries are used in playlist order.</summary>
    Playlist = 3,

    /// <summary>A filter over the library (genres and tags).</summary>
    Filter = 4,
}
