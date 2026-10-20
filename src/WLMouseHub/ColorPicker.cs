using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace WLMouseHub;

/// <summary>Saturation/value square + hue bar + hex field, like the hub's lighting colour picker.</summary>
public sealed class ColorPicker : Grid
{
    private readonly Border _square = new() { CornerRadius = new CornerRadius(8), ClipToBounds = true, Width = 170, Height = 100 };
    private readonly Ellipse _svDot = new() { Width = 12, Height = 12, Stroke = Brushes.White, StrokeThickness = 2, IsHitTestVisible = false };
    private readonly Canvas _svCanvas = new();
    private readonly Border _hueBar = new() { Width = 14, CornerRadius = new CornerRadius(7), Height = 128, Margin = new Thickness(10, 0, 0, 0) };
    private readonly Ellipse _hueDot = new() { Width = 14, Height = 14, Stroke = Brushes.White, StrokeThickness = 2, IsHitTestVisible = false };
    private readonly Canvas _hueCanvas = new() { Width = 14, Height = 128 };
    private readonly TextBox _hex = new() { Width = 120, MinHeight = 22, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Left };
    private double _h, _s = 1, _v = 1;
    private bool _updating;

    /// <summary>Raised when the user finishes picking (mouse up or Enter in the hex box).</summary>
    public event Action<Color>? ColorCommitted;

    public ColorPicker()
    {
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var left = new StackPanel();
        var squareHost = new Grid();
        squareHost.Children.Add(_square);
        _svCanvas.Children.Add(_svDot);
        squareHost.Children.Add(_svCanvas);
        left.Children.Add(squareHost);
        var hexRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        hexRow.Children.Add(new TextBlock { Text = "Hex", FontSize = 11, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        hexRow.Children.Add(_hex);
        left.Children.Add(hexRow);
        Children.Add(left);

        _hueBar.Background = new LinearGradientBrush(new GradientStopCollection
        {
            new(Color.FromRgb(255, 0, 0), 0), new(Color.FromRgb(255, 255, 0), 1 / 6.0), new(Color.FromRgb(0, 255, 0), 2 / 6.0),
            new(Color.FromRgb(0, 255, 255), 3 / 6.0), new(Color.FromRgb(0, 0, 255), 4 / 6.0), new(Color.FromRgb(255, 0, 255), 5 / 6.0),
            new(Color.FromRgb(255, 0, 0), 1),
        }, 90);
        var hueHost = new Grid { VerticalAlignment = VerticalAlignment.Top };
        hueHost.Children.Add(_hueBar);
        _hueCanvas.Margin = _hueBar.Margin;
        _hueCanvas.Children.Add(_hueDot);
        hueHost.Children.Add(_hueCanvas);
        SetColumn(hueHost, 1);
        Children.Add(hueHost);

        _square.MouseLeftButtonDown += (_, e) => { _square.CaptureMouse(); PickSv(e.GetPosition(_square)); };
        _square.MouseMove += (_, e) => { if (_square.IsMouseCaptured) PickSv(e.GetPosition(_square)); };
        _square.MouseLeftButtonUp += (_, _) => { _square.ReleaseMouseCapture(); ColorCommitted?.Invoke(Color); };
        _hueBar.MouseLeftButtonDown += (_, e) => { _hueBar.CaptureMouse(); PickHue(e.GetPosition(_hueBar)); };
        _hueBar.MouseMove += (_, e) => { if (_hueBar.IsMouseCaptured) PickHue(e.GetPosition(_hueBar)); };
        _hueBar.MouseLeftButtonUp += (_, _) => { _hueBar.ReleaseMouseCapture(); ColorCommitted?.Invoke(Color); };
        _hex.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            if (TryParseHex(_hex.Text, out var c)) { Color = c; ColorCommitted?.Invoke(c); }
        };
        Loaded += (_, _) => Render();
    }

    public Color Color
    {
        get => FromHsv(_h, _s, _v);
        set
        {
            ToHsv(value, out _h, out _s, out _v);
            Render();
        }
    }

    private void PickSv(Point p)
    {
        _s = Math.Clamp(p.X / _square.Width, 0, 1);
        _v = 1 - Math.Clamp(p.Y / _square.Height, 0, 1);
        Render();
    }

    private void PickHue(Point p)
    {
        _h = Math.Clamp(p.Y / _hueBar.Height, 0, 0.9999) * 360;
        Render();
    }

    private void Render()
    {
        if (_updating) return;
        _updating = true;
        var pure = FromHsv(_h, 1, 1);
        var whiteToHue = new LinearGradientBrush(Colors.White, pure, 0);
        var transparentToBlack = new LinearGradientBrush(Color.FromArgb(0, 0, 0, 0), Colors.Black, 90);
        var layers = new Grid();
        layers.Children.Add(new Rectangle { Fill = whiteToHue });
        layers.Children.Add(new Rectangle { Fill = transparentToBlack });
        _square.Child = layers;
        Canvas.SetLeft(_svDot, _s * _square.Width - 6);
        Canvas.SetTop(_svDot, (1 - _v) * _square.Height - 6);
        _svDot.Stroke = _v > 0.5 ? Brushes.Black : Brushes.White;
        Canvas.SetLeft(_hueDot, 0);
        Canvas.SetTop(_hueDot, _h / 360 * _hueBar.Height - 7);
        _hueDot.Fill = new SolidColorBrush(pure);
        var c = Color;
        _hex.Text = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        _hex.Foreground = new SolidColorBrush(c);
        _updating = false;
    }

    public static bool TryParseHex(string text, out Color c)
    {
        c = Colors.Black;
        string t = text.Trim().TrimStart('#');
        if (t.Length != 6 || !int.TryParse(t, NumberStyles.HexNumber, null, out int v)) return false;
        c = Color.FromRgb((byte)(v >> 16), (byte)(v >> 8), (byte)v);
        return true;
    }

    public static Color FromHsv(double h, double s, double v)
    {
        double c = v * s, x = c * (1 - Math.Abs(h / 60 % 2 - 1)), m = v - c;
        (double r, double g, double b) = (h % 360) switch
        {
            < 60 => (c, x, 0d), < 120 => (x, c, 0d), < 180 => (0d, c, x),
            < 240 => (0d, x, c), < 300 => (x, 0d, c), _ => (c, 0d, x),
        };
        return Color.FromRgb((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }

    public static void ToHsv(Color color, out double h, out double s, out double v)
    {
        double r = color.R / 255.0, g = color.G / 255.0, b = color.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
        h = d == 0 ? 0 : max == r ? 60 * ((g - b) / d % 6) : max == g ? 60 * ((b - r) / d + 2) : 60 * ((r - g) / d + 4);
        if (h < 0) h += 360;
        s = max == 0 ? 0 : d / max;
        v = max;
    }
}
