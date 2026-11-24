using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using Avalonia.Controls.Templates;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Core.Models.Weather;
using ClassIsland.Shared;
using CommunityToolkit.Mvvm.ComponentModel;
using ManualWeatherPlugin.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ManualWeatherPlugin.Services;

public sealed class ManualWeatherService : ObservableObject, IWeatherService, IHostedService
{
    private static readonly IReadOnlyList<XiaomiWeatherStatusCodeItem> StandardWeatherStatuses =
    [
        new() { Code = 0, Weather = "晴" },
        new() { Code = 1, Weather = "多云" },
        new() { Code = 2, Weather = "阴" },
        new() { Code = 3, Weather = "阵雨" },
        new() { Code = 4, Weather = "雷阵雨" },
        new() { Code = 5, Weather = "雷阵雨并伴有冰雹" },
        new() { Code = 6, Weather = "雨夹雪" },
        new() { Code = 7, Weather = "小雨" },
        new() { Code = 8, Weather = "中雨" },
        new() { Code = 9, Weather = "大雨" },
        new() { Code = 10, Weather = "暴雨" },
        new() { Code = 11, Weather = "大暴雨" },
        new() { Code = 12, Weather = "特大暴雨" },
        new() { Code = 13, Weather = "阵雪" },
        new() { Code = 14, Weather = "小雪" },
        new() { Code = 15, Weather = "中雪" },
        new() { Code = 16, Weather = "大雪" },
        new() { Code = 17, Weather = "暴雪" },
        new() { Code = 18, Weather = "雾" },
        new() { Code = 19, Weather = "冻雨" },
        new() { Code = 20, Weather = "沙尘暴" },
        new() { Code = 21, Weather = "小雨转中雨" },
        new() { Code = 22, Weather = "中雨转大雨" },
        new() { Code = 23, Weather = "大雨转暴雨" },
        new() { Code = 24, Weather = "暴雨转大暴雨" },
        new() { Code = 25, Weather = "大暴雨转特大暴雨" },
        new() { Code = 26, Weather = "小雪转中雪" },
        new() { Code = 27, Weather = "中雪转大雪" },
        new() { Code = 28, Weather = "大雪转暴雪" },
        new() { Code = 29, Weather = "浮尘" },
        new() { Code = 30, Weather = "扬沙" },
        new() { Code = 31, Weather = "强沙尘暴" },
        new() { Code = 32, Weather = "风" },
        new() { Code = 33, Weather = "龙卷风" },
        new() { Code = 34, Weather = "弱高吹雪" },
        new() { Code = 35, Weather = "轻雾" },
        new() { Code = 53, Weather = "霾" },
        new() { Code = 99, Weather = "未知" }
    ];

    private readonly ManualWeatherSettings _settings;
    private readonly IRulesetService _rulesetService;
    private readonly ILogger<ManualWeatherService> _logger;
    private bool _isWeatherRefreshed;
    private bool _settingsSubscriptionActive = true;
    private bool _appSettingsBound;
    private WeatherInfo? _lastWeatherInfo;
    private IDataTemplate? _selectedWeatherIconTemplate;
    private object? _appSettingsService;
    private object? _appSettings;
    private PropertyInfo? _appLastWeatherInfoProperty;
    private PropertyInfo? _appWeatherIconProperty;
    private INotifyPropertyChanged? _appSettingsNotifier;

    public ManualWeatherService(
        ManualWeatherSettings settings,
        IRulesetService rulesetService,
        ILogger<ManualWeatherService> logger)
    {
        _settings = settings;
        _rulesetService = rulesetService;
        _logger = logger;
        _settings.PropertyChanged += SettingsOnPropertyChanged;
        _rulesetService.RegisterRuleHandler(
            "classisland.weather.currentWeather",
            CurrentWeatherRuleHandler);
        _rulesetService.RegisterRuleHandler(
            "classisland.weather.tomorrowWeather",
            TomorrowWeatherRuleHandler);
        _rulesetService.RegisterRuleHandler("classisland.weather.hasWeatherAlert", _ => false);
        _rulesetService.RegisterRuleHandler("classisland.weather.rainTime", _ => false);
        ApplyWeatherData();
    }

