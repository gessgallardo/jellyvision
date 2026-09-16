using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Jellyfin.Plugin.JellyVision.Configuration;
using Jellyfin.Plugin.JellyVision.Scheduling;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

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

    /// <summary>
    /// Initializes a new instance of the <see cref="ChannelManagementController"/> class.
    /// </summary>
    /// <param name="resolver">The channel resolver.</param>
    public ChannelManagementController(ChannelResolver resolver)
    {
        _resolver = resolver;
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
        channel.Mode = (ScheduleMode)dto.Mode;
        channel.LiveWallClock = dto.LiveWallClock;

        if (dto.AnchorUtc.HasValue)
        {
            channel.AnchorUtc = DateTime.SpecifyKind(dto.AnchorUtc.Value, DateTimeKind.Utc);
        }

        channel.Sources = dto.Sources
            .Select(s => new ChannelSource
            {
                Kind = (SourceKind)s.Kind,
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
            Mode = (ScheduleMode)dto.Mode,
            LiveWallClock = dto.LiveWallClock,
            Sources = dto.Sources
                .Select(s => new ChannelSource
                {
                    Kind = (SourceKind)s.Kind,
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
