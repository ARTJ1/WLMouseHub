using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using WLMouseHub.Protocol;

namespace WLMouseHub;

/// <summary>Setting page: profiles, buttons around the mouse, Performance and DPI panels.</summary>
public partial class MainWindow
{
    private readonly ObservableCollection<DpiStageVm> _stages = new();
    private readonly List<ButtonVm> _buttons = new();
    private readonly Dictionary<WlmMouse.Flag, CheckBox> _flagSwitches = new();
    private byte[] _dpiColors = new byte[18];
    private byte? _selectedButton;

    private static readonly (WlmMouse.Flag Flag, string Key, string Tip)[] ToggleDefs =
    {
        (WlmMouse.Flag.HighSpeed, "HighSpeed", "Tip.HighSpeed"),
        (WlmMouse.Flag.ButtonCombo, "ButtonCombo", "Tip.Combo"),
        (WlmMouse.Flag.Tracking, "Turbo", "Tip.Turbo"),
        (WlmMouse.Flag.Ripple, "Ripple", "Tip.Ripple"),
        (WlmMouse.Flag.MotionSync, "MotionSync", "Tip.MotionSync"),
        (WlmMouse.Flag.AngleSnap, "AngleSnap", "Tip.AngleSnap"),
        (WlmMouse.Flag.DpiIndicator, "DpiIndicator", "Tip.SelectColor"),
    };

