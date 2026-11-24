using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using System.Diagnostics;
using System.Globalization;

namespace ManualWeatherPlugin.Controls;

public sealed class TemperatureCurveControl : Control
{
    private const double LineAnimationSeconds = 0.42;
    private const double TooltipFadeSeconds = 0.16;

    private readonly DispatcherTimer _animationTimer;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private double[]? _displayValues;
    private double[]? _animationFromValues;
    private double[]? _animationToValues;
    private double _lineProgress = 1;
    private double _displayHoverIndex;
    private double _tooltipOpacity;
    private int? _targetHoverIndex;
    private double _lastFrameTime;

    public static readonly StyledProperty<IReadOnlyList<double>?> ValuesProperty =
        AvaloniaProperty.Register<TemperatureCurveControl, IReadOnlyList<double>?>(nameof(Values));

    public static readonly StyledProperty<double> LowTemperatureProperty =
        AvaloniaProperty.Register<TemperatureCurveControl, double>(nameof(LowTemperature));

    public static readonly StyledProperty<double> HighTemperatureProperty =
        AvaloniaProperty.Register<TemperatureCurveControl, double>(nameof(HighTemperature));

    public static readonly StyledProperty<bool> OmitDecimalPointProperty =
        AvaloniaProperty.Register<TemperatureCurveControl, bool>(nameof(OmitDecimalPoint));

