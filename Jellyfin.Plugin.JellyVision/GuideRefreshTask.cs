using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyVision;

/// <summary>
/// Keeps Jellyfin's cached XMLTV guide rolling for always-on channels.
/// </summary>
public sealed class GuideRefreshTask : IScheduledTask
{
    private readonly ITaskManager _taskManager;
    private readonly IApplicationPaths _appPaths;
    private readonly ILogger<GuideRefreshTask> _logger;

    /// <summary>Initializes a new instance of the <see cref="GuideRefreshTask"/> class.</summary>
    /// <param name="taskManager">The Jellyfin task manager.</param>
    /// <param name="appPaths">The Jellyfin application paths.</param>
    /// <param name="logger">The task logger.</param>
    public GuideRefreshTask(
        ITaskManager taskManager,
        IApplicationPaths appPaths,
        ILogger<GuideRefreshTask> logger)
    {
        _taskManager = taskManager;
        _appPaths = appPaths;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Refresh JellyVision XMLTV guide";

    /// <inheritdoc />
    public string Key => "JellyVisionGuideRefresh";

    /// <inheritdoc />
    public string Description => "Refreshes the rolling JellyVision Live TV guide.";

    /// <inheritdoc />
    public string Category => "JellyVision";

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        =>
        [
            new TaskTriggerInfo
            {
                Type = TaskTriggerInfoType.StartupTrigger,
            },
            new TaskTriggerInfo
            {
                Type = TaskTriggerInfoType.IntervalTrigger,
                IntervalTicks = TimeSpan.FromHours(1).Ticks,
            },
        ];

    /// <inheritdoc />
    public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DropXmltvCache();

        var refresh = _taskManager.ScheduledTasks.FirstOrDefault(
            t => string.Equals(t.ScheduledTask.Key, "RefreshGuide", StringComparison.Ordinal));
        if (refresh is not null)
        {
            _taskManager.Execute(refresh, new TaskOptions());
        }
        else
        {
            _logger.LogWarning("Jellyfin RefreshGuide task is unavailable");
        }

        progress?.Report(100);
        return Task.CompletedTask;
    }

    private void DropXmltvCache()
    {
        try
        {
            var directory = Path.Combine(_appPaths.CachePath, "xmltv");
            if (!Directory.Exists(directory))
            {
                return;
            }

            foreach (var file in Directory.EnumerateFiles(directory, "*.xml"))
            {
                try
                {
                    File.Delete(file);
                }
                catch (IOException ex)
                {
                    _logger.LogDebug(ex, "Could not delete cached guide {File}", file);
                }
                catch (UnauthorizedAccessException ex)
                {
                    _logger.LogDebug(ex, "Could not delete cached guide {File}", file);
                }
            }
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Could not clear the XMLTV cache");
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Could not clear the XMLTV cache");
        }
    }
}
