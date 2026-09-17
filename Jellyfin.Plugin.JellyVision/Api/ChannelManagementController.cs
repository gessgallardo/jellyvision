using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.JellyVision.Configuration;
using Jellyfin.Plugin.JellyVision.Scheduling;
using MediaBrowser.Common.Api;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Model.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyVision.Api;

/// <summary>
/// Channel management for the plugin configuration page.
/// </summary>
[ApiController]
[Authorize(Policy = Policies.RequiresElevation)]
[Route("JellyVision/Manage")]
[Produces(System.Net.Mime.MediaTypeNames.Application.Json)]
public class ChannelManagementController : ControllerBase
{
    private readonly ChannelResolver _resolver;
    private readonly ITaskManager _taskManager;
    private readonly IApplicationPaths _appPaths;
    private readonly ILogger<ChannelManagementController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChannelManagementController"/> class.
    /// </summary>
    /// <param name="resolver">The channel resolver.</param>
    /// <param name="taskManager">The scheduled task manager, used to refresh the guide.</param>
    /// <param name="appPaths">Application paths, used to clear the cached guide.</param>
    /// <param name="logger">The logger.</param>
    public ChannelManagementController(
        ChannelResolver resolver,
        ITaskManager taskManager,
        IApplicationPaths appPaths,
        ILogger<ChannelManagementController> logger)
    {
        _resolver = resolver;
        _taskManager = taskManager;
        _appPaths = appPaths;
        _logger = logger;
    }

    /// <summary>
    /// Creates or updates a channel.
    /// </summary>
    /// <param name="dto">The channel to save.</param>
    /// <returns>The saved channel's id.</returns>
    [HttpPost("Channels")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public ActionResult<ChannelSavedDto> SaveChannel([FromBody] ChannelSaveDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var plugin = Plugin.Instance;
        if (plugin is null)
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(dto.Name))
        {
            return BadRequest("A channel needs a name.");
        }

        if (dto.Sources.Count == 0)
        {
            return BadRequest("A channel needs at least one source.");
        }

        var config = plugin.Configuration;
        var channel = config.Channels.FirstOrDefault(
            c => string.Equals(c.Id, dto.Id, StringComparison.OrdinalIgnoreCase));

        if (channel is null)
        {
            channel = new ChannelConfig { Id = Guid.NewGuid().ToString("N") };
            config.Channels.Add(channel);
        }

        channel.Name = dto.Name.Trim();
        channel.Number = dto.Number > 0 ? dto.Number : NextNumber(config, channel);
        channel.Enabled = dto.Enabled;
        channel.Mode = dto.Mode;
        channel.LiveWallClock = dto.LiveWallClock;

        if (dto.AnchorUtc.HasValue)
        {
            channel.AnchorUtc = DateTime.SpecifyKind(dto.AnchorUtc.Value, DateTimeKind.Utc);
        }

        channel.Sources = dto.Sources
            .Select(s => new ChannelSource
            {
                Kind = s.Kind,
                ItemId = s.ItemId,
                Label = s.Label,
                Genres = [.. s.Genres],
                Tags = [.. s.Tags],
                IncludeEpisodes = s.IncludeEpisodes,
                IncludeMovies = s.IncludeMovies,
            })
            .ToList();

        plugin.UpdateConfiguration(config);

        return Ok(new ChannelSavedDto(channel.Id, _resolver.Resolve(channel).Count));
    }

