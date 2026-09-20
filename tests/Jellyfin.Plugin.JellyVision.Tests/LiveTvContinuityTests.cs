using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyVision.LiveTv;
using Xunit;

namespace Jellyfin.Plugin.JellyVision.Tests;

/// <summary>
/// Exercises the actual ffmpeg concat boundary used by the Live TV streamer.
/// </summary>
public sealed class LiveTvContinuityTests
{
    [Fact]
    public async Task FfmpegContinuesWithTheNextChapterAtTheScheduledBoundary()
    {
        var ffmpeg = Environment.GetEnvironmentVariable("FFMPEG_PATH") ?? "ffmpeg";
        var directory = Path.Combine(Path.GetTempPath(), $"jellyvision-continuity-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        try
        {
            var first = Path.Combine(directory, "chapter-1.mp4");
            var second = Path.Combine(directory, "chapter-2.mp4");
            await CreateSolidColorChapter(ffmpeg, first, "red");
            await CreateSolidColorChapter(ffmpeg, second, "blue");

            var concat = ConcatListBuilder.Build(
            [
                (first, TimeSpan.FromSeconds(1), TimeSpan.Zero),
                (second, TimeSpan.FromSeconds(1), TimeSpan.Zero),
            ]);
            var listPath = Path.Combine(directory, "chapters.ffconcat");
            await File.WriteAllTextAsync(listPath, "ffconcat version 1.0\n" + concat.Text);

            var output = await RunFfmpeg(
                ffmpeg,
                $"-hide_banner -loglevel error -nostdin -f concat -safe 0 -i \"{listPath}\" " +
                "-map 0:v:0 -an -f rawvideo -pix_fmt rgb24 pipe:1");

            const int frameWidth = 32;
            const int frameHeight = 32;
            const int bytesPerFrame = frameWidth * frameHeight * 3;
            var frames = output.Length / bytesPerFrame;

            Assert.Equal(20, frames);
            Assert.True(IsMostlyRed(output, 0, bytesPerFrame), "the first chapter should be present");
            Assert.True(
                IsMostlyBlue(output, output.Length - bytesPerFrame, bytesPerFrame),
                "the next chapter should continue after the first chapter ends");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task CreateSolidColorChapter(string ffmpeg, string path, string color)
    {
        await RunFfmpeg(
            ffmpeg,
            string.Create(
                CultureInfo.InvariantCulture,
                $"-hide_banner -loglevel error -nostdin -f lavfi -i color=c={color}:s=32x32:r=10:d=1 " +
                $"-c:v libx264 -pix_fmt yuv420p -frames:v 10 \"{path}\""));
    }

    private static async Task<byte[]> RunFfmpeg(string ffmpeg, string arguments)
    {
        var startInfo = new ProcessStartInfo(ffmpeg, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start ffmpeg.");
        var standardError = process.StandardError.ReadToEndAsync();
        await using var standardOutput = new MemoryStream();
        await process.StandardOutput.BaseStream.CopyToAsync(standardOutput);
        await process.WaitForExitAsync();
        var error = await standardError;

        Assert.True(process.ExitCode == 0, $"ffmpeg failed with exit code {process.ExitCode}: {error}");
        return standardOutput.ToArray();
    }

    private static bool IsMostlyRed(byte[] bytes, int offset, int length)
        => AverageChannel(bytes, offset, length, 0) > 180
            && AverageChannel(bytes, offset, length, 1) < 80
            && AverageChannel(bytes, offset, length, 2) < 80;

    private static bool IsMostlyBlue(byte[] bytes, int offset, int length)
        => AverageChannel(bytes, offset, length, 0) < 80
            && AverageChannel(bytes, offset, length, 1) < 80
            && AverageChannel(bytes, offset, length, 2) > 140;

    private static double AverageChannel(byte[] bytes, int offset, int length, int channel)
        => Enumerable.Range(0, length / 3)
            .Average(i => bytes[offset + (i * 3) + channel]);
}
