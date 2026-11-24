using CommunityToolkit.Mvvm.ComponentModel;

namespace ManualWeatherPlugin.Models;

public sealed class ManualWeatherSettings : ObservableObject
{
    private double _lowTemperature = 12;
    private double _highTemperature = 24;
    private double _lowestTemperatureHour = 5;
    private double _highestTemperatureHour = 14;
    private int _weatherCode;
    private bool _hideTemperatureDecimal;

    public double LowTemperature
    {
        get => _lowTemperature;
        set
        {
            if (!double.IsFinite(value))
            {
                return;
            }

            var normalized = Math.Clamp(value, -60, 60);
            if (!SetProperty(ref _lowTemperature, normalized))
            {
                return;
            }

            if (HighTemperature < normalized)
            {
                HighTemperature = normalized;
            }
        }
    }

    public double HighTemperature
    {
        get => _highTemperature;
        set
        {
            if (!double.IsFinite(value))
            {
                return;
            }

            var normalized = Math.Clamp(value, -60, 60);
            if (!SetProperty(ref _highTemperature, normalized))
            {
                return;
            }

            if (LowTemperature > normalized)
            {
                LowTemperature = normalized;
            }
        }
    }

    public double LowestTemperatureHour
    {
        get => _lowestTemperatureHour;
        set
        {
            if (double.IsFinite(value))
            {
                SetProperty(ref _lowestTemperatureHour, Math.Clamp(value, 0, 23.99));
            }
        }
    }

    public double HighestTemperatureHour
    {
        get => _highestTemperatureHour;
        set
        {
            if (double.IsFinite(value))
            {
                SetProperty(ref _highestTemperatureHour, Math.Clamp(value, 0, 23.99));
            }
        }
    }

    public int WeatherCode
    {
        get => _weatherCode;
        set => SetProperty(ref _weatherCode, value);
    }

    public bool HideTemperatureDecimal
    {
        get => _hideTemperatureDecimal;
        set => SetProperty(ref _hideTemperatureDecimal, value);
    }
}
