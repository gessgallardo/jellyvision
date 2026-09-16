using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyVision.Configuration;
using Jellyfin.Plugin.JellyVision.LiveTv;
using Jellyfin.Plugin.JellyVision.Scheduling;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.JellyVision.Api;

/// <summary>
/// The IPTV surface Jellyfin's Live TV consumes: an M3U playlist, an XMLTV
/// guide and a continuous MPEG-TS stream per channel.
/// </summary>
/// <remarks>
/// These endpoints are anonymous by necessity: Jellyfin's M3U tuner and XMLTV
/// guide provider fetch them with a plain HTTP client that sends no
/// credentials, and so do external IPTV players. They expose channel names and
/// programme titles, and will stream media to anyone who can reach them, so the
/// server should not be published to the internet without a reverse proxy.
/// </remarks>
[ApiController]
[AllowAnonymous]
[Route("JellyVision/iptv")]
public class IptvController : ControllerBase
{
    private readonly ChannelResolver _resolver;
    private readonly ChannelStreamer _streamer;

    /// <summary>
    /// Initializes a new instance of the <see cref="IptvController"/> class.
    /// </summary>
    /// <param name="resolver">The channel resolver.</param>
    /// <param name="streamer">The channel streamer.</param>
    public IptvController(ChannelResolver resolver, ChannelStreamer streamer)
    {
        _resolver = resolver;
        _streamer = streamer;
    }

    private static PluginConfiguration Config
        => Plugin.Instance?.Configuration ?? new PluginConfiguration();

    /// <summary>
    /// Gets the M3U playlist of JellyVision channels.
    /// </summary>
    /// <returns>The playlist.</returns>
    [HttpGet("channels.m3u")]
    [Produces("application/x-mpegurl")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult GetPlaylist()
    {
        var baseUrl = string.Create(
            CultureInfo.InvariantCulture,
            $"{Request.Scheme}://{Request.Host}{Request.PathBase}");

        var body = IptvDocuments.BuildM3u(Config.Channels, baseUrl);
        return Content(body, "application/x-mpegurl");
    }

    /// <summary>
    /// Gets the XMLTV guide for the JellyVision channels.
    /// </summary>
    /// <param name="hours">How many hours of guide to emit.</param>
    /// <returns>The XMLTV document.</returns>
    [HttpGet("epg.xml")]
    [Produces("application/xml")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult GetGuide([FromQuery] int? hours)
    {
        var window = TimeSpan.FromHours(Math.Clamp(hours ?? Config.GuideHours, 1, 168));
        var nowUtc = DateTime.UtcNow;

        var listings = new List<(ChannelConfig, IReadOnlyList<ProgramSlot>)>();
        foreach (var channel in Config.Channels.Where(c => c.Enabled))
        {
            var items = _resolver.Resolve(channel);
            var slots = ScheduleEngine.GetGuide(
                items,
                channel.Mode,
                channel.AnchorUtc,
                nowUtc,
                window,
                ScheduleEngine.SeedFor(channel.Id));

            listings.Add((channel, slots));
        }

        return Content(IptvDocuments.BuildXmltv(listings), "application/xml");
    }

    /// <summary>
    /// Streams a channel as MPEG-TS until the client disconnects.
    /// </summary>
    /// <param name="channelId">The channel id.</param>
    /// <returns>The stream.</returns>
    [HttpGet("stream/{channelId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> GetStream([FromRoute] string channelId)
    {
        var channel = Config.Channels.FirstOrDefault(
            c => string.Equals(c.Id, channelId, StringComparison.OrdinalIgnoreCase));

        if (channel is null || !channel.Enabled)
        {
            return NotFound();
        }

        Response.ContentType = "video/mp2t";
        Response.Headers.CacheControl = "no-cache, no-store";

        await _streamer.StreamAsync(channel, Response.Body, HttpContext.RequestAborted)
            .ConfigureAwait(false);

        return new EmptyResult();
    }
}
