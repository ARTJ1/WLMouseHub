using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using WLMouseHub.Hid;
using WLMouseHub.Protocol;

namespace WLMouseHub;

/// <summary>Firmware page: versions, release notes and the update flow.</summary>
public partial class MainWindow
{
    private static readonly HttpClient NotesHttp = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly Dictionary<string, string> _notesCache = new();
    private FirmwarePlan? _rxPlan, _mousePlan;

    private async Task RefreshFirmwarePageAsync()
    {
        if (TxtFwTitle == null || _flashing) return;
        var model = _mouse?.Model;
        if (model == null)
        {
            TxtFwTitle.Text = Loc.T("Firmware");
            SetFwRow(BtnFwRx, TxtFwRxNew, TxtFwRxCur, null, "", Loc.T("NotConnected"), false);
            SetFwRow(BtnFwMouse, TxtFwMouseNew, TxtFwMouseCur, null, "", Loc.T("NotConnected"), false);
            TxtFwNotes.Inlines.Clear();
            return;
        }

        TxtFwTitle.Text = Loc.F("FwTitle", model.ModelEN);
        _rxPlan = FirmwarePlan.For(model, FirmwareTarget.Receiver);
        _mousePlan = FirmwarePlan.For(model, FirmwareTarget.Mouse);

        // Receiver: through the current connection, or plugged in separately while the mouse is on the cable.
        string rxVersion = _mouse!.IsWired ? await ReadSeparateReceiverVersionAsync(_rxPlan!) : _rxFw;
        string rxNew = _rxPlan!.AvailableVersion;
        if (!_rxPlan.Supported)
            SetFwRow(BtnFwRx, TxtFwRxNew, TxtFwRxCur, rxNew, rxVersion, Loc.T("FwUnsupported"), false);
        else if (rxVersion.Length == 0)
            SetFwRow(BtnFwRx, TxtFwRxNew, TxtFwRxCur, rxNew, rxVersion, Loc.T("NotConnected"), false);
        else if (FirmwareImage.IsNewer(rxVersion, rxNew))
            SetFwRow(BtnFwRx, TxtFwRxNew, TxtFwRxCur, rxNew, rxVersion, Loc.T("Update"), true);
        else
            SetFwRow(BtnFwRx, TxtFwRxNew, TxtFwRxCur, rxNew, rxVersion, Loc.T("Latest"), false);

        string mouseNew = _mousePlan!.AvailableVersion;
        if (!_mousePlan.Supported)
            SetFwRow(BtnFwMouse, TxtFwMouseNew, TxtFwMouseCur, mouseNew, _mouseFw, Loc.T("FwUnsupported"), false);
        else if (!_mouse.IsWired)
            SetFwRow(BtnFwMouse, TxtFwMouseNew, TxtFwMouseCur, mouseNew, _mouseFw, Loc.T("CableRequired"), false);
        else if (FirmwareImage.IsNewer(_mouseFw, mouseNew))
            SetFwRow(BtnFwMouse, TxtFwMouseNew, TxtFwMouseCur, mouseNew, _mouseFw, Loc.T("Update"), true);
        else
            SetFwRow(BtnFwMouse, TxtFwMouseNew, TxtFwMouseCur, mouseNew, _mouseFw, Loc.T("Latest"), false);

        bool updateAvailable = BtnFwRx.IsEnabled || BtnFwMouse.IsEnabled;
        FwDot.Visibility = updateAvailable ? Visibility.Visible : Visibility.Collapsed;
        Anim.Twinkle(FwDot, updateAvailable);
        ShowReleaseNotes(await GetReleaseNotesAsync(model));
    }

