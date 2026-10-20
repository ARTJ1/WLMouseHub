using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using WLMouseHub.Hid;
using WLMouseHub.Protocol;
using Forms = System.Windows.Forms;

namespace WLMouseHub;

/// <summary>Core: connection, header, home page, tray. Other pages live in the MainWindow.*.cs partial files.</summary>
public partial class MainWindow : Window
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "WLMouseHub";

    private WlmMouse? _mouse;
    private bool _loading;
    private bool _exiting;
    private bool _scanning;
    private bool _flashing;
    private int _profile = 1;
    private int _profileCount = 3;
    private (bool Charging, int Percent) _battery;
    private string _mouseFw = "", _rxFw = "";
    private bool _hasKeyImage;

    private readonly DispatcherTimer _scanTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly DispatcherTimer _batteryTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    private readonly Forms.NotifyIcon _tray = new();

    public MainWindow()
    {
        InitializeComponent();

        TxtSupported.Text = string.Join("、", DeviceModel.All.Select(m => m.ModelEN));
        _loading = true;
        ChkStartup.IsChecked = Registry.CurrentUser.OpenSubKey(RunKey)?.GetValue(RunValue) != null;
        ChkCloseToTray.IsChecked = App.Settings.CloseToTray;
        _loading = false;

        InitSettingsPage();
        InitMacroPage();
        InitLightPage();
        UpdateHeader();
        Loc.Changed += OnLanguageChanged;

        PreviewKeyDown += Window_PreviewKeyDown;
        PreviewKeyUp += Window_PreviewKeyUp;
        _scanTimer.Tick += async (_, _) => await ScanAsync();
        _batteryTimer.Tick += async (_, _) => await RefreshBatteryAsync();
        SetupTray();

        SizeChanged += (_, _) => PlaceCapsules(false);
        Loaded += async (_, _) =>
        {
            PlaceCapsules(false);
            Anim.FadeIn(PageHome, 16, 400);
            Anim.Twinkle(HomeHintDot, true);
            LoadLogo();
            await ScanAsync();
            if (await ModelCatalog.RefreshAsync()) TxtSupported.Text = string.Join("、", DeviceModel.All.Select(m => m.ModelEN));
        };
        _scanTimer.Start();
    }

    /// <summary>Starts in the tray without showing the window (autostart).</summary>
    public void StartHidden() => _ = ScanAsync();

    // ===================================================================== header

    /// <summary>The hub's wordmark: WLMOUSE_D.png on dark, WLMOUSE.png on light; text fallback when offline.</summary>
    private async void LoadLogo()
    {
        var logo = await AssetCache.GetAsync(App.Settings.Theme == "light" ? "WLMOUSE.png" : "WLMOUSE_D.png");
        ImgLogo.Source = logo;
        ImgLogo.Visibility = logo != null ? Visibility.Visible : Visibility.Collapsed;
        TxtLogo.Visibility = logo != null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void UpdateHeader()
    {
        TxtLang.Text = Loc.Label;
        TxtThemeIcon.Text = App.Settings.Theme == "light" ? "" : "";
    }

    private void Header_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        else if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void Lang_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = BtnLang, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        foreach (var (code, label) in Loc.Languages)
        {
            var item = new MenuItem { Header = label, IsChecked = code == Loc.Lang };
            item.Click += (_, _) =>
            {
                App.Settings.Language = code;
                App.Settings.Save();
                Loc.Apply(code);
            };
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    private void OnLanguageChanged()
    {
        UpdateHeader();
        RefreshDynamicTexts();
    }

    private void Theme_Click(object sender, RoutedEventArgs e)
    {
        App.SetTheme(App.Settings.Theme == "light" ? "dark" : "light");
        UpdateHeader();
        LoadLogo();
        RebuildButtonCards();
        RebuildMarkers();
    }

    private void Tab_Checked(object sender, RoutedEventArgs e)
    {
        if (PageHome == null) return;
        ShowPage(TabSettings.IsChecked == true ? PageSettings : TabFirmware.IsChecked == true ? PageFirmware : PageHome);
        if (TabFirmware.IsChecked == true) _ = RefreshFirmwarePageAsync();
    }

    private void Home_Click(object sender, RoutedEventArgs e) => TabMain.IsChecked = true;
    private void HomeMouse_Click(object sender, RoutedEventArgs e) { if (_mouse != null) TabSettings.IsChecked = true; }
    private void GoFirmware_Click(object sender, RoutedEventArgs e) => TabFirmware.IsChecked = true;

    private void UserGuide_Click(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb) rb.IsChecked = false;
        Process.Start(new ProcessStartInfo("https://www.wlmouse.com/pages/web-hub") { UseShellExecute = true });
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    // ===================================================================== connection

    private async Task ScanAsync()
    {
        if (_scanning || _flashing) return;
        _scanning = true;
        try
        {
            if (_mouse != null)
            {
                string path = _mouse.DevicePath;
                bool present = await Task.Run(() => WlmMouse.EnumerateAll().Any(d => d.Path == path));
                if (!present) Disconnect(Loc.T("Disconnected"));
                return;
            }

            WlmMouse? found = await Task.Run(() =>
            {
                try { return WlmMouse.Find(); }
                catch (WlmException) { return null; }
            });
            if (found == null) return;

            _mouse = found;
            await LoadAllAsync();
        }
        finally
        {
            _scanning = false;
        }
    }

    private void Disconnect(string message)
    {
        _mouse?.Dispose();
        _mouse = null;
        _batteryTimer.Stop();
        TabSettings.IsEnabled = false;
        if (TabSettings.IsChecked == true) TabMain.IsChecked = true;
        CardDevice.Visibility = Visibility.Collapsed;
        CardNoDevice.Visibility = Visibility.Visible;
        _tray.Text = "WLMouse Hub";
        Status(message);
    }

    private async Task LoadAllAsync()
    {
        var m = _mouse!;
        Status(Loc.T("Reading"));
        ShowBusy(Loc.T("Reading"));
        try
        {
            var info = await Task.Run(() => new
            {
                Count = m.GetProfileCount(),
                Active = m.GetActiveProfile(),
                Fw = m.GetFirmware(),
                RxFw = m.IsWired ? "" : m.GetReceiverFirmware(),
                Battery = m.GetBattery(),
                Eid = m.GetEid(),
            });
            var names = await Task.Run(() => Enumerable.Range(1, info.Count).Select(p => m.GetProfileName(p)).ToList());

            _profileCount = info.Count;
            _profile = Math.Clamp(info.Active, 1, info.Count);
            _mouseFw = info.Fw;
            _rxFw = info.RxFw;
            _battery = info.Battery;
            _eid = info.Eid;

            SetupModelControls(m);
            BuildProfileTabs(names);
            await BuildColorCarouselAsync(m.Model);
            await UpdateMousePictureAsync();
            await LoadProfileAsync();
            await LoadMacrosAsync();

            TabSettings.IsEnabled = true;
            CardNoDevice.Visibility = Visibility.Collapsed;
            CardDevice.Visibility = Visibility.Visible;
            Anim.FadeIn(CardDevice, 24, 450);
            ShowBattery(info.Battery);
            UpdateInfoCards();
            _batteryTimer.Start();
            Status(Loc.F("Connected", m.Model.ModelEN));
            Dispatcher.BeginInvoke(() => PlaceCapsules(false), System.Windows.Threading.DispatcherPriority.Loaded);
            _ = RefreshFirmwarePageAsync(); // lights the update dot on the Firmware tab
            if (StartPanel == "screen" && m.ScreenSupported)
            {
                StartPanel = null;
                _screenMode = true;
                TxtNavLight.SetResourceReference(TextBlock.TextProperty, "S.DongleScreen");
                TabSettings.IsChecked = true;
                NavLight.IsChecked = true;
                Nav_Checked(NavLight, new RoutedEventArgs());
            }
        }
        catch (WlmException ex)
        {
            _loading = false;
            Disconnect(ex.Message);
        }
        finally
        {
            HideBusy();
        }
    }

    // ===================================================================== home / info cards

    private void UpdateInfoCards()
    {
        var m = _mouse;
        if (m == null) return;
        string dpi = _stages.FirstOrDefault(s => s.IsActive)?.X ?? "-";
        string poll = (CmbPolling.SelectedItem as Choice<int>)?.Value.ToString() ?? "-";
        string lod = (CmbLod.SelectedItem as Choice<double>)?.Value.ToString("0.#") ?? "-";

        TxtHomeModel.Text = TxtInfoModel.Text = m.Model.ModelEN;
        TxtHomeConn.Text = m.IsWired ? "" : ""; // USB / wireless
        TxtHomeDpi.Text = TxtInfoDpi.Text = $"DPI:{dpi}";
        TxtHomePoll.Text = TxtInfoPoll.Text = $"PR:{poll}Hz";
        TxtHomeLod.Text = TxtInfoLod.Text = $"LOD:{lod}mm";
        TxtHomeDongle.Text = $"{Loc.T("Dongle")}:{_rxFw}";
        TxtHomeFw.Text = $"{Loc.T("Mouse")}:{_mouseFw}";
        HelpModel.ToolTip = $"{m.Model.ModelEN}\n{(m.IsWired ? Loc.T("Wired") : Loc.T("Wireless"))} · PID {m.ProductId:X4}\n" +
                            $"{Loc.T("Mouse")} {_mouseFw}" + (_rxFw.Length > 0 ? $"\n{Loc.T("Dongle")} {_rxFw}" : "");
    }

    private void ShowBattery((bool Charging, int Percent) b)
    {
        _battery = b;
        string text = $"BAT:{b.Percent}%" + (b.Charging ? " ⚡" : "");
        TxtHomeBat.Text = TxtInfoBat.Text = text;
        Anim.To(BarHomeBat, WidthProperty, Math.Max(0, 110 * b.Percent / 100.0), 700);
        Anim.To(BarInfoBat, WidthProperty, Math.Max(0, 100 * b.Percent / 100.0), 700);
        string model = _mouse?.Model.ModelEN ?? "";
        _tray.Text = $"WLMOUSE {model} – {b.Percent}%{(b.Charging ? " " + Loc.T("Charging") : "")}";
    }

    private async Task RefreshBatteryAsync()
    {
        var m = _mouse;
        if (m == null || _flashing) return;
        try { ShowBattery(await Task.Run(m.GetBattery)); }
        catch (WlmException) { /* wireless mouse asleep */ }
    }

    private void RefreshDynamicTexts()
    {
        UpdateInfoCards();
        RebuildButtonCards();
        RebuildToggles();
        RebuildMacroSlots();
        RefreshEffects();
        if (_mouse != null) SetupSleepLabels();
        _ = RefreshFirmwarePageAsync();
    }

    // ===================================================================== app options / tray

    private void Startup_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (ChkStartup.IsChecked == true) key.SetValue(RunValue, $"\"{Environment.ProcessPath}\" --minimized");
        else key.DeleteValue(RunValue, false);
    }

    private void Tray_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        App.Settings.CloseToTray = ChkCloseToTray.IsChecked == true;
        App.Settings.Save();
    }

    private void SetupTray()
    {
        _tray.Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? System.Drawing.SystemIcons.Application;
        _tray.Text = "WLMouse Hub";
        _tray.Visible = true;
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(Loc.T("Open"), null, (_, _) => ShowFromTray());
        menu.Items.Add(Loc.T("Exit"), null, (_, _) => { _exiting = true; Close(); });
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => ShowFromTray();
    }

    public void BringToFront() => ShowFromTray();

    /// <summary>"--panel screen" opens the Dongle Screen panel once the mouse is connected.</summary>
    public string? StartPanel { get; set; }

    private void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_flashing)
        {
            e.Cancel = true;
            Status(Loc.T("FwBusy"));
            return;
        }
        if (!_exiting && App.Settings.CloseToTray)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _tray.Visible = false;
        _tray.Dispose();
        _mouse?.Dispose();
        base.OnClosed(e);
        Application.Current.Shutdown();
    }

    // ===================================================================== helpers

    /// <summary>Runs a device command off the UI thread; failures go to the status line.</summary>
    private async Task<bool> Run(string? success, Action<WlmMouse> action)
    {
        var m = _mouse;
        if (m == null || _flashing) return false;
        try
        {
            await Task.Run(() => action(m));
            if (success != null) Status(success);
            return true;
        }
        catch (WlmException ex)
        {
            // A wireless mouse that went to sleep does not answer: show the hub's hint instead of a raw error.
            Status(!m.IsWired && ex is not WlmRejectedException ? Loc.T("WakeUp") : ex.Message);
            return false;
        }
    }

    private void Status(string text) => TxtStatus.Text = $"{DateTime.Now:HH:mm:ss}  {text}";

    private static Brush Res(string key) => (Brush)Application.Current.Resources[key];
}
