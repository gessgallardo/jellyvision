using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using Jellyfin.Plugin.JellyVision.Configuration;
using Jellyfin.Plugin.JellyVision.LiveTv;
using Jellyfin.Plugin.JellyVision.Scheduling;
using Xunit;

namespace Jellyfin.Plugin.JellyVision.Tests;

public class IptvDocumentsTests
{
    private static readonly DateTime Anchor = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static ChannelConfig Channel(string id = "cc1", int number = 1, bool enabled = true)
        => new()
        {
            Id = id,
            Number = number,
            Name = "Magical Girl 24/7",
            Enabled = enabled,
            AnchorUtc = Anchor,
        };

    private static IReadOnlyList<ScheduleItem> Items() =>
    [
        new("a", "Show - S01E01", TimeSpan.FromMinutes(25), "s1"),
        new("b", "Show - S01E02", TimeSpan.FromMinutes(25), "s1"),
    ];

    [Fact]
    public void M3u_HasHeaderAndOneEntryPerEnabledChannel()
    {
        var channels = new List<ChannelConfig>
        {
            Channel(),
            Channel("cc2", 2),
            Channel("cc3", 3, enabled: false),
        };

        var m3u = IptvDocuments.BuildM3u(channels, "https://host/jellyfin");

        Assert.StartsWith("#EXTM3U", m3u, StringComparison.Ordinal);
        Assert.Equal(2, m3u.Split("#EXTINF").Length - 1);
        Assert.Contains("https://host/jellyfin/JellyVision/iptv/stream/cc1", m3u, StringComparison.Ordinal);
        Assert.Contains("tvg-chno=\"2\"", m3u, StringComparison.Ordinal);
        Assert.DoesNotContain("cc3", m3u, StringComparison.Ordinal);
    }

    [Fact]
    public void M3u_DoesNotDoubleSlashTheBaseUrl()
    {
        var m3u = IptvDocuments.BuildM3u([Channel()], "https://host/jellyfin/");
        Assert.Contains("jellyfin/JellyVision", m3u, StringComparison.Ordinal);
        Assert.DoesNotContain("jellyfin//JellyVision", m3u, StringComparison.Ordinal);
    }

    [Fact]
    public void Xmltv_IsWellFormedWithChannelsAndProgrammes()
    {
        var channel = Channel();
        var slots = ScheduleEngine.GetGuide(
            Items(), ScheduleMode.Sequential, Anchor, Anchor, TimeSpan.FromHours(2), 1);

        var xml = IptvDocuments.BuildXmltv([(channel, slots)]);
        var doc = XDocument.Parse(xml);

        Assert.Equal("tv", doc.Root!.Name.LocalName);

        var channelEl = Assert.Single(doc.Root.Elements("channel"));
        Assert.Equal("jellyvision.cc1", channelEl.Attribute("id")!.Value);
        Assert.Contains(
            channelEl.Elements("display-name"),
            e => e.Value == "Magical Girl 24/7");

        var programmes = doc.Root.Elements("programme").ToList();
        Assert.Equal(slots.Count, programmes.Count);
        Assert.All(programmes, p => Assert.Equal("jellyvision.cc1", p.Attribute("channel")!.Value));
    }

    [Fact]
    public void Xmltv_TimestampsAreXmltvFormatAndOrdered()
    {
        var channel = Channel();
        var slots = ScheduleEngine.GetGuide(
            Items(), ScheduleMode.Sequential, Anchor, Anchor, TimeSpan.FromHours(2), 1);

        var doc = XDocument.Parse(IptvDocuments.BuildXmltv([(channel, slots)]));
        var programmes = doc.Root!.Elements("programme").ToList();

        // XMLTV wants "yyyyMMddHHmmss +0000" - colons in the offset break parsers.
        foreach (var p in programmes)
        {
            var start = p.Attribute("start")!.Value;
            Assert.Matches(@"^\d{14} [+-]\d{4}$", start);
            Assert.DoesNotContain(":", start, StringComparison.Ordinal);
        }

        var starts = programmes
            .Select(p => DateTime.ParseExact(
                p.Attribute("start")!.Value,
                "yyyyMMddHHmmss zzz",
                CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AdjustToUniversal))
            .ToList();

        Assert.Equal(starts.OrderBy(s => s).ToList(), starts);

        // And the stop of one programme is the start of the next: no gaps.
        for (var i = 1; i < programmes.Count; i++)
        {
            Assert.Equal(
                programmes[i - 1].Attribute("stop")!.Value,
                programmes[i].Attribute("start")!.Value);
        }
    }

