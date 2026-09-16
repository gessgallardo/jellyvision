using Jellyfin.Plugin.JellyVision.LiveTv;
using Jellyfin.Plugin.JellyVision.Scheduling;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.JellyVision;

/// <summary>
/// Registers JellyVision services with the host container.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<ChannelResolver>();
        serviceCollection.AddSingleton<ChannelStreamer>();
    }
}
