using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WLMouseHub.Protocol;

namespace WLMouseHub;

/// <summary>Borderless dialog in the hub's card style.</summary>
public class ThemedDialog : Window
{
    private readonly StackPanel _body = new();
    private readonly StackPanel _buttons = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };

    protected ThemedDialog(string title, double width = 400)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.Height;
        Width = width;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Owner = Application.Current.MainWindow;
        FontFamily = (FontFamily)Application.Current.Resources["F.Main"];
        SetResourceReference(ForegroundProperty, "B.Text");

        var card = new Border { CornerRadius = new CornerRadius(18), BorderThickness = new Thickness(1), Padding = new Thickness(22, 18, 22, 18) };
        card.SetResourceReference(Border.BackgroundProperty, "B.Shell");
        card.SetResourceReference(Border.BorderBrushProperty, "B.Outline");
        var root = new StackPanel();
        var heading = new TextBlock { Text = title, Margin = new Thickness(0, 0, 0, 14) };
        heading.SetResourceReference(StyleProperty, "T.Big");
        root.Children.Add(heading);
        root.Children.Add(_body);
        root.Children.Add(_buttons);
        card.Child = root;
        Content = card;
        card.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };

        // The hub's v-modal-in: 0.2 s fade, plus a slight scale-up.
        card.RenderTransformOrigin = new Point(0.5, 0.5);
        var scale = new ScaleTransform(0.94, 0.94);
        card.RenderTransform = scale;
        Loaded += (_, _) =>
        {
            var d = TimeSpan.FromMilliseconds(200);
            var ease = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut };
            card.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 1, d));
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new System.Windows.Media.Animation.DoubleAnimation(1, d) { EasingFunction = ease });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new System.Windows.Media.Animation.DoubleAnimation(1, d) { EasingFunction = ease });
        };
    }

    protected void AddBody(UIElement e) => _body.Children.Add(e);

    protected Button AddButton(string text, bool isDefault, Action onClick)
    {
        var b = new Button { Content = text, MinWidth = 96, Margin = new Thickness(8, 0, 0, 0), IsDefault = isDefault, IsCancel = !isDefault };
        b.Click += (_, _) => onClick();
        _buttons.Children.Add(b);
        return b;
    }

    protected static TextBlock Text(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };

    public static bool Confirm(string title, string message)
    {
        var d = new ConfirmDialog(title, message);
        return d.ShowDialog() == true;
    }

    public static void Info(string title, string message) => new ConfirmDialog(title, message, onlyOk: true).ShowDialog();

    private sealed class ConfirmDialog : ThemedDialog
    {
        public ConfirmDialog(string title, string message, bool onlyOk = false) : base(title, 440)
        {
            AddBody(Text(message));
            if (!onlyOk) AddButton(Loc.T("Cancel"), false, () => DialogResult = false);
            AddButton(Loc.T("OK"), true, () => DialogResult = true);
        }
    }
}

public sealed class InputDialog : ThemedDialog
{
    private readonly TextBox _box;
    public string Value => _box.Text.Trim();

    public InputDialog(string title, string prompt, string initial) : base(title)
    {
        AddBody(Text(prompt));
        _box = new TextBox { Text = initial, MaxLength = 24, HorizontalContentAlignment = HorizontalAlignment.Left };
        AddBody(_box);
        AddButton(Loc.T("Cancel"), false, () => DialogResult = false);
        AddButton(Loc.T("OK"), true, () => DialogResult = true);
        Loaded += (_, _) => { _box.Focus(); _box.SelectAll(); };
    }
}

/// <summary>Captures a key or key combination for a button.</summary>
public sealed class KeyComboDialog : ThemedDialog
{
    private readonly TextBox _box = new() { IsReadOnly = true, MinHeight = 36 };
    private byte _mods, _usage;

    public ButtonAction? Result { get; private set; }