    [Fact]
    public void Xmltv_EscapesSpecialCharactersInTitles()
    {
        var channel = Channel();
        channel.Name = "Rock & Roll <TV>";
        IReadOnlyList<ScheduleItem> items =
            [new("a", "Tom & Jerry <the best>", TimeSpan.FromMinutes(10), "b")];

        var slots = ScheduleEngine.GetGuide(
            items, ScheduleMode.Sequential, Anchor, Anchor, TimeSpan.FromMinutes(30), 1);

        // Must parse: if escaping were wrong this throws.
        var doc = XDocument.Parse(IptvDocuments.BuildXmltv([(channel, slots)]));
        Assert.Contains(
            doc.Root!.Elements("programme").Elements("title"),
            t => t.Value == "Tom & Jerry <the best>");
    }

    [Fact]
    public void Xmltv_DeclaresUtf8NotUtf16()
    {
        // XmlWriter takes the declared encoding from its underlying writer, and
        // a plain StringWriter reports UTF-16. The response is served as UTF-8
        // bytes, so declaring utf-16 makes strict XMLTV parsers reject the
        // document outright - as Python's ElementTree did against the live server.
        var channel = Channel();
        var slots = ScheduleEngine.GetGuide(
            Items(), ScheduleMode.Sequential, Anchor, Anchor, TimeSpan.FromHours(1), 1);

        var xml = IptvDocuments.BuildXmltv([(channel, slots)]);

        Assert.Contains("encoding=\"utf-8\"", xml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("utf-16", xml, StringComparison.OrdinalIgnoreCase);

        // And it must survive a round trip through real UTF-8 bytes.
        var bytes = System.Text.Encoding.UTF8.GetBytes(xml);
        using var stream = new System.IO.MemoryStream(bytes);
        var parsed = XDocument.Load(stream);
        Assert.Equal("tv", parsed.Root!.Name.LocalName);
    }

    private static IReadOnlyList<ScheduleItem> RichItems() =>
    [
        new(
            "a",
            "Criminal Minds - Derailed",
            TimeSpan.FromMinutes(44),
            "s1",
            "criminal",
            new ScheduleItemMetadata
            {
                SeriesName = "Criminal Minds",
                EpisodeName = "Derailed",
                Overview = "The team negotiates a hostage situation.",
                SeasonNumber = 1,
                EpisodeNumber = 9,
                Year = 2005,
                PrimaryImageItemId = "ep111",
                SeriesImageItemId = "ser222",
                Genres = { "Drama", "Crime" },
            }),
    ];

    [Fact]
    public void Xmltv_CarriesArtworkAndEpisodeMetadata()
    {
        var channel = Channel();
        var slots = ScheduleEngine.GetGuide(
            RichItems(), ScheduleMode.Sequential, Anchor, Anchor, TimeSpan.FromMinutes(40), 1);

        var doc = XDocument.Parse(
            IptvDocuments.BuildXmltv([(channel, slots)], "https://host/jellyfin"));
        var programme = doc.Root!.Elements("programme").First();

        // Title is the series; the episode name goes in sub-title, so clients
        // stop showing a raw timestamp as the second line.
        Assert.Equal("Criminal Minds", programme.Element("title")!.Value);
        Assert.Equal("Derailed", programme.Element("sub-title")!.Value);
        Assert.Equal("The team negotiates a hostage situation.", programme.Element("desc")!.Value);

        // <icon src> is what Jellyfin maps to the programme tile image.
        var icon = programme.Element("icon")!.Attribute("src")!.Value;
        Assert.Equal("https://host/jellyfin/Items/ep111/Images/Primary?maxWidth=600", icon);

        Assert.Contains(
            programme.Elements("image"),
            i => i.Attribute("type")!.Value == "backdrop" && i.Value.Contains("ser222", StringComparison.Ordinal));

        Assert.Contains(
            programme.Elements("episode-num"),
            e => e.Attribute("system")!.Value == "onscreen" && e.Value == "S01E09");
        Assert.Contains(
            programme.Elements("episode-num"),
            e => e.Attribute("system")!.Value == "xmltv_ns" && e.Value == "0.8.");

        Assert.Contains(programme.Elements("category"), c => c.Value == "Drama");
        Assert.Equal("2005", programme.Element("date")!.Value);
    }

    [Fact]
    public void Xmltv_SubTitleIsNeverARawTimestamp()
    {
        // Regression: the old dedupe hack wrote the start stamp into sub-title,
        // which surfaced as "20260917204559 +0000" under every guide tile.
        var channel = Channel();
        var slots = ScheduleEngine.GetGuide(
            RichItems(), ScheduleMode.Sequential, Anchor, Anchor, TimeSpan.FromHours(2), 1);

        var doc = XDocument.Parse(
            IptvDocuments.BuildXmltv([(channel, slots)], "https://host"));

        foreach (var sub in doc.Root!.Elements("programme").Elements("sub-title"))
        {
            Assert.DoesNotMatch(@"^\d{14}", sub.Value);
        }
    }

    [Fact]
    public void Xmltv_WithoutBaseUrl_OmitsArtworkButStaysValid()
    {
        var channel = Channel();
        var slots = ScheduleEngine.GetGuide(
            RichItems(), ScheduleMode.Sequential, Anchor, Anchor, TimeSpan.FromMinutes(40), 1);

        var doc = XDocument.Parse(IptvDocuments.BuildXmltv([(channel, slots)]));
        var programme = doc.Root!.Elements("programme").First();

        Assert.Null(programme.Element("icon"));
        Assert.Equal("Criminal Minds", programme.Element("title")!.Value);
    }

    [Fact]
    public void Xmltv_ChannelCarriesALogo()
    {
        var channel = Channel();
        var slots = ScheduleEngine.GetGuide(
            RichItems(), ScheduleMode.Sequential, Anchor, Anchor, TimeSpan.FromMinutes(40), 1);

        var doc = XDocument.Parse(
            IptvDocuments.BuildXmltv([(channel, slots)], "https://host"));

        var icon = doc.Root!.Element("channel")!.Element("icon");
        Assert.NotNull(icon);
        Assert.Contains("ser222", icon!.Attribute("src")!.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void Xmltv_MovieUsesItsOwnTitleAndNoSubTitle()
    {
        IReadOnlyList<ScheduleItem> items =
        [
            new(
                "m1",
                "Blade Runner",
                TimeSpan.FromMinutes(117),
                "m1",
                "m1",
                new ScheduleItemMetadata
                {
                    EpisodeName = "Blade Runner",
                    IsMovie = true,
                    PrimaryImageItemId = "mov1",
                    SeriesImageItemId = "mov1",
                }),
        ];

        var slots = ScheduleEngine.GetGuide(
            items, ScheduleMode.Sequential, Anchor, Anchor, TimeSpan.FromMinutes(60), 1);

        var doc = XDocument.Parse(IptvDocuments.BuildXmltv([(Channel(), slots)], "https://host"));
        var programme = doc.Root!.Elements("programme").First();

        Assert.Equal("Blade Runner", programme.Element("title")!.Value);
        Assert.Null(programme.Element("sub-title"));
    }

    [Fact]
    public void Xmltv_EmptyChannelListStillParses()
    {
        var doc = XDocument.Parse(IptvDocuments.BuildXmltv([]));
        Assert.Equal("tv", doc.Root!.Name.LocalName);
        Assert.Empty(doc.Root.Elements("programme"));
    }
}
