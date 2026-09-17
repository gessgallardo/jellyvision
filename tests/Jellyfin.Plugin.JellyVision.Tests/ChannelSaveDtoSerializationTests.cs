using System.Text.Json;
using System.Text.Json.Serialization;
using Jellyfin.Plugin.JellyVision.Api;
using Jellyfin.Plugin.JellyVision.Configuration;
using Xunit;

namespace Jellyfin.Plugin.JellyVision.Tests;

/// <summary>
/// Jellyfin serialises enums as names, so <c>GET /Plugins/{id}/Configuration</c>
/// returns <c>"Mode": "Sequential"</c>, not <c>0</c>. A UI that round-trips a
/// channel therefore posts names back. When the save DTO typed these as plain
/// ints, deserialisation quietly produced 0 and every edit reset the channel to
/// Sequential - which looked like "the guide never updates".
/// </summary>
public class ChannelSaveDtoSerializationTests
{
    private static JsonSerializerOptions Options()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    [Theory]
    [InlineData("\"RoundRobin\"", ScheduleMode.RoundRobin)]
    [InlineData("\"BlockShuffle\"", ScheduleMode.BlockShuffle)]
    [InlineData("\"Shuffle\"", ScheduleMode.Shuffle)]
    [InlineData("\"Sequential\"", ScheduleMode.Sequential)]
    [InlineData("3", ScheduleMode.RoundRobin)]
    [InlineData("1", ScheduleMode.Shuffle)]
    public void Mode_AcceptsBothNameAndNumber(string json, ScheduleMode expected)
    {
        var dto = JsonSerializer.Deserialize<ChannelSaveDto>(
            $$"""{"Name":"x","Mode":{{json}},"Sources":[]}""", Options());

        Assert.Equal(expected, dto!.Mode);
    }

    [Theory]
    [InlineData("\"Series\"", SourceKind.Series)]
    [InlineData("\"Filter\"", SourceKind.Filter)]
    [InlineData("4", SourceKind.Filter)]
    [InlineData("0", SourceKind.Series)]
    public void SourceKind_AcceptsBothNameAndNumber(string json, SourceKind expected)
    {
        var dto = JsonSerializer.Deserialize<ChannelSaveDto>(
            $$"""{"Name":"x","Sources":[{"Kind":{{json}},"ItemId":"a"}]}""", Options());

        Assert.Equal(expected, dto!.Sources[0].Kind);
    }

    [Fact]
    public void RoundTrip_FromConfigurationShapedJson_PreservesMode()
    {
        // Exactly the shape GET /Plugins/{id}/Configuration hands the UI.
        const string FromServer = """
        {
          "Id": "abc",
          "Number": 1,
          "Name": "Casefile",
          "Enabled": true,
          "Mode": "RoundRobin",
          "LiveWallClock": true,
          "Sources": [ { "Kind": "Series", "ItemId": "s1", "Label": "Dexter" } ]
        }
        """;

        var dto = JsonSerializer.Deserialize<ChannelSaveDto>(FromServer, Options())!;

        Assert.Equal(ScheduleMode.RoundRobin, dto.Mode);
        Assert.Equal(SourceKind.Series, dto.Sources[0].Kind);
        Assert.Equal("Casefile", dto.Name);
    }
}
