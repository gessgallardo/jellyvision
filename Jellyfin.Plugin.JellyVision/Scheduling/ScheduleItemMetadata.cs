using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.JellyVision.Scheduling;

/// <summary>
/// Metadata carried alongside a scheduled item so the XMLTV guide can show
/// artwork, episode titles and descriptions.
/// </summary>
/// <remarks>
/// Kept separate from the scheduling fields so <see cref="ScheduleEngine"/>
/// stays a pure function of durations and keys.
/// </remarks>
public sealed class ScheduleItemMetadata
{
    /// <summary>
    /// Gets or sets the series name, for episodes.
    /// </summary>
    public string SeriesName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the episode or movie name on its own, without the series prefix.
    /// </summary>
    public string EpisodeName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the plot summary.
    /// </summary>
    public string Overview { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the season number, for episodes.
    /// </summary>
    public int? SeasonNumber { get; set; }

    /// <summary>
    /// Gets or sets the episode number, for episodes.
    /// </summary>
    public int? EpisodeNumber { get; set; }

    /// <summary>
    /// Gets or sets the production year.
    /// </summary>
    public int? Year { get; set; }

    /// <summary>
    /// Gets or sets the item whose Primary image represents this programme,
    /// normally the episode still.
    /// </summary>
    public string PrimaryImageItemId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the item carrying series-level artwork (poster, thumb, backdrop).
    /// </summary>
    public string SeriesImageItemId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the programme is a movie.
    /// </summary>
    public bool IsMovie { get; set; }

    /// <summary>
    /// Gets the genres, used by Jellyfin to categorise the programme.
    /// </summary>
    public IList<string> Genres { get; } = new List<string>();
}
