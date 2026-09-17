using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using Jellyfin.Plugin.JellyVision.Configuration;
using Jellyfin.Plugin.JellyVision.Scheduling;

namespace Jellyfin.Plugin.JellyVision.LiveTv;

/// <summary>
/// Renders the M3U playlist and XMLTV guide that Jellyfin's Live TV consumes.
/// </summary>
public static class IptvDocuments
{
    private const string XmltvTimeFormat = "yyyyMMddHHmmss zzz";

    /// <summary>
    /// Builds the M3U playlist for the enabled channels.
    /// </summary>
    /// <param name="channels">The configured channels.</param>
    /// <param name="baseUrl">The absolute base URL clients should call back on.</param>
    /// <returns>The playlist body.</returns>
    public static string BuildM3u(IEnumerable<ChannelConfig> channels, string baseUrl)
    {
        ArgumentNullException.ThrowIfNull(channels);

        var sb = new StringBuilder();
        sb.Append("#EXTM3U\n");

        foreach (var channel in channels)
        {
            if (!channel.Enabled)
            {
                continue;
            }

            var id = ChannelTvgId(channel);
            sb.Append(CultureInfo.InvariantCulture, $"#EXTINF:-1 tvg-id=\"{id}\"")
              .Append(CultureInfo.InvariantCulture, $" tvg-name=\"{Escape(channel.Name)}\"")
              .Append(CultureInfo.InvariantCulture, $" tvg-chno=\"{channel.Number}\"")
              .Append(CultureInfo.InvariantCulture, $" group-title=\"JellyVision\",{Escape(channel.Name)}\n")
              .Append(CultureInfo.InvariantCulture, $"{baseUrl.TrimEnd('/')}/JellyVision/iptv/stream/{channel.Id}\n");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Builds the XMLTV guide document.
    /// </summary>
    /// <param name="programmes">Per-channel programme listings.</param>
    /// <param name="baseUrl">Absolute base URL used to build artwork links.</param>
    /// <returns>The XMLTV body.</returns>
    public static string BuildXmltv(
        IReadOnlyList<(ChannelConfig Channel, IReadOnlyList<ProgramSlot> Slots)> programmes,
        string baseUrl = "")
    {
        ArgumentNullException.ThrowIfNull(programmes);

        var settings = new XmlWriterSettings
        {
            Indent = true,
            Encoding = new UTF8Encoding(false),
            OmitXmlDeclaration = false,
        };

        var sb = new StringBuilder();
        using (var stringWriter = new Utf8StringWriter(sb))
        using (var writer = XmlWriter.Create(stringWriter, settings))
        {
            writer.WriteStartDocument();
            writer.WriteStartElement("tv");
            writer.WriteAttributeString("generator-info-name", "JellyVision");

            foreach (var (channel, slots) in programmes)
            {
                writer.WriteStartElement("channel");
                writer.WriteAttributeString("id", ChannelTvgId(channel));

                writer.WriteStartElement("display-name");
                writer.WriteString(channel.Name);
                writer.WriteEndElement();

                writer.WriteStartElement("display-name");
                writer.WriteString(channel.Number.ToString(CultureInfo.InvariantCulture));
                writer.WriteEndElement();

                // Without an icon the channel tile is a blank placeholder, so
                // borrow the artwork of whatever it is showing first.
                var logoId = slots
                    .Select(s => s.Item.Metadata?.SeriesImageItemId)
                    .FirstOrDefault(id => !string.IsNullOrEmpty(id));

                if (!string.IsNullOrEmpty(baseUrl) && !string.IsNullOrEmpty(logoId))
                {
                    writer.WriteStartElement("icon");
                    writer.WriteAttributeString(
                        "src", ImageUrl(baseUrl.TrimEnd('/'), logoId, "Primary", 400));
                    writer.WriteEndElement();
                }

                writer.WriteEndElement();
            }

            foreach (var (channel, slots) in programmes)
            {
                foreach (var slot in slots)
                {
                    WriteProgramme(writer, channel, slot, baseUrl);
                }
            }

            writer.WriteEndElement();
            writer.WriteEndDocument();
        }

        return sb.ToString();
    }

    /// <summary>
    /// Builds the stable XMLTV channel identifier for a channel.
    /// </summary>
    /// <param name="channel">The channel.</param>
    /// <returns>The identifier.</returns>
    public static string ChannelTvgId(ChannelConfig channel)
    {
        ArgumentNullException.ThrowIfNull(channel);
        return string.Create(CultureInfo.InvariantCulture, $"jellyvision.{channel.Id}");
    }

    private static void WriteProgramme(
        XmlWriter writer, ChannelConfig channel, ProgramSlot slot, string baseUrl)
    {
        var meta = slot.Item.Metadata;

        writer.WriteStartElement("programme");
        writer.WriteAttributeString("start", Stamp(slot.StartUtc));
        writer.WriteAttributeString("stop", Stamp(slot.EndUtc));
        writer.WriteAttributeString("channel", ChannelTvgId(channel));

        // Title is the series (or movie) name; the episode name belongs in
        // sub-title, which is what clients render as the second line.
        var title = meta is null || string.IsNullOrEmpty(meta.SeriesName)
            ? slot.Item.Title
            : meta.SeriesName;

        writer.WriteStartElement("title");
        writer.WriteAttributeString("lang", "en");
        writer.WriteString(title);
        writer.WriteEndElement();

        if (meta is not null && !string.IsNullOrEmpty(meta.SeriesName) &&
            !string.IsNullOrEmpty(meta.EpisodeName))
        {
            writer.WriteStartElement("sub-title");
            writer.WriteAttributeString("lang", "en");
            writer.WriteString(meta.EpisodeName);
            writer.WriteEndElement();
        }

        if (meta is not null && !string.IsNullOrEmpty(meta.Overview))
        {
            writer.WriteStartElement("desc");
            writer.WriteAttributeString("lang", "en");
            writer.WriteString(meta.Overview);
            writer.WriteEndElement();
        }

        if (meta is not null)
        {
            foreach (var genre in meta.Genres)
            {
                writer.WriteStartElement("category");
                writer.WriteAttributeString("lang", "en");
                writer.WriteString(genre);
                writer.WriteEndElement();
            }

            if (meta.SeasonNumber.HasValue && meta.EpisodeNumber.HasValue)
            {
                // xmltv_ns counts from zero and Jellyfin uses this to decide a
                // programme is part of a series.
                writer.WriteStartElement("episode-num");
                writer.WriteAttributeString("system", "xmltv_ns");
                writer.WriteString(string.Create(
                    CultureInfo.InvariantCulture,
                    $"{meta.SeasonNumber.Value - 1}.{meta.EpisodeNumber.Value - 1}."));
                writer.WriteEndElement();

                writer.WriteStartElement("episode-num");
                writer.WriteAttributeString("system", "onscreen");
                writer.WriteString(string.Create(
                    CultureInfo.InvariantCulture,
                    $"S{meta.SeasonNumber.Value:00}E{meta.EpisodeNumber.Value:00}"));
                writer.WriteEndElement();
            }

            if (meta.Year.HasValue)
            {
                writer.WriteStartElement("date");
                writer.WriteString(meta.Year.Value.ToString(CultureInfo.InvariantCulture));
                writer.WriteEndElement();
            }

            WriteArtwork(writer, meta, baseUrl);
        }

        writer.WriteEndElement();
    }

    private static void WriteArtwork(XmlWriter writer, ScheduleItemMetadata meta, string baseUrl)
    {
        if (string.IsNullOrEmpty(baseUrl))
        {
            return;
        }

        var root = baseUrl.TrimEnd('/');

        // <icon> is what Jellyfin maps to ProgramInfo.ImageUrl, which is the
        // tile artwork in the guide. Prefer the episode still, fall back to the
        // series poster so a programme is never left with a blank tile.
        var iconId = !string.IsNullOrEmpty(meta.PrimaryImageItemId)
            ? meta.PrimaryImageItemId
            : meta.SeriesImageItemId;

        if (!string.IsNullOrEmpty(iconId))
        {
            writer.WriteStartElement("icon");
            writer.WriteAttributeString("src", ImageUrl(root, iconId, "Primary", 600));
            writer.WriteEndElement();
        }

        if (!string.IsNullOrEmpty(meta.SeriesImageItemId))
        {
            writer.WriteStartElement("image");
            writer.WriteAttributeString("type", "backdrop");
            writer.WriteString(ImageUrl(root, meta.SeriesImageItemId, "Backdrop", 1280));
            writer.WriteEndElement();
        }

        if (!string.IsNullOrEmpty(meta.PrimaryImageItemId))
        {
            writer.WriteStartElement("image");
            writer.WriteAttributeString("type", "still");
            writer.WriteString(ImageUrl(root, meta.PrimaryImageItemId, "Primary", 600));
            writer.WriteEndElement();
        }
    }

    private static string ImageUrl(string root, string itemId, string type, int maxWidth)
        => string.Create(
            CultureInfo.InvariantCulture,
            $"{root}/Items/{itemId}/Images/{type}?maxWidth={maxWidth}");

    private static string Stamp(DateTime utc)
        => utc.ToUniversalTime().ToString(XmltvTimeFormat, CultureInfo.InvariantCulture)
              .Replace(":", string.Empty, StringComparison.Ordinal);

    private static string Escape(string value)
        => value.Replace("\"", "'", StringComparison.Ordinal);

    /// <summary>
    /// A <see cref="StringWriter"/> that reports UTF-8.
    /// </summary>
    /// <remarks>
    /// <see cref="XmlWriter"/> takes the encoding for the XML declaration from
    /// the writer, and a plain <see cref="StringWriter"/> reports UTF-16. That
    /// produces <c>encoding="utf-16"</c> on a response served as UTF-8 bytes,
    /// which strict XMLTV parsers reject outright.
    /// </remarks>
    private sealed class Utf8StringWriter : StringWriter
    {
        public Utf8StringWriter(StringBuilder sb)
            : base(sb, CultureInfo.InvariantCulture)
        {
        }

        public override Encoding Encoding => Encoding.UTF8;
    }
}