    public List<XiaomiWeatherStatusCodeItem> WeatherStatusList { get; set; } =
        StandardWeatherStatuses.ToList();

    public bool IsWeatherRefreshed
    {
        get => _isWeatherRefreshed;
        set => SetProperty(ref _isWeatherRefreshed, value);
    }

    public WeatherInfo? LastWeatherInfo
    {
        get => _lastWeatherInfo;
        private set => SetProperty(ref _lastWeatherInfo, value);
    }

    public IDataTemplate? SelectedWeatherIconTemplate
    {
        get => _selectedWeatherIconTemplate;
        private set => SetProperty(ref _selectedWeatherIconTemplate, value);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        BindApplicationSettings();
        ApplyWeatherData();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        if (_settingsSubscriptionActive)
        {
            _settings.PropertyChanged -= SettingsOnPropertyChanged;
            _settingsSubscriptionActive = false;
        }

        if (_appSettingsNotifier != null)
        {
            _appSettingsNotifier.PropertyChanged -= AppSettingsOnPropertyChanged;
        }

        return Task.CompletedTask;
    }

    public Task QueryWeatherAsync()
    {
        ApplyWeatherData();
        return Task.CompletedTask;
    }

    public string GetWeatherTextByCode(string code)
    {
        return WeatherStatusList.FirstOrDefault(item =>
            item.Code.ToString(CultureInfo.InvariantCulture) == code)?.Weather ?? "未知";
    }

    public Task<List<City>> GetCitiesByName(string name)
    {
        return Task.FromResult(new List<City>
        {
            new() { Name = "手动天气", CityId = "manual" }
        });
    }

    public IReadOnlyList<double> GetHourlyTemperatures(DateTime start, int count = 24)
    {
        if (count <= 0)
        {
            return [];
        }

        var values = new double[count];
        for (var index = 0; index < count; index++)
        {
            values[index] = CalculateTemperature(start.AddHours(index));
        }

        return values;
    }

    public double CalculateTemperature(DateTime time)
    {
        var low = Math.Min(_settings.LowTemperature, _settings.HighTemperature);
        var high = Math.Max(_settings.LowTemperature, _settings.HighTemperature);
        var lowHour = _settings.LowestTemperatureHour;
        var highHour = _settings.HighestTemperatureHour;
        var warmingHours = PositiveModulo(highHour - lowHour, 24);

        if (warmingHours < 0.5 || warmingHours > 23.5)
        {
            warmingHours = 9;
        }

        var currentHour = time.TimeOfDay.TotalHours;
        var elapsed = PositiveModulo(currentHour - lowHour, 24);

        if (elapsed <= warmingHours)
        {
            var factor = (1 - Math.Cos(Math.PI * elapsed / warmingHours)) / 2;
            return Math.Round(low + (high - low) * factor, 1);
        }

        var coolingHours = 24 - warmingHours;
        var coolingFactor =
            (1 - Math.Cos(Math.PI * (elapsed - warmingHours) / coolingHours)) / 2;
        return Math.Round(high + (low - high) * coolingFactor, 1);
    }