    public IReadOnlyList<double>? Values
    {
        get => GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public double LowTemperature
    {
        get => GetValue(LowTemperatureProperty);
        set => SetValue(LowTemperatureProperty, value);
    }

    public double HighTemperature
    {
        get => GetValue(HighTemperatureProperty);
        set => SetValue(HighTemperatureProperty, value);
    }

    public bool OmitDecimalPoint
    {
        get => GetValue(OmitDecimalPointProperty);
        set => SetValue(OmitDecimalPointProperty, value);
    }

    static TemperatureCurveControl()
    {
        AffectsRender<TemperatureCurveControl>(
            ValuesProperty,
            LowTemperatureProperty,
            HighTemperatureProperty,
            OmitDecimalPointProperty);
    }

    public TemperatureCurveControl()
    {
        Cursor = new Cursor(StandardCursorType.Cross);
        _animationTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, OnAnimationTick);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ValuesProperty)
        {
            BeginLineAnimation(change.NewValue as IReadOnlyList<double>);
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        var values = _displayValues ?? Values;
        if (values == null || values.Count < 2 || Bounds.Width <= 24)
        {
            SetHoverTarget(null);
            return;
        }

        var position = e.GetPosition(this);
        var plotLeft = 12d;
        var plotWidth = Bounds.Width - 24;
        if (position.X < plotLeft || position.X > plotLeft + plotWidth)
        {
            SetHoverTarget(null);
            return;
        }

        SetHoverTarget(GetNearestHourIndex(position.X, Bounds.Width, values.Count));
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        SetHoverTarget(null);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var background = new SolidColorBrush(Color.FromArgb(28, 64, 132, 255));
        var gridPen = new Pen(new SolidColorBrush(Color.FromArgb(55, 127, 127, 127)), 1);
        var lineBrush = new SolidColorBrush(Color.FromRgb(64, 132, 255));
        var linePen = new Pen(lineBrush, 2.5);
        var fillBrush = new SolidColorBrush(Color.FromArgb(42, 64, 132, 255));
        var bounds = new Rect(0, 0, Bounds.Width, Bounds.Height);

        context.DrawRectangle(background, null, bounds, 8, 8);

        for (var index = 0; index < 4; index++)
        {
            var y = 12 + (Bounds.Height - 24) * index / 3;
            context.DrawLine(gridPen, new Point(12, y), new Point(Bounds.Width - 12, y));
        }

        var values = _displayValues ?? Values;
        if (values == null || values.Count < 2 || Bounds.Width <= 24 || Bounds.Height <= 24)
        {
            return;
        }

        var low = Math.Min(LowTemperature, HighTemperature) - 1;
        var high = Math.Max(LowTemperature, HighTemperature) + 1;
        var range = Math.Max(1, high - low);
        var points = new Point[values.Count];

        for (var index = 0; index < values.Count; index++)
        {
            var x = 12 + (Bounds.Width - 24) * index / (values.Count - 1);
            var y = 12 + (Bounds.Height - 24) * (high - values[index]) / range;
            points[index] = new Point(x, y);
        }

        var area = new StreamGeometry();
        using (var geometry = area.Open())
        {
            geometry.BeginFigure(new Point(points[0].X, Bounds.Height - 12), true);
            foreach (var point in points)
            {
                geometry.LineTo(point);
            }
            geometry.LineTo(new Point(points[^1].X, Bounds.Height - 12));
            geometry.EndFigure(true);
        }
        context.DrawGeometry(fillBrush, null, area);

        var line = new StreamGeometry();
        using (var geometry = line.Open())
        {
            geometry.BeginFigure(points[0], false);
            foreach (var point in points.Skip(1))
            {
                geometry.LineTo(point);
            }
            geometry.EndFigure(false);
        }
        context.DrawGeometry(null, linePen, line);

        for (var index = 0; index < points.Length; index += 3)
        {
            context.DrawEllipse(lineBrush, null, points[index], 3, 3);
        }

        if (_tooltipOpacity <= 0.01)
        {
            return;
        }

        var hoverIndex = Math.Clamp(
            (int)Math.Round(_displayHoverIndex, MidpointRounding.AwayFromZero),
            0,
            values.Count - 1);
        var lowerIndex = Math.Clamp((int)Math.Floor(_displayHoverIndex), 0, values.Count - 1);
        var upperIndex = Math.Min(lowerIndex + 1, values.Count - 1);
        var pointFraction = _displayHoverIndex - lowerIndex;
        var hoverX = points[lowerIndex].X + (points[upperIndex].X - points[lowerIndex].X) * pointFraction;
        var hoverY = points[lowerIndex].Y + (points[upperIndex].Y - points[lowerIndex].Y) * pointFraction;
        var hoverPoint = new Point(hoverX, hoverY);
        var opacity = _tooltipOpacity;

        var markerBrush = new SolidColorBrush(Color.FromArgb(
            (byte)(255 * opacity),
            25,
            105,
            230));
        var markerPen = new Pen(
            new SolidColorBrush(Color.FromArgb((byte)(255 * opacity), 255, 255, 255)),
            2);
        var tooltipBackground = new SolidColorBrush(Color.FromArgb(
            (byte)(238 * opacity),
            31,
            38,
            52));
        var tooltipBorder = new Pen(
            new SolidColorBrush(Color.FromArgb((byte)(160 * opacity), 255, 255, 255)),
            1);
        var tooltipTextBrush = new SolidColorBrush(Color.FromArgb(
            (byte)(255 * opacity),
            255,
            255,
            255));
        var temperatureText = OmitDecimalPoint
            ? Math.Round(values[hoverIndex], MidpointRounding.AwayFromZero)
                .ToString("0", CultureInfo.InvariantCulture)
            : values[hoverIndex].ToString("0.#", CultureInfo.InvariantCulture);
        var tooltipText = new FormattedText(
            $"{hoverIndex:00}:00   {temperatureText}°C",
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(FontFamily.Default),
            12,
            tooltipTextBrush);

        context.DrawLine(
            new Pen(
                new SolidColorBrush(Color.FromArgb((byte)(95 * opacity), 25, 105, 230)),
                1),
            new Point(hoverPoint.X, 12),
            new Point(hoverPoint.X, Bounds.Height - 12));
        context.DrawEllipse(markerBrush, markerPen, hoverPoint, 5, 5);

        var tooltipWidth = tooltipText.Width + 16;
        var tooltipHeight = tooltipText.Height + 10;
        var tooltipX = Math.Clamp(
            hoverPoint.X - tooltipWidth / 2,
            2,
            Math.Max(2, Bounds.Width - tooltipWidth - 2));
        var tooltipY = hoverPoint.Y - tooltipHeight - 10;
        if (tooltipY < 4)
        {
            tooltipY = hoverPoint.Y + 10;
        }

        var tooltipRect = new Rect(tooltipX, tooltipY, tooltipWidth, tooltipHeight);
        context.DrawRectangle(tooltipBackground, tooltipBorder, tooltipRect, 6, 6);
        context.DrawText(tooltipText, new Point(tooltipX + 8, tooltipY + 5));
    }

