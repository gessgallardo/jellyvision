using System;
using System.Collections.Generic;
using System.Globalization;
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
    /// <returns>The XMLTV body.</returns>
    public static string BuildXmltv(
        IReadOnlyList<(ChannelConfig Channel, IReadOnlyList<ProgramSlot> Slots)> programmes)
    {
        ArgumentNullException.ThrowIfNull(programmes);

        var settings = new XmlWriterSettings
        {
            Indent = true,
            Encoding = new UTF8Encoding(false),
            OmitXmlDeclaration = false,
        };

        var sb = new StringBuilder();
        using (var writer = XmlWriter.Create(sb, settings))
        {
            writer.WriteStartDocument();
            writer.WriteStartElement("tv");
            writer.WriteAttributeString("generator-info-name", "JellyVision");

            foreach (var (channel, _) in programmes)
            {
                writer.WriteStartElement("channel");
                writer.WriteAttributeString("id", ChannelTvgId(channel));

                writer.WriteStartElement("display-name");
                writer.WriteString(channel.Name);
                writer.WriteEndElement();

                writer.WriteStartElement("display-name");
                writer.WriteString(channel.Number.ToString(CultureInfo.InvariantCulture));
                writer.WriteEndElement();

                writer.WriteEndElement();
            }

            foreach (var (channel, slots) in programmes)
            {
                foreach (var slot in slots)
                {
                    writer.WriteStartElement("programme");
                    writer.WriteAttributeString("start", Stamp(slot.StartUtc));
                    writer.WriteAttributeString("stop", Stamp(slot.EndUtc));
                    writer.WriteAttributeString("channel", ChannelTvgId(channel));

                    writer.WriteStartElement("title");
                    writer.WriteAttributeString("lang", "en");
                    writer.WriteString(slot.Item.Title);
                    writer.WriteEndElement();

                    // Jellyfin dedupes guide entries by title; without a unique
                    // sub-title, repeats of the same episode collapse in the UI.
                    writer.WriteStartElement("sub-title");
                    writer.WriteAttributeString("lang", "en");
                    writer.WriteString(Stamp(slot.StartUtc));
                    writer.WriteEndElement();

                    writer.WriteEndElement();
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

    private static string Stamp(DateTime utc)
        => utc.ToUniversalTime().ToString(XmltvTimeFormat, CultureInfo.InvariantCulture)
              .Replace(":", string.Empty, StringComparison.Ordinal);

    private static string Escape(string value)
        => value.Replace("\"", "'", StringComparison.Ordinal);
}