    public KeyComboDialog(string buttonName, ButtonAction current) : base(buttonName)
    {
        _box.Text = Loc.T("PressKeys");
        if (current.Type == ActionType.Keyboard && current.Data.Length >= 2)
        {
            _mods = current.Data[0];
            _usage = current.Data[1];
            _box.Text = HidKeys.Combo(_mods, _usage);
        }
        _box.PreviewKeyDown += OnKey;
        AddBody(_box);
        AddButton(Loc.T("Cancel"), false, () => DialogResult = false);
        AddButton(Loc.T("Assign"), true, () =>
        {
            if (_mods == 0 && _usage == 0) return;
            Result = ButtonAction.Key(_mods, _usage);
            DialogResult = true;
        });
        Loaded += (_, _) => _box.Focus();
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.Enter && (_mods != 0 || _usage != 0)) return; // let the default button act
        byte mods = 0;
        if (Keyboard.IsKeyDown(Key.LeftCtrl)) mods |= HidKeys.LCtrl;
        if (Keyboard.IsKeyDown(Key.RightCtrl)) mods |= HidKeys.RCtrl;
        if (Keyboard.IsKeyDown(Key.LeftShift)) mods |= HidKeys.LShift;
        if (Keyboard.IsKeyDown(Key.RightShift)) mods |= HidKeys.RShift;
        if (Keyboard.IsKeyDown(Key.LeftAlt)) mods |= HidKeys.LAlt;
        if (Keyboard.IsKeyDown(Key.RightAlt)) mods |= HidKeys.RAlt;
        if (Keyboard.IsKeyDown(Key.LWin)) mods |= HidKeys.LWin;
        if (Keyboard.IsKeyDown(Key.RWin)) mods |= HidKeys.RWin;
        _mods = mods;
        _usage = HidKeys.Usage(key) ?? 0;
        _box.Text = HidKeys.Combo(_mods, _usage);
    }
}

/// <summary>Assigns a stored macro to a button.</summary>
public sealed class MacroAssignDialog : ThemedDialog
{
    private readonly ComboBox _slot = new();
    private readonly ComboBox _mode = new() { Margin = new Thickness(0, 8, 0, 0) };
    private readonly TextBox _times = new() { Text = "1", Width = 80, HorizontalAlignment = HorizontalAlignment.Left };

    public ButtonAction? Result { get; private set; }

    public MacroAssignDialog(string buttonName, IEnumerable<MacroSlotVm> macros, ButtonAction current) : base(buttonName)
    {
        foreach (var m in macros.Where(m => !m.IsEmpty)) _slot.Items.Add(new Choice<int>(m.DisplayName, m.Id));
        _mode.Items.Add(new Choice<ActionType>(Loc.T("RunTimes"), ActionType.MacroTimes));
        _mode.Items.Add(new Choice<ActionType>(Loc.T("RepeatHold"), ActionType.MacroHold));
        _mode.Items.Add(new Choice<ActionType>(Loc.T("ToggleRepeat"), ActionType.MacroToggle));
        _slot.SelectedIndex = _slot.Items.Count > 0 ? 0 : -1;
        _mode.SelectedIndex = 0;

        if (current.Type is ActionType.MacroTimes or ActionType.MacroHold or ActionType.MacroToggle && current.Data.Length >= 2)
        {
            int id = (current.Data[0] << 8) | current.Data[1];
            _slot.SelectedItem = _slot.Items.Cast<Choice<int>>().FirstOrDefault(c => c.Value == id) ?? _slot.SelectedItem;
            _mode.SelectedItem = _mode.Items.Cast<Choice<ActionType>>().First(c => c.Value == current.Type);
            if (current.Type == ActionType.MacroTimes && current.Data.Length >= 3) _times.Text = current.Data[2].ToString();
        }

        if (_slot.Items.Count == 0) AddBody(Text(Loc.T("MacroEmpty")));
        AddBody(_slot);
        AddBody(_mode);
        var times = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        times.Children.Add(new TextBlock { Text = Loc.T("Times"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
        times.Children.Add(_times);
        AddBody(times);
        AddButton(Loc.T("Cancel"), false, () => DialogResult = false);
        var ok = AddButton(Loc.T("Assign"), true, () =>
        {
            if (_slot.SelectedItem is not Choice<int> s || _mode.SelectedItem is not Choice<ActionType> m) return;
            int.TryParse(_times.Text, out int n);
            Result = ButtonAction.Macro(m.Value, s.Value, Math.Max(1, n));
            DialogResult = true;
        });
        ok.IsEnabled = _slot.Items.Count > 0;
    }
}

/// <summary>Popup colour picker for DPI stage colours.</summary>
public sealed class ColorDialog : ThemedDialog
{
    private readonly ColorPicker _picker = new();
    public Color Value => _picker.Color;

    public ColorDialog(string title, Color initial) : base(title, 280)
    {
        _picker.Color = initial;
        AddBody(_picker);
        AddButton(Loc.T("Cancel"), false, () => DialogResult = false);
        AddButton(Loc.T("OK"), true, () => DialogResult = true);
    }
}