    public static int GetNearestHourIndex(double x, double width, int count)
    {
        if (count <= 1 || width <= 24)
        {
            return 0;
        }

        var fraction = Math.Clamp((x - 12) / (width - 24), 0, 1);
        return (int)Math.Round(fraction * (count - 1), MidpointRounding.AwayFromZero);
    }

    private void BeginLineAnimation(IReadOnlyList<double>? newValues)
    {
        if (newValues == null || newValues.Count < 2)
        {
            _displayValues = null;
            _lineProgress = 1;
            InvalidateVisual();
            return;
        }

        if (_displayValues == null || _displayValues.Length != newValues.Count)
        {
            _displayValues = newValues.ToArray();
            _lineProgress = 1;
            InvalidateVisual();
            return;
        }

        _animationFromValues = _displayValues.ToArray();
        _animationToValues = newValues.ToArray();
        _lineProgress = 0;
        StartAnimation();
    }

    private void SetHoverTarget(int? target)
    {
        if (_targetHoverIndex == target)
        {
            return;
        }

        if (_tooltipOpacity <= 0.01 && target is int targetIndex)
        {
            _displayHoverIndex = targetIndex;
        }

        _targetHoverIndex = target;
        StartAnimation();
    }

    private void StartAnimation()
    {
        if (!_animationTimer.IsEnabled)
        {
            _lastFrameTime = _clock.Elapsed.TotalSeconds;
            _animationTimer.Start();
        }
    }

    private void OnAnimationTick(object? sender, EventArgs e)
    {
        var now = _clock.Elapsed.TotalSeconds;
        var delta = Math.Clamp(now - _lastFrameTime, 0, 0.05);
        _lastFrameTime = now;

        if (_lineProgress < 1 && _animationFromValues != null && _animationToValues != null)
        {
            _lineProgress = Math.Min(1, _lineProgress + delta / LineAnimationSeconds);
            var eased = EaseOutCubic(_lineProgress);
            _displayValues = _animationFromValues
                .Zip(_animationToValues, (from, to) => from + (to - from) * eased)
                .ToArray();
        }

        var targetOpacity = _targetHoverIndex == null ? 0 : 1;
        _tooltipOpacity = MoveTowards(
            _tooltipOpacity,
            targetOpacity,
            delta / TooltipFadeSeconds);

        if (_targetHoverIndex is int targetIndex)
        {
            var hoverBlend = 1 - Math.Exp(-delta * 22);
            _displayHoverIndex += (targetIndex - _displayHoverIndex) * hoverBlend;
        }

        InvalidateVisual();

        var hoverSettled =
            Math.Abs(_tooltipOpacity - targetOpacity) < 0.001 &&
            (_targetHoverIndex == null ||
             Math.Abs(_displayHoverIndex - _targetHoverIndex.Value) < 0.01);
        if (_lineProgress >= 1 && hoverSettled)
        {
            _tooltipOpacity = targetOpacity;
            if (_targetHoverIndex is int settledIndex)
            {
                _displayHoverIndex = settledIndex;
            }

            _animationTimer.Stop();
        }
    }

    private static double MoveTowards(double current, double target, double amount)
    {
        if (Math.Abs(target - current) <= amount)
        {
            return target;
        }

        return current + Math.Sign(target - current) * amount;
    }

    private static double EaseOutCubic(double value)
    {
        var inverse = 1 - value;
        return 1 - inverse * inverse * inverse;
    }
}