    private void InitSettingsPage()
    {
        LstStages.ItemsSource = _stages;
        RebuildToggles();
    }

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (PanelPerf == null || PanelScreen == null) return;
        var panels = new (bool Show, FrameworkElement Panel)[]
        {
            (NavPerf.IsChecked == true, PanelPerf),
            (NavDpi.IsChecked == true, PanelDpi),
            (NavMacro.IsChecked == true, PanelMacro),
            (NavLight.IsChecked == true && !_screenMode, PanelLight),
            (NavLight.IsChecked == true && _screenMode, PanelScreen),
        };
        foreach (var (show, panel) in panels)
        {
            bool wasVisible = panel.Visibility == Visibility.Visible;
            panel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            if (show && !wasVisible) Anim.FadeIn(panel, 10, 280);
        }
        if (NavLight.IsChecked == true && _screenMode) _ = EnsureScreenLoadedAsync();
    }

    private void SetupModelControls(WlmMouse m)
    {
        var model = m.Model;
        _loading = true;

        CmbStageCount.Items.Clear();
        for (int i = 1; i <= model.MaxStages; i++) CmbStageCount.Items.Add(i);
        TxtDpiRange.Text = $"50 – {model.MaxDpi} DPI";
        ChkSeparateXY.IsEnabled = model.HasDpiXY;

        CmbPolling.Items.Clear();
        foreach (int hz in model.PollingRates(m.ProductId, m.IsWired)) CmbPolling.Items.Add(new Choice<int>($"{hz} Hz", hz));
        CmbLod.Items.Clear();
        foreach (double mm in model.LodOptions) CmbLod.Items.Add(new Choice<double>($"{mm:0.#} MM", mm));

        CardAngleTune.Visibility = model.HasAngleTune ? Visibility.Visible : Visibility.Hidden;
        CardSleep.Visibility = model.HasSleepTime ? Visibility.Visible : Visibility.Hidden;
        SetupSleepLabels();
        RebuildToggles();

        NavLight.Visibility = Visibility.Visible;
        bool light = m.LightingSupported;
        TxtNoLighting.Visibility = light ? Visibility.Collapsed : Visibility.Visible;
        CardLightMain.Visibility = CardLightPicker.Visibility = CardLightSwatches.Visibility = light ? Visibility.Visible : Visibility.Collapsed;
        RefreshEffects();
        SetupScreenNav();

        _loading = false;
    }

    private void SetupSleepLabels() => SldSleep.Tag = Loc.T("Minutes");

    private void BuildProfileTabs(IReadOnlyList<string> names)
    {
        PanelProfiles.Children.Clear();
        for (int p = 1; p <= _profileCount; p++)
        {
            int profile = p;
            string label = string.IsNullOrEmpty(names.ElementAtOrDefault(p - 1)) ? Loc.F("Profile", p) : names[p - 1];
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
            var edit = new TextBlock
            {
                Text = "", FontFamily = (FontFamily)Application.Current.Resources["F.Icons"], FontSize = 12,
                Margin = new Thickness(26, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand,
                ToolTip = Loc.T("Rename"),
            };
            edit.MouseLeftButtonUp += (_, ev) => { ev.Handled = true; RenameProfile(profile, label); };
            content.Children.Add(edit);
            var rb = new RadioButton
            {
                Style = (Style)FindResource("ProfileRadio"), Content = content, GroupName = "profile",
                IsChecked = p == _profile, Tag = p,
            };
            rb.Checked += Profile_Checked;
            PanelProfiles.Children.Add(rb);
        }
        PanelProfiles.ToolTip = Loc.T("Tip.Profile");
        Dispatcher.BeginInvoke(() => PlaceCapsules(false), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private async void Profile_Checked(object sender, RoutedEventArgs e)
    {
        PlaceCapsules(true);
        if (_loading || sender is not RadioButton { Tag: int p } || p == _profile) return;
        _profile = p;
        ShowBusy(Loc.F("Profile", p));
        try
        {
            if (await Run(null, m => m.SetActiveProfile(p)))
            {
                await LoadProfileAsync();
                UpdateInfoCards();
            }
        }
        finally
        {
            HideBusy();
        }
    }

    private async void RenameProfile(int profile, string current)
    {
        var dlg = new InputDialog(Loc.T("Rename"), Loc.T("NewName"), current);
        if (dlg.ShowDialog() != true || dlg.Value.Length == 0) return;
        string name = dlg.Value;
        if (await Run(Loc.T("Saved"), m => m.SetProfileName(profile, name)))
        {
            var m = _mouse!;
            var names = await Task.Run(() => Enumerable.Range(1, _profileCount).Select(m.GetProfileName).ToList());
            _loading = true;
            BuildProfileTabs(names);
            _loading = false;
        }
    }

    private async void ResetProfile_Click(object sender, RoutedEventArgs e)
    {
        if (!ThemedDialog.Confirm(Loc.T("ResetProfile"), Loc.T("ConfirmResetProfile"))) return;
        if (await Run(Loc.T("Saved"), m => m.ResetProfile(_profile)))
        {
            await LoadProfileAsync();
            UpdateInfoCards();
        }
    }

    private async void FactoryReset_Click(object sender, RoutedEventArgs e)
    {
        if (!ThemedDialog.Confirm(Loc.T("FactoryReset"), Loc.T("ConfirmFactory"))) return;
        if (await Run(null, m => m.FactoryReset()))
        {
            await Task.Delay(1500);
            Disconnect(Loc.T("Searching"));
            await ScanAsync();
        }
    }

    // ===================================================================== profile data

    private sealed class ProfileData
    {
        public List<DpiStage> Stages = new();
        public int ActiveStage;
        public byte[] Colors = new byte[18];
        public int Polling;
        public double Lod;
        public int Debounce;
        public int? Sleep;
        public int AngleTune;
        public Dictionary<WlmMouse.Flag, bool> Flags = new();
        public List<(byte Id, ButtonAction Action)> Buttons = new();
        public LightState? Light;
        public int Brightness;
    }

    private IEnumerable<WlmMouse.Flag> SupportedFlags(DeviceModel model) => ToggleDefs.Select(t => t.Flag).Where(f => f switch
    {
        WlmMouse.Flag.HighSpeed or WlmMouse.Flag.Tracking => model.HasHyperMode,
        WlmMouse.Flag.ButtonCombo => model.HasButtonCombine,
        WlmMouse.Flag.DpiIndicator => model.HasDpiIndicator,
        _ => true,
    });

    private async Task LoadProfileAsync()
    {
        var m = _mouse;
        if (m == null) return;
        int p = _profile;
        var model = m.Model;
        var flags = SupportedFlags(model).ToList();
        if (model.HasDpiXY) flags.Add(WlmMouse.Flag.SeparateXY);

        ProfileData d;
        try
        {
            d = await Task.Run(() =>
            {
                var data = new ProfileData
                {
                    Stages = m.GetDpiStages(p),
                    ActiveStage = m.GetActiveDpiStage(p),
                    Colors = m.GetDpiColors(p),
                    Polling = m.GetPollingRate(p),
                    Lod = m.GetLod(p),
                    Debounce = m.GetDebounce(p),
                    Sleep = model.HasSleepTime ? m.GetSleepSeconds(p) : null,
                    AngleTune = model.HasAngleTune ? m.GetAngleTune(p) : 0,
                };
                foreach (var f in flags) data.Flags[f] = m.GetFlag(p, f);
                foreach (byte id in model.ButtonIds) data.Buttons.Add((id, m.GetButton(p, id)));
                if (m.LightingSupported)
                {
                    data.Light = m.GetLight();
                    data.Brightness = m.GetBrightness();
                }
                return data;
            });
        }
        catch (WlmException ex)
        {
            Status(ex.Message);
            return;
        }

        _loading = true;
        try
        {
            _dpiColors = d.Colors.Length >= 18 ? d.Colors : d.Colors.Concat(new byte[18]).Take(18).ToArray();
            bool xy = d.Flags.GetValueOrDefault(WlmMouse.Flag.SeparateXY);
            ChkSeparateXY.IsChecked = xy;
            _stages.Clear();
            for (int i = 0; i < d.Stages.Count; i++)
            {
                _stages.Add(new DpiStageVm(i + 1, model.MaxDpi, model.DpiStep)
                {
                    X = d.Stages[i].X.ToString(),
                    Y = d.Stages[i].Y.ToString(),
                    IsActive = i + 1 == d.ActiveStage,
                    YVisibility = xy ? Visibility.Visible : Visibility.Collapsed,
                    Color = StageColor(i),
                });
            }
            CmbStageCount.SelectedItem = d.Stages.Count;
            UpdateActiveSwatch();

            CmbPolling.SelectedItem = CmbPolling.Items.Cast<Choice<int>>().FirstOrDefault(c => c.Value == d.Polling);
            CmbLod.SelectedItem = CmbLod.Items.Cast<Choice<double>>().FirstOrDefault(c => Math.Abs(c.Value - d.Lod) < 0.01);
            SldDebounce.Value = d.Debounce;
            SldAngleTune.Value = d.AngleTune;
            ChkSleep.IsChecked = d.Sleep != null;
            SldSleep.IsEnabled = d.Sleep != null;
            SldSleep.Value = d.Sleep is int s ? Math.Clamp(Math.Round(s / 60.0), 1, 30) : 1;

            foreach (var (flag, box) in _flagSwitches) box.IsChecked = d.Flags.GetValueOrDefault(flag);
            if (_flagSwitches.TryGetValue(WlmMouse.Flag.Tracking, out var turbo))
                turbo.IsEnabled = d.Flags.GetValueOrDefault(WlmMouse.Flag.HighSpeed);

            _buttons.Clear();
            foreach (var (id, action) in d.Buttons) _buttons.Add(new ButtonVm(id, action));
            RebuildButtonCards();
            RebuildMarkers();
            Anim.Stagger(PanelButtonsLeft, 60);
            Anim.Stagger(PanelButtonsRight, 60);
            Anim.Stagger(PanelToggles, 30, 10);

            if (d.Light != null) ShowLight(d.Light, d.Brightness);
        }
        finally
        {
            _loading = false;
        }
        UpdateInfoCards();
    }

    private Color StageColor(int i) =>
        i * 3 + 2 < _dpiColors.Length ? Color.FromRgb(_dpiColors[i * 3], _dpiColors[i * 3 + 1], _dpiColors[i * 3 + 2]) : Colors.White;

    // ===================================================================== buttons around the mouse

    private string ButtonTitle(ButtonVm b) => Loc.T("B" + b.Number);

    private string ActionText(ButtonAction a) => a.Describe(Loc.T, MacroName);

    private string MacroName(int id) => _macros.FirstOrDefault(m => m.Id == id)?.DisplayName ?? Loc.F("MacroN", id);

    private void RebuildButtonCards()
    {
        if (PanelButtonsLeft == null) return;
        PanelButtonsLeft.Children.Clear();
        PanelButtonsRight.Children.Clear();
        foreach (var b in _buttons.OrderBy(b => b.Number))
        {
            var card = new Border { Style = (Style)FindResource("Card"), Margin = new Thickness(0, 0, 0, 8), Padding = new Thickness(18, 12, 18, 10) };
            if (_selectedButton == b.Id) card.SetResourceReference(Border.BorderBrushProperty, "B.Accent");
            card.BorderThickness = new Thickness(_selectedButton == b.Id ? 1.5 : 0);
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock { Text = ButtonTitle(b), Style = (Style)FindResource("T.Title"), Margin = new Thickness(0, 0, 0, 8) });
            var pick = new Button { Tag = b, HorizontalContentAlignment = HorizontalAlignment.Center };
            var label = new StackPanel { Orientation = Orientation.Horizontal };
            label.Children.Add(new TextBlock { Text = ActionText(b.Action), MaxWidth = 120, TextTrimming = TextTrimming.CharacterEllipsis });
            label.Children.Add(new TextBlock
            {
                Text = "", FontFamily = (FontFamily)Application.Current.Resources["F.Icons"], FontSize = 10,
                Margin = new Thickness(8, 2, 0, 0),
            });
            pick.Content = label;
            pick.Click += (_, _) => OpenButtonMenu(b, pick);
            stack.Children.Add(pick);
            card.Child = stack;
            (b.Number is 1 or 4 or 5 ? PanelButtonsLeft : PanelButtonsRight).Children.Add(card);
        }
    }

    private void RebuildMarkers()
    {
        if (CanvasMarkers == null) return;
        CanvasMarkers.Children.Clear();
        var model = _mouse?.Model;
        if (model == null || !_hasKeyImage) return; // marker positions only fit the Device_Key picture
        foreach (var b in _buttons)
        {
            if (model.MarkerFor(b.Id) is not { } pos) continue;
            var (top, left) = pos;
            var marker = new Border
            {
                Width = 20, Height = 20, CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(2),
                BorderBrush = Brushes.White, Cursor = Cursors.Hand, Tag = b,
                Background = _selectedButton == b.Id ? Res("B.Accent") : Brushes.Transparent,
                Child = new TextBlock
                {
                    Text = b.Number.ToString(), FontSize = 10, FontWeight = FontWeights.Bold, Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                },
            };
            marker.MouseLeftButtonUp += (_, _) => SelectButton(b);
            Canvas.SetTop(marker, top);
            Canvas.SetLeft(marker, left);
            CanvasMarkers.Children.Add(marker);
        }
    }

    private void SelectButton(ButtonVm b)
    {
        _selectedButton = _selectedButton == b.Id ? null : b.Id;
        RebuildMarkers();
        RebuildButtonCards();
    }

    private void OpenButtonMenu(ButtonVm b, FrameworkElement anchor)
    {
        _selectedButton = b.Id;
        RebuildMarkers();

        var menu = new ContextMenu { PlacementTarget = anchor, Placement = PlacementMode.Bottom };
        MenuItem Item(string key, ButtonAction action)
        {
            var mi = new MenuItem { Header = Loc.T(key), IsChecked = b.Action.SameAs(action) };
            mi.Click += async (_, _) => await AssignButtonAsync(b, action);
            return mi;
        }
        MenuItem Group(string key, ActionGroup group)
        {
            var g = new MenuItem { Header = Loc.T(key) };
            foreach (var (k, grp, a) in ButtonAction.Presets.Where(p => p.Group == group)) g.Items.Add(Item(k, a));
            return g;
        }

        foreach (var (k, _, a) in ButtonAction.Presets.Where(p => p.Group == ActionGroup.Mouse)) menu.Items.Add(Item(k, a));
        menu.Items.Add(Group("GroupDpi", ActionGroup.Dpi));
        menu.Items.Add(Group("GroupMedia", ActionGroup.Media));
        menu.Items.Add(Group("GroupSystem", ActionGroup.System));

        var keys = new MenuItem { Header = Loc.T("KeyCombo"), IsChecked = b.Action.Type == ActionType.Keyboard };
        keys.Click += async (_, _) =>
        {
            var dlg = new KeyComboDialog(ButtonTitle(b), b.Action);
            if (dlg.ShowDialog() == true && dlg.Result != null) await AssignButtonAsync(b, dlg.Result);
        };
        menu.Items.Add(keys);

        var dpiLock = new MenuItem { Header = Loc.T("DpiLockAssign"), IsChecked = b.Action.Type == ActionType.Dpi && b.Action.Data.FirstOrDefault() == 5 };
        dpiLock.Click += async (_, _) =>
        {
            var dlg = new InputDialog(Loc.T("DpiLock"), Loc.F("DpiRange", _mouse?.Model.MaxDpi ?? 26000), "800");
            if (dlg.ShowDialog() != true) return;
            if (!int.TryParse(dlg.Value, out int dpi) || dpi < 50 || dpi > (_mouse?.Model.MaxDpi ?? 26000))
            {
                Status(Loc.F("DpiRange", _mouse?.Model.MaxDpi ?? 26000));
                return;
            }
            await AssignButtonAsync(b, ButtonAction.DpiLock(dpi));
        };
        menu.Items.Add(dpiLock);

        var macro = new MenuItem { Header = Loc.T("MacroAssign"), IsChecked = b.Action.Type is ActionType.MacroTimes or ActionType.MacroHold or ActionType.MacroToggle };
        macro.Click += async (_, _) =>
        {
            var dlg = new MacroAssignDialog(ButtonTitle(b), _macros, b.Action);
            if (dlg.ShowDialog() == true && dlg.Result != null) await AssignButtonAsync(b, dlg.Result);
        };
        menu.Items.Add(macro);

        var restore = new MenuItem { Header = Loc.T("RestoreButtons") };
        restore.Click += async (_, _) => await AssignButtonAsync(b, ButtonVm.DefaultFor(b.Id));
        menu.Items.Add(new Separator { Opacity = 0.3 });
        menu.Items.Add(Item("Disabled", new ButtonAction(ActionType.Off, Array.Empty<byte>())));
        menu.Items.Add(restore);

        menu.IsOpen = true;
    }

    private async Task AssignButtonAsync(ButtonVm button, ButtonAction action)
    {
        var leftClick = ButtonAction.Mouse(1);
        if (_buttons.Count(b => (b == button ? action : b.Action).SameAs(leftClick)) == 0)
        {
            Status(Loc.T("AtLeastOneLeft"));
            return;
        }
        if (await Run(Loc.T("Saved"), m => m.SetButton(_profile, button.Id, action)))
        {
            button.Action = action;
            RebuildButtonCards();
        }
    }

    // ===================================================================== performance

    private void RebuildToggles()
    {
        if (PanelToggles == null) return;
        PanelToggles.Children.Clear();
        _flagSwitches.Clear();
        var model = _mouse?.Model;
        var flags = model != null ? SupportedFlags(model).ToHashSet() : ToggleDefs.Take(6).Select(t => t.Flag).ToHashSet();
        bool loading = _loading;
        _loading = true;
        foreach (var (flag, key, tip) in ToggleDefs.Where(t => flags.Contains(t.Flag)))
        {
            var card = new Border { Style = (Style)FindResource("Card"), Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(18, 6, 16, 6) };
            var dock = new DockPanel();
            var box = new CheckBox { Style = (Style)FindResource("Switch"), Tag = flag, VerticalAlignment = VerticalAlignment.Center };
            box.Checked += Flag_Changed;
            box.Unchecked += Flag_Changed;
            DockPanel.SetDock(box, Dock.Right);
            dock.Children.Add(box);
            var title = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            title.Children.Add(new TextBlock { Text = Loc.T(key), Style = (Style)FindResource("T.Title"), TextWrapping = TextWrapping.Wrap, MaxWidth = 130 });
            title.Children.Add(new ContentControl { Style = (Style)FindResource("Help"), ToolTip = Loc.T(tip) });
            dock.Children.Add(title);
            card.Child = dock;
            PanelToggles.Children.Add(card);
            _flagSwitches[flag] = box;
        }
        _loading = loading;
    }

    private async void Flag_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading || sender is not CheckBox { Tag: WlmMouse.Flag flag } box) return;
        bool on = box.IsChecked == true;
        if (!await Run(Loc.T("Saved"), m => m.SetFlag(_profile, flag, on))) return;

        if (flag == WlmMouse.Flag.HighSpeed && _flagSwitches.TryGetValue(WlmMouse.Flag.Tracking, out var turbo))
        {
            turbo.IsEnabled = on;
            if (!on && turbo.IsChecked == true)
            {
                _loading = true;
                turbo.IsChecked = false;
                _loading = false;
                await Run(null, m => m.SetFlag(_profile, WlmMouse.Flag.Tracking, false));
            }
        }
    }

    private async void Polling_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || CmbPolling.SelectedItem is not Choice<int> c) return;
        await Run(Loc.T("Saved"), m => m.SetPollingRate(_profile, c.Value));
        UpdateInfoCards();
    }

    private async void Lod_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || CmbLod.SelectedItem is not Choice<double> c) return;
        await Run(Loc.T("Saved"), m => m.SetLod(_profile, c.Value));
        UpdateInfoCards();
    }

    private async void Debounce_Commit(object sender, EventArgs e)
    {
        if (_loading || _mouse == null) return;
        int ms = (int)SldDebounce.Value;
        await Run(Loc.T("Saved"), m => m.SetDebounce(_profile, ms));
    }

    private async void AngleTune_Commit(object sender, EventArgs e)
    {
        if (_loading || _mouse == null) return;
        int deg = (int)SldAngleTune.Value;
        await Run(Loc.T("Saved"), m => m.SetAngleTune(_profile, deg));
    }

    private async void SleepToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (!_loading && _mouse != null && ChkSleep.IsChecked != true &&
            !ThemedDialog.Confirm(Loc.T("SleepOff1"), Loc.T("SleepOff2") + "\n\n" + Loc.T("SleepOff3")))
        {
            _loading = true;
            ChkSleep.IsChecked = true; // the hub asks before turning sleep off
            _loading = false;
            return;
        }
        SldSleep.IsEnabled = ChkSleep.IsChecked == true;
        if (_loading || _mouse == null) return;
        int? seconds = ChkSleep.IsChecked == true ? (int)SldSleep.Value * 60 : null;
        await Run(Loc.T("Saved"), m => m.SetSleepSeconds(_profile, seconds));
    }

    private async void Sleep_Commit(object sender, EventArgs e)
    {
        if (_loading || _mouse == null || ChkSleep.IsChecked != true) return;
        int seconds = (int)SldSleep.Value * 60;
        await Run(Loc.T("Saved"), m => m.SetSleepSeconds(_profile, seconds));
    }

    // ===================================================================== DPI

    private async void Stage_Activated(object sender, RoutedEventArgs e)
    {
        if (_loading || (sender as FrameworkElement)?.DataContext is not DpiStageVm stage) return;
        if (stage.IsActive) return; // raised by the binding while rendering
        foreach (var s in _stages) s.IsActive = s == stage;
        UpdateActiveSwatch();
        await Run(Loc.T("Saved"), m => m.SetActiveDpiStage(_profile, stage.Index));
        UpdateInfoCards();
    }

    private async void StageCount_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || CmbStageCount.SelectedItem is not int count || _mouse == null) return;
        var model = _mouse.Model;
        int[] defaults = { 400, 800, 1600, 3200, 6400, model.MaxDpi };
        while (_stages.Count < count)
        {
            int i = _stages.Count;
            string v = defaults[Math.Min(i, defaults.Length - 1)].ToString();
            _stages.Add(new DpiStageVm(i + 1, model.MaxDpi, model.DpiStep)
            {
                X = v, Y = v,
                YVisibility = ChkSeparateXY.IsChecked == true ? Visibility.Visible : Visibility.Collapsed,
                Color = StageColor(i),
            });
        }
        while (_stages.Count > count) _stages.RemoveAt(_stages.Count - 1);
        await SaveDpiAsync();
    }

    private async void SeparateXY_Changed(object sender, RoutedEventArgs e)
    {
        bool on = ChkSeparateXY.IsChecked == true;
        foreach (var s in _stages)
        {
            s.YVisibility = on ? Visibility.Visible : Visibility.Collapsed;
            if (!on) s.Y = s.X;
        }
        if (_loading) return;
        if (await Run(Loc.T("Saved"), m => m.SetFlag(_profile, WlmMouse.Flag.SeparateXY, on)) && !on)
            await SaveDpiAsync();
    }

    private async void DpiSlider_Commit(object sender, MouseButtonEventArgs e) => await SaveDpiAsync();
    private async void DpiBox_LostFocus(object sender, RoutedEventArgs e) => await SaveDpiAsync();

    private async void DpiBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) await SaveDpiAsync();
    }

    private string _lastSavedDpi = "";

    private async Task SaveDpiAsync()
    {
        if (_mouse == null || _loading) return;
        var model = _mouse.Model;
        bool xy = ChkSeparateXY.IsChecked == true;
        var stages = new List<DpiStage>();
        foreach (var s in _stages)
        {
            int? x = ParseDpi(s.X, model), y = xy ? ParseDpi(s.Y, model) : x;
            if (x == null || y == null)
            {
                Status(Loc.F("DpiRange", model.MaxDpi));
                return;
            }
            s.X = x.Value.ToString();
            s.Y = y.Value.ToString();
            stages.Add(new DpiStage(x.Value, y.Value));
        }
        int active = _stages.FirstOrDefault(s => s.IsActive)?.Index ?? 1;
        if (active > stages.Count)
        {
            active = stages.Count;
            foreach (var s in _stages) s.IsActive = s.Index == active;
        }

        string signature = string.Join(",", stages) + "|" + active;
        if (signature == _lastSavedDpi) return;
        if (await Run(Loc.T("Saved"), m =>
            {
                m.SetDpiStages(_profile, stages);
                m.SetActiveDpiStage(_profile, active);
            }))
        {
            _lastSavedDpi = signature;
            UpdateInfoCards();
        }
    }

    private static int? ParseDpi(string text, DeviceModel model)
    {
        if (!int.TryParse(text.Trim(), out int v) || v < 50 || v > model.MaxDpi) return null;
        return Math.Clamp((int)Math.Round(v / (double)model.DpiStep) * model.DpiStep, 50, model.MaxDpi);
    }

    private void UpdateActiveSwatch()
    {
        var active = _stages.FirstOrDefault(s => s.IsActive);
        SwatchActive.Background = new SolidColorBrush(active?.Color ?? Colors.White);
    }

    private async void StageColor_Click(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is DpiStageVm stage) await PickStageColorAsync(stage);
    }

    private async void ActiveColor_Click(object sender, MouseButtonEventArgs e)
    {
        if (_stages.FirstOrDefault(s => s.IsActive) is { } stage) await PickStageColorAsync(stage);
    }

    private async Task PickStageColorAsync(DpiStageVm stage)
    {
        var dlg = new ColorDialog(Loc.T("SelectColor"), stage.Color);
        if (dlg.ShowDialog() != true) return;
        var c = dlg.Value;
        stage.Color = c;
        int i = (stage.Index - 1) * 3;
        _dpiColors[i] = c.R;
        _dpiColors[i + 1] = c.G;
        _dpiColors[i + 2] = c.B;
        UpdateActiveSwatch();
        byte[] colors = _dpiColors.ToArray();
        await Run(Loc.T("Saved"), m => m.SetDpiColors(_profile, colors));
    }
}