    private void SettingsOnPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is not (
            nameof(ManualWeatherSettings.LowTemperature) or
            nameof(ManualWeatherSettings.HighTemperature) or
            nameof(ManualWeatherSettings.LowestTemperatureHour) or
            nameof(ManualWeatherSettings.HighestTemperatureHour) or
            nameof(ManualWeatherSettings.WeatherCode) or
            nameof(ManualWeatherSettings.HideTemperatureDecimal)))
        {
            return;
        }

        ApplyWeatherData();
    }

    private bool CurrentWeatherRuleHandler(object? settings)
    {
        if (settings == null || LastWeatherInfo == null)
        {
            return false;
        }

        var settingsType = settings.GetType();
        var target = settingsType.GetProperty("WeatherId")?.GetValue(settings)?.ToString();
        if (target == null)
        {
            return false;
        }

        var fuzzy = settingsType.GetProperty("IsFuzzyMatch")?.GetValue(settings) is true;
        var actual = LastWeatherInfo.Current.Weather;
        return fuzzy ? IsWeatherCodeMatched(actual, target) : actual == target;
    }

    private bool TomorrowWeatherRuleHandler(object? settings)
    {
        var forecast = LastWeatherInfo?.ForecastDaily.Weather.Value;
        if (settings == null || forecast == null || forecast.Count <= 1)
        {
            return false;
        }

        var settingsType = settings.GetType();
        var target = settingsType.GetProperty("WeatherId")?.GetValue(settings)?.ToString();
        if (target == null)
        {
            return false;
        }

        var fuzzy = settingsType.GetProperty("IsFuzzyMatch")?.GetValue(settings) is true;
        var tomorrow = forecast[1];
        return fuzzy
            ? IsWeatherCodeMatched(tomorrow.From, target) ||
              IsWeatherCodeMatched(tomorrow.To, target)
            : tomorrow.From == target || tomorrow.To == target;
    }

    private void ApplyWeatherData()
    {
        var now = DateTime.Now;
        var weatherCode = _settings.WeatherCode.ToString(CultureInfo.InvariantCulture);
        var hourlyStart = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0);
        var hourlyTemperatures = GetHourlyTemperatures(hourlyStart)
            .Select(value => (int)Math.Round(value))
            .ToList();

        var low = Math.Round(Math.Min(_settings.LowTemperature, _settings.HighTemperature), 1);
        var high = Math.Round(Math.Max(_settings.LowTemperature, _settings.HighTemperature), 1);
        var dailyTemperatures = Enumerable.Range(0, 15)
            .Select(_ => new RangedValue
            {
                From = FormatTemperature(low),
                To = FormatTemperature(high)
            })
            .ToList();
        var dailyWeather = Enumerable.Range(0, 15)
            .Select(_ => new RangedValue { From = weatherCode, To = weatherCode })
            .ToList();
        var sunRiseSet = Enumerable.Range(0, 15)
            .Select(day =>
            {
                var date = now.Date.AddDays(day);
                return new RangedValue
                {
                    From = new DateTimeOffset(date.AddHours(6))
                        .ToString("O", CultureInfo.InvariantCulture),
                    To = new DateTimeOffset(date.AddHours(18))
                        .ToString("O", CultureInfo.InvariantCulture)
                };
            })
            .ToList();

        var currentTemperature = FormatTemperature(CalculateTemperature(now));

        var weatherInfo = new WeatherInfo
        {
            Current = new CurrentWeather
            {
                Weather = weatherCode,
                PublishTime = now,
                Temperature = new ValueUnitPair { Value = currentTemperature, Unit = "°C" },
                FeelsLike = new ValueUnitPair { Value = currentTemperature, Unit = "°C" },
                Humidity = new ValueUnitPair { Value = "50", Unit = "%" },
                Pressure = new ValueUnitPair { Value = "1013", Unit = "hPa" },
                Visibility = new ValueUnitPair { Value = "10", Unit = "km" },
                Wind = new WindInfo
                {
                    Direction = new ValueUnitPair { Value = "0", Unit = "°" },
                    Speed = new ValueUnitPair { Value = "0", Unit = "km/h" }
                }
            },
            UpdateTimeUnix = DateTimeOffset.Now.ToUnixTimeMilliseconds(),
            ForecastDaily = new ForecastDaily
            {
                Temperature = new StatusValueBase<List<RangedValue>> { Value = dailyTemperatures },
                Weather = new StatusValueBase<List<RangedValue>> { Value = dailyWeather },
                SunRiseSet = new StatusValueBase<List<RangedValue>> { Value = sunRiseSet },
                PrecipitationProbability = new StatusValueBase<List<string>>
                {
                    Value = Enumerable.Repeat("0%", 15).ToList()
                }
            },
            ForecastHourly = new ForecastHourly
            {
                Temperature = new StatusValueBase<List<int>> { Value = hourlyTemperatures },
                Weather = new StatusValueBase<List<int>>
                {
                    Value = Enumerable.Repeat(_settings.WeatherCode, 24).ToList()
                }
            },
            Minutely = new Minutely
            {
                Precipitation = new MinutelyPrecipitation
                {
                    Value = Enumerable.Repeat(0.0, 60).ToList()
                }
            }
        };

        LastWeatherInfo = weatherInfo;
        PublishToApplication(weatherInfo);
        IsWeatherRefreshed = true;
        _rulesetService.NotifyStatusChanged();
    }

    private void BindApplicationSettings()
    {
        if (_appSettingsBound)
        {
            return;
        }

        try
        {
            var appAssembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(assembly => assembly.GetName().Name == "ClassIsland");
            var settingsServiceType = appAssembly?.GetType("ClassIsland.Services.SettingsService");
            if (settingsServiceType == null || IAppHost.Host == null)
            {
                return;
            }

            _appSettingsService = IAppHost.Host.Services.GetService(settingsServiceType);
            _appSettings = settingsServiceType.GetProperty("Settings")?.GetValue(_appSettingsService);
            if (_appSettings == null)
            {
                return;
            }

            _appLastWeatherInfoProperty = _appSettings.GetType().GetProperty("LastWeatherInfo");
            _appWeatherIconProperty = _appSettings.GetType().GetProperty("WeatherIconId");
            _appSettingsNotifier = _appSettings as INotifyPropertyChanged;
            if (_appSettingsNotifier != null)
            {
                _appSettingsNotifier.PropertyChanged += AppSettingsOnPropertyChanged;
            }

            UpdateWeatherIconTemplate();
            _appSettingsBound = true;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "无法绑定 ClassIsland 应用设置，天气组件可能无法同步");
        }
    }

    private void PublishToApplication(WeatherInfo weatherInfo)
    {
        BindApplicationSettings();
        if (_appLastWeatherInfoProperty == null || _appSettings == null)
        {
            return;
        }

        try
        {
            _appLastWeatherInfoProperty.SetValue(_appSettings, weatherInfo);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "无法将手动天气写入 ClassIsland 应用设置");
        }
    }

    private void AppSettingsOnPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == "WeatherIconId")
        {
            UpdateWeatherIconTemplate();
        }
    }

    private void UpdateWeatherIconTemplate()
    {
        var iconId = _appWeatherIconProperty?.GetValue(_appSettings) as string;
        SelectedWeatherIconTemplate =
            IWeatherService.RegisteredTemplates.FirstOrDefault(item => item.Id == iconId)?.Template ??
            IWeatherService.RegisteredTemplates.FirstOrDefault()?.Template;
    }

    private static double PositiveModulo(double value, double modulus)
    {
        var remainder = value % modulus;
        return remainder < 0 ? remainder + modulus : remainder;
    }

    private string FormatTemperature(double value)
    {
        return _settings.HideTemperatureDecimal
            ? Math.Round(value, MidpointRounding.AwayFromZero)
                .ToString("0", CultureInfo.InvariantCulture)
            : value.ToString("0.#", CultureInfo.InvariantCulture);
    }

    private static bool IsWeatherCodeMatched(string actual, string target)
    {
        return actual == target || ExpandWeatherCode(actual).Overlaps(ExpandWeatherCode(target));
    }

    private static HashSet<string> ExpandWeatherCode(string code)
    {
        return code switch
        {
            "21" => ["7", "8", "21"],
            "22" => ["8", "9", "22"],
            "23" => ["9", "10", "23"],
            "24" => ["10", "11", "24"],
            "25" => ["11", "12", "25"],
            "26" => ["14", "15", "26"],
            "27" => ["15", "16", "27"],
            "28" => ["16", "17", "28"],
            "301" => ["3", "4", "5", "6", "7", "8", "9", "10", "11", "12", "19", "21",
                "22", "23", "24", "25", "301"],
            "302" => ["6", "13", "14", "15", "16", "17", "26", "27", "28", "34", "302"],
            _ => [code]
        };
    }
}
