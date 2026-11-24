using ClassIsland.Core.Abstractions;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Extensions.Registry;
using ClassIsland.Shared.Helpers;
using ManualWeatherPlugin.Models;
using ManualWeatherPlugin.Services;
using ManualWeatherPlugin.Views.SettingsPages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace ManualWeatherPlugin;

[PluginEntrance]
public class Plugin : PluginBase
{
    public override void Initialize(HostBuilderContext context, IServiceCollection services)
    {
        var settingsPath = Path.Combine(PluginConfigFolder, "Settings.json");
        var settings = ConfigureFileHelper.LoadConfig<ManualWeatherSettings>(settingsPath);
        settings.PropertyChanged += (_, _) =>
            ConfigureFileHelper.SaveConfig(settingsPath, settings, true);

        services.AddSingleton(settings);
        services.RemoveAll<IWeatherService>();
        services.AddSingleton<ManualWeatherService>();
        services.AddSingleton<IWeatherService>(
            provider => provider.GetRequiredService<ManualWeatherService>());
        services.AddSingleton<IHostedService>(
            provider => provider.GetRequiredService<ManualWeatherService>());
        services.AddSettingsPage<ManualWeatherSettingsPage>();
    }
}
