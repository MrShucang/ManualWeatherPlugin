using System.ComponentModel;
using Avalonia.Interactivity;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
using ClassIsland.Shared;
using ManualWeatherPlugin.Models;
using ManualWeatherPlugin.Services;

namespace ManualWeatherPlugin.Views.SettingsPages;

[SettingsPageInfo(
    "classisland.manualweather.settings",
    "手动天气",
    "\uE4DC",
    "\uE4DC")]
public partial class ManualWeatherSettingsPage : SettingsPageBase, INotifyPropertyChanged
{
    public ManualWeatherSettings Settings { get; } =
        IAppHost.GetService<ManualWeatherSettings>();

    public ManualWeatherService WeatherService { get; } =
        IAppHost.GetService<ManualWeatherService>();

    public IReadOnlyList<double> PreviewTemperatures =>
        WeatherService.GetHourlyTemperatures(DateTime.Today);

    public string TemperatureRangeText =>
        $"最低 {FormatTemperature(Math.Min(Settings.LowTemperature, Settings.HighTemperature))}°C  ·  " +
        $"最高 {FormatTemperature(Math.Max(Settings.LowTemperature, Settings.HighTemperature))}°C";

    public new event PropertyChangedEventHandler? PropertyChanged;

    public ManualWeatherSettingsPage()
    {
        InitializeComponent();
        DataContext = this;
        Settings.PropertyChanged += SettingsOnPropertyChanged;
        Unloaded += (_, _) => Settings.PropertyChanged -= SettingsOnPropertyChanged;
    }

    private void SettingsOnPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is not (
            nameof(ManualWeatherSettings.LowTemperature) or
            nameof(ManualWeatherSettings.HighTemperature) or
            nameof(ManualWeatherSettings.LowestTemperatureHour) or
            nameof(ManualWeatherSettings.HighestTemperatureHour) or
            nameof(ManualWeatherSettings.HideTemperatureDecimal)))
        {
            return;
        }

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PreviewTemperatures)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TemperatureRangeText)));
    }

    private async void ApplyButton_OnClick(object? sender, RoutedEventArgs e)
    {
        await WeatherService.QueryWeatherAsync();
    }

    private string FormatTemperature(double value)
    {
        return Settings.HideTemperatureDecimal
            ? Math.Round(value, MidpointRounding.AwayFromZero).ToString("0")
            : value.ToString("0.#");
    }
}
