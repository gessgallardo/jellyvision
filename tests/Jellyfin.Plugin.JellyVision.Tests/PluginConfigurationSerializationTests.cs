using System;
using System.IO;
using System.Xml.Serialization;
using Jellyfin.Plugin.JellyVision.Configuration;
using Xunit;

namespace Jellyfin.Plugin.JellyVision.Tests;

/// <summary>
/// Jellyfin persists plugin configuration with <see cref="XmlSerializer"/>.
/// It refuses interface-typed and read-only collections at runtime, which
/// takes down every endpoint that touches the configuration - and it only
/// shows up on a real server, never in a plain unit test. These tests pin
/// the shape so the failure is caught at build time instead.
/// </summary>
public class PluginConfigurationSerializationTests
{
    [Fact]
    public void Configuration_RoundTripsThroughXmlSerializer()
    {
        var config = new PluginConfiguration { GuideHours = 12 };
        config.Channels.Add(new ChannelConfig
        {
            Id = "cc1",
            Number = 1,
            Name = "Magical Girl 24/7",
            Mode = ScheduleMode.Sequential,
            LiveWallClock = true,
            AnchorUtc = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Sources =
            [
                new ChannelSource { Kind = SourceKind.Series, ItemId = "abc", Label = "Show" },
                new ChannelSource
                {
                    Kind = SourceKind.Filter,
                    Genres = { "Anime" },
                    Tags = { "retro" },
                },
            ],
        });

        var serializer = new XmlSerializer(typeof(PluginConfiguration));

        using var buffer = new MemoryStream();
        serializer.Serialize(buffer, config);
        buffer.Position = 0;
        var restored = (PluginConfiguration)serializer.Deserialize(buffer)!;

        Assert.Equal(12, restored.GuideHours);
        var channel = Assert.Single(restored.Channels);
        Assert.Equal("cc1", channel.Id);
        Assert.Equal("Magical Girl 24/7", channel.Name);
        Assert.Equal(ScheduleMode.Sequential, channel.Mode);
        Assert.True(channel.LiveWallClock);
        Assert.Equal(2, channel.Sources.Count);
        Assert.Equal("abc", channel.Sources[0].ItemId);
        Assert.Equal("Anime", channel.Sources[1].Genres[0]);
        Assert.Equal("retro", channel.Sources[1].Tags[0]);
    }

    [Fact]
    public void EmptyConfiguration_RoundTrips()
    {
        var serializer = new XmlSerializer(typeof(PluginConfiguration));
        using var buffer = new MemoryStream();
        serializer.Serialize(buffer, new PluginConfiguration());
        buffer.Position = 0;

        var restored = (PluginConfiguration)serializer.Deserialize(buffer)!;
        Assert.Empty(restored.Channels);
        Assert.Equal(24, restored.GuideHours);
    }
}