    /// <summary>Like the hub: text inside 'single quotes' is a bold heading, the quotes are not shown.</summary>
    private void ShowReleaseNotes(string notes)
    {
        TxtFwNotes.Inlines.Clear();
        foreach (string line in notes.Split('\n'))
        {
            var parts = line.Split('\'');
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].Length == 0) continue;
                var run = new System.Windows.Documents.Run(parts[i]);
                if (i % 2 == 1) run.FontWeight = FontWeights.Bold;
                TxtFwNotes.Inlines.Add(run);
            }
            TxtFwNotes.Inlines.Add(new System.Windows.Documents.LineBreak());
        }
    }

    private static void SetFwRow(Button button, TextBlock newText, TextBlock curText, string? available, string current, string action, bool enabled)
    {
        newText.Text = available is { Length: > 0 } ? Loc.F("NewFw", available) : "";
        curText.Text = Loc.F("CurrentFw", current.Length > 0 ? current : Loc.T("NotConnected"));
        button.Content = action;
        button.IsEnabled = enabled;
    }

    private static async Task<string> ReadSeparateReceiverVersionAsync(FirmwarePlan plan)
    {
        if (plan.Pid == 0) return "";
        return await Task.Run(() =>
        {
            try
            {
                var hid = HidDeviceInfo.Enumerate(plan.Vid).FirstOrDefault(d => d.ProductId == plan.Pid && d.FeatureLength == WlmTransport.ReportSize);
                if (hid == null) return "";
                using var io = new WlmTransport(hid.Path);
                var r = io.Transact(0, 16, 0, 0x81);
                return $"{r[7]}.{r[8]}.{r[9]}.{r[10]}";
            }
            catch (WlmException) { return ""; }
        });
    }

    private async Task<string> GetReleaseNotesAsync(DeviceModel model)
    {
        string lang = Loc.Lang == "zh" ? "ZH" : "EN";
        string key = model.FWFolder + "/" + lang;
        if (_notesCache.TryGetValue(key, out var cached)) return cached;
        try
        {
            string url = ModelCatalog.HubUrl + "Config/fwfiles/" + Uri.EscapeDataString(model.FWFolder) + "/release_notes/" + lang + ".txt";
            string text = await NotesHttp.GetStringAsync(url);
            if (text.TrimStart().StartsWith("<")) text = ""; // HTML fallback page, not notes
            _notesCache[key] = text.Replace("\r\n", "\n").Trim();
        }
        catch
        {
            _notesCache[key] = "";
        }
        return _notesCache[key];
    }

    private async void FirmwareRx_Click(object sender, RoutedEventArgs e)
    {
        if (_rxPlan != null) await RunFirmwareUpdateAsync(_rxPlan, Loc.T("Receiver8K"), BarFwRx, BtnFwRx);
    }

    private async void FirmwareMouse_Click(object sender, RoutedEventArgs e)
    {
        if (_mousePlan != null) await RunFirmwareUpdateAsync(_mousePlan, Loc.T("Mouse"), BarFwMouse, BtnFwMouse);
    }

    private async Task RunFirmwareUpdateAsync(FirmwarePlan plan, string what, FrameworkElement bar, Button button)
    {
        if (_flashing) return;
        if (!ThemedDialog.Confirm(Loc.T("Firmware"), Loc.F("FwConfirm", what, plan.AvailableVersion))) return;

        _flashing = true;
        _scanTimer.Stop();
        _batteryTimer.Stop();
        _mouse?.Dispose();
        _mouse = null;
        TabMain.IsEnabled = TabSettings.IsEnabled = false;
        BtnFwRx.IsEnabled = BtnFwMouse.IsEnabled = false;
        button.Content = Loc.T("FwBusy");

        var progress = new Progress<(string Message, int Percent)>(p =>
        {
            TxtFwMessage.Text = p.Message;
            if (p.Percent >= 0) bar.Width = button.ActualWidth * p.Percent / 100.0;
        });
        try
        {
            await new FirmwareUpdater(plan, progress).RunAsync();
            ThemedDialog.Info(Loc.T("Firmware"), TxtFwMessage.Text);
        }
        catch (Exception ex) when (ex is WlmException or HttpRequestException or TaskCanceledException)
        {
            TxtFwMessage.Text = ex.Message;
            ThemedDialog.Info(Loc.T("Error"), ex.Message);
        }
        finally
        {
            bar.Width = 0;
            _flashing = false;
            TabMain.IsEnabled = true;
            _scanTimer.Start();
            Disconnect(Loc.T("Searching"));
            TabFirmware.IsChecked = true;
            await ScanAsync();
            await RefreshFirmwarePageAsync();
        }
    }
}
