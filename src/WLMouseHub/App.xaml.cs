using System;
using System.Globalization;
using System.Linq;
using System.Windows;

namespace WLMouseHub;

public partial class App : Application
{
    public static AppSettings Settings { get; private set; } = new();
    private static System.Threading.Mutex? _single;

    public static void SetTheme(string theme)
    {
        Settings.Theme = theme == "light" ? "light" : "dark";
        var merged = Current.Resources.MergedDictionaries;
        merged[0] = new ResourceDictionary { Source = new Uri($"Themes/{(Settings.Theme == "light" ? "Light" : "Dark")}.xaml", UriKind.Relative) };
        Settings.Save();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        string? Arg(string name)
        {
            int i = Array.FindIndex(e.Args, a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
            return i < 0 ? null : i + 1 < e.Args.Length ? e.Args[i + 1] : "";
        }

        if (Arg("--dump") is { } dump)
        {
            Diagnostics.Dump(dump.Length > 0 ? dump : "wlmouse-dump.txt");
            Shutdown();
            return;
        }
        if (Arg("--screen-roundtrip") is { } srt)
        {
            Diagnostics.ScreenRoundTrip(srt.Length > 0 ? srt : "wlmouse-screen-rt.txt");
            Shutdown();
            return;
        }
        if (Arg("--screen") is { } screen)
        {
            Diagnostics.Screen(screen.Length > 0 ? screen : "wlmouse-screen.txt");
            Shutdown();
            return;
        }
        if (Arg("--fwcheck") is { } fw)
        {
            Diagnostics.FirmwareCheck(fw.Length > 0 ? fw : "wlmouse-fwcheck.txt");
            Shutdown();
            return;
        }
        int rt = Array.FindIndex(e.Args, a => a.Equals("--roundtrip", StringComparison.OrdinalIgnoreCase));
        if (rt >= 0 && rt + 2 < e.Args.Length && int.TryParse(e.Args[rt + 1], out int profile))
        {
            Diagnostics.RoundTrip(profile, e.Args[rt + 2]);
            Shutdown();
            return;
        }

        // One copy only: two apps talking to the same mouse at once mix up each other's replies.
        _single = new System.Threading.Mutex(true, "WLMouseHub.SingleInstance", out bool first);
        var showSignal = new System.Threading.EventWaitHandle(false, System.Threading.EventResetMode.AutoReset, "WLMouseHub.Show");
        if (!first)
        {
            showSignal.Set();
            Shutdown();
            return;
        }

        // Older builds did not have the lock above: close any of them still running, so only this copy talks to the mouse.
        foreach (var other in System.Diagnostics.Process.GetProcessesByName("WLMouseHub"))
        {
            if (other.Id == Environment.ProcessId) continue;
            try { other.Kill(); other.WaitForExit(2000); } catch { /* already gone or not ours */ }
        }

        Settings = AppSettings.Load();
        if (string.IsNullOrEmpty(Settings.Language))
        {
            string ui = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            Settings.Language = ui is "ru" or "zh" ? ui : "en";
        }
        SetTheme(Settings.Theme);
        Loc.Apply(Arg("--lang") ?? Settings.Language);
        if (Arg("--theme") is { Length: > 0 } theme) SetTheme(theme);

        var window = new MainWindow { StartPanel = Arg("--panel") };
        // a second launch asks this copy to come to the front
        var listener = new System.Threading.Thread(() =>
        {
            while (showSignal.WaitOne())
                window.Dispatcher.BeginInvoke(window.BringToFront);
        }) { IsBackground = true };
        listener.Start();
        if (e.Args.Any(a => a.Equals("--minimized", StringComparison.OrdinalIgnoreCase))) window.StartHidden();
        else window.Show();
    }
}
