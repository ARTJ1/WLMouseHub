using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using WLMouseHub.Protocol;

namespace WLMouseHub;

/// <summary>Wheel Lighting panel: effect, brightness, speed, colour picker, presets and recent colours.</summary>
public partial class MainWindow
{
    private static readonly string[] PresetColors =
        { "#FF0000", "#FF7F00", "#FFE600", "#7FFF00", "#00FFB4", "#0080FF", "#3000FF", "#FF00E6" };

    private readonly ColorPicker _lightPicker = new();
    private Color _lightColor = Colors.Red;

    private void InitLightPage()
    {
        HostLightPicker.Content = _lightPicker;
        _lightPicker.ColorCommitted += async c => await SetLightColorAsync(c);
        PanelPresetColors.Children.Clear();
        foreach (string hex in PresetColors) PanelPresetColors.Children.Add(Swatch(hex));
        RebuildRecentColors();
    }

    private FrameworkElement Swatch(string hex)
    {
        ColorPicker.TryParseHex(hex, out var c);
        var dot = new Ellipse { Width = 24, Height = 24, Fill = new SolidColorBrush(c), Margin = new Thickness(0, 0, 10, 0), Cursor = Cursors.Hand, ToolTip = hex };
        dot.MouseLeftButtonUp += async (_, _) => await SetLightColorAsync(c);
        return dot;
    }

    private void RebuildRecentColors()
    {
        PanelRecentColors.Children.Clear();
        foreach (string hex in App.Settings.RecentColors.Take(9)) PanelRecentColors.Children.Add(Swatch(hex));
        for (int i = App.Settings.RecentColors.Count; i < 9; i++)
        {
            var empty = new Ellipse { Width = 24, Height = 24, Margin = new Thickness(0, 0, 10, 0), Opacity = 0.6 };
            empty.SetResourceReference(Shape.FillProperty, "B.PillActive");
            PanelRecentColors.Children.Add(empty);
        }
    }

    private void RefreshEffects()
    {
        var model = _mouse?.Model;
        bool loading = _loading;
        _loading = true;
        int? selected = (CmbEffect.SelectedItem as Choice<int>)?.Value ?? _light?.Effect;
        CmbEffect.Items.Clear();
        CmbEffect.Items.Add(new Choice<int>(Loc.T("Disable"), 0));
        foreach (int id in model?.LightEffectIds ?? Array.Empty<int>())
        {
            string key = id switch { 1 => "Spectrum", 2 => "Wave", 4 => "Static", 5 => "Breathing", _ => "" };
            CmbEffect.Items.Add(new Choice<int>(key.Length > 0 ? Loc.T(key) : $"Effect {id}", id));
        }
        CmbEffect.SelectedItem = CmbEffect.Items.Cast<Choice<int>>().FirstOrDefault(c => c.Value == selected);
        _loading = loading;
    }

    private void ShowLight(LightState light, int brightness)
    {
        _light = light;
        _lightColor = Color.FromRgb(light.R, light.G, light.B);
        if (!CmbEffect.Items.Cast<Choice<int>>().Any(c => c.Value == light.Effect))
            CmbEffect.Items.Add(new Choice<int>($"Effect {light.Effect}", light.Effect));
        CmbEffect.SelectedItem = CmbEffect.Items.Cast<Choice<int>>().FirstOrDefault(c => c.Value == light.Effect);
        _lightPicker.Color = _lightColor;
        SldSpeed.Value = light.SpeedLevel;
        SldBrightness.Value = brightness;
        UpdateSpeedRow();
    }

    private LightState? _light;

    private void UpdateSpeedRow()
    {
        int effect = (CmbEffect.SelectedItem as Choice<int>)?.Value ?? 0;
        RowSpeed.Visibility = effect is 0 or 4 ? Visibility.Collapsed : Visibility.Visible;
    }

    private async Task ApplyLightAsync()
    {
        if (_loading || CmbEffect.SelectedItem is not Choice<int> effect || _mouse is not { LightingSupported: true }) return;
        var state = new LightState(effect.Value, (int)SldSpeed.Value, _lightColor.R, _lightColor.G, _lightColor.B);
        if (await Run(Loc.T("Saved"), m => m.SetLight(state))) _light = state;
    }

    private async void Effect_Changed(object sender, SelectionChangedEventArgs e)
    {
        UpdateSpeedRow();
        await ApplyLightAsync();
    }

    private async Task SetLightColorAsync(Color c)
    {
        _lightColor = c;
        _lightPicker.Color = c;
        string hex = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        App.Settings.RecentColors.Remove(hex);
        App.Settings.RecentColors.Insert(0, hex);
        if (App.Settings.RecentColors.Count > 9) App.Settings.RecentColors.RemoveRange(9, App.Settings.RecentColors.Count - 9);
        App.Settings.Save();
        RebuildRecentColors();
        await ApplyLightAsync();
    }

    private async void Speed_Commit(object sender, EventArgs e) => await ApplyLightAsync();

    private async void Brightness_Commit(object sender, EventArgs e)
    {
        if (_loading || _mouse is not { LightingSupported: true }) return;
        int pct = (int)SldBrightness.Value;
        await Run(Loc.T("Saved"), m => m.SetBrightness(pct));
    }
}
