using System;
using System.Collections.Generic;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.JellyVision.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyVision.Scheduling;

/// <summary>
/// Turns a channel's opted-in sources into a concrete, ordered item list.
/// </summary>
public class ChannelResolver
{
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<ChannelResolver> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChannelResolver"/> class.
    /// </summary>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="logger">The logger.</param>
    public ChannelResolver(ILibraryManager libraryManager, ILogger<ChannelResolver> logger)
    {
        _libraryManager = libraryManager;
        _logger = logger;
    }

    /// <summary>
    /// Resolves every source of a channel into schedulable items, in natural order.
    /// </summary>
    /// <param name="channel">The channel.</param>
    /// <returns>The channel's item list.</returns>
    public IReadOnlyList<ScheduleItem> Resolve(ChannelConfig channel)
    {
        ArgumentNullException.ThrowIfNull(channel);

        var items = new List<ScheduleItem>();
        var seen = new HashSet<Guid>();

        foreach (var source in channel.Sources)
        {
            foreach (var item in Expand(source))
            {
                if (item.RunTimeTicks is null or <= 0)
                {
                    continue;
                }

                if (!seen.Add(item.Id))
                {
                    continue;
                }

                items.Add(ToScheduleItem(item));
            }
        }

        _logger.LogDebug(
            "Resolved {Count} items for channel {Channel}",
            items.Count,
            channel.Name);

        return items;
    }

    /// <summary>
    /// Resolves the filler pool for a channel: bumpers, idents and the like.
    /// </summary>
    /// <param name="channel">The channel.</param>
    /// <returns>The filler items, marked so the guide can ignore them.</returns>
    public IReadOnlyList<ScheduleItem> ResolveFiller(ChannelConfig channel)
    {
        ArgumentNullException.ThrowIfNull(channel);

        if (channel.Filler.Mode == FillerMode.None || channel.Filler.Sources.Count == 0)
        {
            return [];
        }

        var items = new List<ScheduleItem>();
        var seen = new HashSet<Guid>();

        foreach (var source in channel.Filler.Sources)
        {
            foreach (var item in Expand(source))
            {
                if (item.RunTimeTicks is null or <= 0 || !seen.Add(item.Id))
                {
                    continue;
                }

                items.Add(ToScheduleItem(item) with { IsFiller = true });
            }
        }

        _logger.LogDebug(
            "Resolved {Count} filler items for channel {Channel}",
            items.Count,
            channel.Name);

        return items;
    }

    private static ScheduleItem ToScheduleItem(BaseItem item)
    {
        var title = item is Episode episode
            ? $"{episode.SeriesName} - {episode.Name}"
            : item.Name;

        var blockKey = item switch
        {
            Episode e when e.SeasonId != Guid.Empty => e.SeasonId.ToString("N"),
            Episode e => e.SeriesId.ToString("N"),
            _ => item.Id.ToString("N"),
        };

        var seriesKey = item is Episode episodeWithSeries && episodeWithSeries.SeriesId != Guid.Empty
            ? episodeWithSeries.SeriesId.ToString("N")
            : item.Id.ToString("N");

        return new ScheduleItem(
            item.Id.ToString("N"),
            title ?? string.Empty,
            TimeSpan.FromTicks(item.RunTimeTicks ?? 0),
            blockKey,
            seriesKey,
            BuildMetadata(item));
    }

    private static ScheduleItemMetadata BuildMetadata(BaseItem item)
    {
        var metadata = new ScheduleItemMetadata
        {
            EpisodeName = item.Name ?? string.Empty,
            Overview = item.Overview ?? string.Empty,
            Year = item.ProductionYear,
            IsMovie = item is Movie,
            PrimaryImageItemId = item.HasImage(ImageType.Primary)
                ? item.Id.ToString("N")
                : string.Empty,
        };

        foreach (var genre in item.Genres)
        {
            metadata.Genres.Add(genre);
        }

        if (item is Episode episode)
        {
            metadata.SeriesName = episode.SeriesName ?? string.Empty;
            metadata.SeasonNumber = episode.ParentIndexNumber;
            metadata.EpisodeNumber = episode.IndexNumber;

            if (episode.SeriesId != Guid.Empty)
            {
                metadata.SeriesImageItemId = episode.SeriesId.ToString("N");
            }
        }
        else
        {
            metadata.SeriesImageItemId = metadata.PrimaryImageItemId;
        }

        return metadata;
    }

    private IEnumerable<BaseItem> Expand(ChannelSource source)
    {
        switch (source.Kind)
        {
            case SourceKind.Series:
                return EpisodesOfSeries(source.ItemId);

            case SourceKind.Movie:
                return Single(source.ItemId);

            case SourceKind.Collection:
            case SourceKind.Playlist:
                return Children(source.ItemId);

            case SourceKind.Filter:
                return Filtered(source);

            default:
                return [];
        }
    }

    private IEnumerable<BaseItem> Single(string id)
    {
        if (!Guid.TryParse(id, out var guid))
        {
            yield break;
        }

        var item = _libraryManager.GetItemById(guid);
        if (item is not null)
        {
            yield return item;
        }
    }

    private IReadOnlyList<BaseItem> EpisodesOfSeries(string id)
    {
        if (!Guid.TryParse(id, out var guid))
        {
            return [];
        }

        return _libraryManager.GetItemList(new InternalItemsQuery
        {
            AncestorIds = [guid],
            IncludeItemTypes = [BaseItemKind.Episode],
            Recursive = true,
            IsVirtualItem = false,
            OrderBy =
            [
                (ItemSortBy.ParentIndexNumber, SortOrder.Ascending),
                (ItemSortBy.IndexNumber, SortOrder.Ascending),
            ],
        });
    }

    private IReadOnlyList<BaseItem> Children(string id)
    {
        if (!Guid.TryParse(id, out var guid))
        {
            return [];
        }

        var parent = _libraryManager.GetItemById(guid);
        return parent switch
        {
            Playlist playlist => playlist.GetLinkedChildren(),
            Folder folder => _libraryManager.GetItemList(new InternalItemsQuery
            {
                AncestorIds = [folder.Id],
                IncludeItemTypes = [BaseItemKind.Movie, BaseItemKind.Episode],
                Recursive = true,
                IsVirtualItem = false,
            }),
            _ => [],
        };
    }

    private IReadOnlyList<BaseItem> Filtered(ChannelSource source)
    {
        var kinds = new List<BaseItemKind>();
        if (source.IncludeMovies)
        {
            kinds.Add(BaseItemKind.Movie);
        }

        if (source.IncludeEpisodes)
        {
            kinds.Add(BaseItemKind.Episode);
        }

        if (kinds.Count == 0)
        {
            return [];
        }

        return _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = [.. kinds],
            Recursive = true,
            IsVirtualItem = false,
            Genres = [.. source.Genres],
            Tags = [.. source.Tags],
            OrderBy =
            [
                (ItemSortBy.SeriesSortName, SortOrder.Ascending),
                (ItemSortBy.ParentIndexNumber, SortOrder.Ascending),
                (ItemSortBy.IndexNumber, SortOrder.Ascending),
            ],
        });
    }
}