    /// <summary>
    /// Deletes a channel.
    /// </summary>
    /// <param name="channelId">The channel id.</param>
    /// <returns>No content.</returns>
    [HttpDelete("Channels/{channelId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult DeleteChannel([FromRoute] string channelId)
    {
        var plugin = Plugin.Instance;
        if (plugin is null)
        {
            return NotFound();
        }

        var config = plugin.Configuration;
        var channel = config.Channels.FirstOrDefault(
            c => string.Equals(c.Id, channelId, StringComparison.OrdinalIgnoreCase));

        if (channel is null)
        {
            return NotFound();
        }

        config.Channels.Remove(channel);
        plugin.UpdateConfiguration(config);
        return NoContent();
    }

    /// <summary>
    /// Previews what a channel would air, without saving it.
    /// </summary>
    /// <param name="dto">The candidate channel.</param>
    /// <returns>The first few programmes it would play.</returns>
    [HttpPost("Preview")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<ChannelPreviewDto> Preview([FromBody] ChannelSaveDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var channel = new ChannelConfig
        {
            Id = string.IsNullOrEmpty(dto.Id) ? "preview" : dto.Id,
            Name = dto.Name,
            Mode = dto.Mode,
            LiveWallClock = dto.LiveWallClock,
            Sources = dto.Sources
                .Select(s => new ChannelSource
                {
                    Kind = s.Kind,
                    ItemId = s.ItemId,
                    Label = s.Label,
                    Genres = [.. s.Genres],
                    Tags = [.. s.Tags],
                    IncludeEpisodes = s.IncludeEpisodes,
                    IncludeMovies = s.IncludeMovies,
                })
                .ToList(),
        };

        if (dto.AnchorUtc.HasValue)
        {
            channel.AnchorUtc = DateTime.SpecifyKind(dto.AnchorUtc.Value, DateTimeKind.Utc);
        }

        var items = _resolver.Resolve(channel);
        var totalTicks = items.Sum(i => i.Duration.Ticks);

        var slots = ScheduleEngine.GetGuide(
            items,
            channel.Mode,
            channel.AnchorUtc,
            DateTime.UtcNow,
            TimeSpan.FromHours(6),
            ScheduleEngine.SeedFor(channel.Id));

        var entries = slots
            .Take(12)
            .Select(s => new GuideEntryDto(s.Item.ItemId, s.Item.Title, s.StartUtc, s.EndUtc))
            .ToList();

        return Ok(new ChannelPreviewDto(
            items.Count,
            TimeSpan.FromTicks(totalTicks).TotalHours,
            entries));
    }

    /// <summary>
    /// Asks Jellyfin to re-read the XMLTV guide.
    /// </summary>
    /// <remarks>
    /// Jellyfin caches guide data; editing a channel changes what JellyVision
    /// serves but the Live TV UI keeps showing the old listings until the
    /// "Refresh Guide" scheduled task runs (every few hours by default). This
    /// queues that task so a channel edit is visible straight away.
    /// Note that <c>POST /LiveTv/Guide/Refresh</c> does not exist on 10.11;
    /// the scheduled task is the supported route.
    /// </remarks>
    /// <returns>No content.</returns>
    [HttpPost("RefreshGuide")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult RefreshGuide()
    {
        // Jellyfin caches the downloaded XMLTV document for an hour, so running
        // the refresh task alone re-reads the same stale file and the guide does
        // not change. Drop the cache first.
        DropXmltvCache();

        // The guide task lives in Jellyfin.LiveTv, which plugins do not
        // reference, so it is located by key rather than by type.
        var task = _taskManager.ScheduledTasks.FirstOrDefault(
            t => string.Equals(t.ScheduledTask.Key, "RefreshGuide", StringComparison.Ordinal));

        if (task is null)
        {
            return NotFound("The Refresh Guide task was not found.");
        }

        _taskManager.Execute(task, new TaskOptions());
        return NoContent();
    }

    private void DropXmltvCache()
    {
        try
        {
            var dir = Path.Combine(_appPaths.CachePath, "xmltv");
            if (!Directory.Exists(dir))
            {
                return;
            }

            foreach (var file in Directory.EnumerateFiles(dir, "*.xml"))
            {
                try
                {
                    System.IO.File.Delete(file);
                }
                catch (IOException ex)
                {
                    _logger.LogDebug(ex, "Could not delete cached guide {File}", file);
                }
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Could not clear the XMLTV cache");
        }
    }

    private static int NextNumber(PluginConfiguration config, ChannelConfig self)
    {
        var used = config.Channels
            .Where(c => !ReferenceEquals(c, self))
            .Select(c => c.Number)
            .ToHashSet();

        var number = 1;
        while (used.Contains(number))
        {
            number++;
        }

        return number;
    }
}
