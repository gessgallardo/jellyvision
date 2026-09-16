using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.JellyVision.Configuration;
using Jellyfin.Plugin.JellyVision.Scheduling;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.JellyVision.Api;

/// <summary>
/// JellyVision channel and schedule endpoints.
/// </summary>
[ApiController]
[Authorize]
[Route("JellyVision")]
[Produces(System.Net.Mime.MediaTypeNames.Application.Json)]
public class JellyVisionController : ControllerBase
{
    private readonly ChannelResolver _resolver;

    /// <summary>
    /// Initializes a new instance of the <see cref="JellyVisionController"/> class.
    /// </summary>
    /// <param name="resolver">The channel resolver.</param>
    public JellyVisionController(ChannelResolver resolver)
    {
        _resolver = resolver;
    }

    private static PluginConfiguration Config
        => Plugin.Instance?.Configuration ?? new PluginConfiguration();

    /// <summary>
    /// Lists configured channels.
    /// </summary>
    /// <returns>The channels.</returns>
    [HttpGet("Channels")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<ChannelDto>> GetChannels()
    {
        var result = Config.Channels
            .OrderBy(c => c.Number)
            .Select(c => new ChannelDto(
                c.Id,
                c.Number,
                c.Name,
                c.Enabled,
                _resolver.Resolve(c).Count))
            .ToList();

        return Ok(result);
    }

    /// <summary>
    /// Gets what a channel is airing right now, with the seek offset.
    /// </summary>
    /// <param name="channelId">The channel id.</param>
    /// <returns>The current programme.</returns>
    [HttpGet("Channels/{channelId}/Now")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<NowPlayingDto> GetNow([FromRoute] string channelId)
    {
        var channel = Find(channelId);
        if (channel is null)
        {
            return NotFound();
        }

        var items = _resolver.Resolve(channel);
        var slot = ScheduleEngine.GetCurrent(
            items,
            channel.Mode,
            channel.AnchorUtc,
            DateTime.UtcNow,
            ScheduleEngine.SeedFor(channel.Id),
            out var offset);

        if (slot is null)
        {
            return NotFound();
        }

        var value = slot.Value;
        return Ok(new NowPlayingDto(
            channel.Id,
            channel.Name,
            value.Item.ItemId,
            value.Item.Title,
            value.StartUtc,
            value.EndUtc,
            channel.LiveWallClock ? offset.Ticks : 0));
    }

    /// <summary>
    /// Gets the upcoming guide for a channel.
    /// </summary>
    /// <param name="channelId">The channel id.</param>
    /// <param name="hours">How many hours to return.</param>
    /// <returns>The guide entries.</returns>
    [HttpGet("Channels/{channelId}/Guide")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<IReadOnlyList<GuideEntryDto>> GetGuide(
        [FromRoute] string channelId,
        [FromQuery] int? hours)
    {
        var channel = Find(channelId);
        if (channel is null)
        {
            return NotFound();
        }

        var items = _resolver.Resolve(channel);
        var window = TimeSpan.FromHours(Math.Clamp(hours ?? Config.GuideHours, 1, 168));
        var slots = ScheduleEngine.GetGuide(
            items,
            channel.Mode,
            channel.AnchorUtc,
            DateTime.UtcNow,
            window,
            ScheduleEngine.SeedFor(channel.Id));

        return Ok(slots
            .Select(s => new GuideEntryDto(s.Item.ItemId, s.Item.Title, s.StartUtc, s.EndUtc))
            .ToList());
    }

    private static ChannelConfig? Find(string channelId)
        => Config.Channels.FirstOrDefault(
            c => string.Equals(c.Id, channelId, StringComparison.OrdinalIgnoreCase));
}
