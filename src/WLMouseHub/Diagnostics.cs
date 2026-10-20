using System;
using System.IO;
using System.Linq;
using System.Text;
using WLMouseHub.Protocol;

namespace WLMouseHub;

/// <summary>Read-only dump of everything the app reads from the mouse (WLMouseHub.exe --dump file.txt).</summary>
public static class Diagnostics
{
    public static void Dump(string path)
    {
        var sb = new StringBuilder();
        try
        {
            using var m = WlmMouse.Find();
            if (m == null)
            {
                sb.AppendLine("No supported WLMOUSE found.");
            }
            else
            {
                sb.AppendLine($"Model {m.Model.ModelEN}, PID {m.ProductId:X4}, {(m.IsWired ? "wired" : "receiver")}");
                sb.AppendLine($"Firmware {m.GetFirmware()}, serial {m.GetSerial()}, battery {m.GetBattery()}, max DPI {m.GetMaxDpi()}");
                int count = m.GetProfileCount();
                sb.AppendLine($"Profiles {count}, active {m.GetActiveProfile()}");
                for (int p = 1; p <= count; p++)
                {
                    sb.AppendLine($"--- Profile {p} \"{m.GetProfileName(p)}\"");
                    sb.AppendLine("  DPI " + string.Join(", ", m.GetDpiStages(p).Select(s => $"{s.X}x{s.Y}")) + $" (active {m.GetActiveDpiStage(p)})");
                    sb.AppendLine("  Colours " + Convert.ToHexString(m.GetDpiColors(p)));
                    sb.AppendLine($"  Polling {m.GetPollingRate(p)} Hz, LOD {m.GetLod(p)} mm, debounce {m.GetDebounce(p)} ms, " +
                                  $"sleep {m.GetSleepSeconds(p)?.ToString() ?? "never"} s, angle tune {m.GetAngleTune(p)}");
                    var flags = Enum.GetValues<WlmMouse.Flag>().Where(f => f switch
                    {
                        WlmMouse.Flag.SeparateXY => m.Model.HasDpiXY,
                        WlmMouse.Flag.HighSpeed or WlmMouse.Flag.Tracking => m.Model.HasHyperMode,
                        WlmMouse.Flag.ButtonCombo => m.Model.HasButtonCombine,
                        WlmMouse.Flag.DpiIndicator => m.Model.HasDpiIndicator,
                        _ => true,
                    });
                    sb.AppendLine("  Flags " + string.Join(", ", flags.Select(f => $"{f}={m.GetFlag(p, f)}")));
                    foreach (byte id in m.Model.ButtonIds.OrderBy(b => b))
                    {
                        var a = m.GetButton(p, id);
                        sb.AppendLine($"  Button {id}: {a.Describe()} [{(byte)a.Type}: {Convert.ToHexString(a.Data)}]");
                    }
                }
                for (int id = 1; id <= WlmMouse.MacroSlots; id++)
                {
                    var data = m.GetMacroData(id);
                    sb.AppendLine($"Macro {id} \"{m.GetMacroName(id)}\": {data.Length} bytes, " +
                                  string.Join(" ", MacroCodec.Decode(data)));
                }
                if (m.LightingSupported) sb.AppendLine($"Light {m.GetLight()}, brightness {m.GetBrightness()}%");
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine("ERROR: " + ex);
        }
        File.WriteAllText(path, sb.ToString());
    }

    /// <summary>Read-only: receiver screen switches and stored pictures, saved next to the report (--screen file.txt).</summary>
    public static void Screen(string path)
    {
        var sb = new StringBuilder();
        try
        {
            using var m = WlmMouse.Find() ?? throw new WlmException("no mouse");
            sb.AppendLine($"{m.Model.ModelEN} wired={m.IsWired} screen={m.ScreenSupported}");
            foreach (var f in Enum.GetValues<ScreenFlag>()) sb.AppendLine($"{f}: {m.GetScreenFlag(f)}");
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var imgs = m.GetScreenImages();
            sb.AppendLine($"{imgs.Count} images read in {sw.Elapsed.TotalSeconds:0.0}s");
            for (int i = 0; i < imgs.Count; i++)
            {
                var img = imgs[i];
                string file = Path.ChangeExtension(path, null) + $"-{i}" + (img.Type == 4 ? ".gif" : ".rgb565");
                File.WriteAllBytes(file, img.Data);
                sb.AppendLine($"  [{i}] {img.Width}x{img.Height} type {img.Type} {img.Data.Length} bytes -> {file}");
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine("ERROR: " + ex);
        }
        File.WriteAllText(path, sb.ToString());
    }

    /// <summary>
    /// Screen write check (--screen-roundtrip file.txt): reads the stored pictures, writes the very same pictures back,
    /// reads them again and compares. The receiver ends up with the content it had.
    /// </summary>
    public static void ScreenRoundTrip(string path)
    {
        var sb = new StringBuilder();
        try
        {
            using var m = WlmMouse.Find() ?? throw new WlmException("no mouse");
            var before = m.GetScreenImages();
            sb.AppendLine($"read {before.Count} images");
            if (before.Count == 0) throw new WlmException("nothing stored to write back");
            var sw = System.Diagnostics.Stopwatch.StartNew();
            m.UploadScreenImages(before);
            sb.AppendLine($"wrote back in {sw.Elapsed.TotalSeconds:0.0}s");
            System.Threading.Thread.Sleep(500);
            var after = m.GetScreenImages();
            sb.AppendLine($"read again {after.Count} images");
            for (int i = 0; i < Math.Max(before.Count, after.Count); i++)
            {
                bool same = i < before.Count && i < after.Count && before[i].Type == after[i].Type &&
                            before[i].Width == after[i].Width && before[i].Data.AsSpan().SequenceEqual(after[i].Data);
                sb.AppendLine($"  [{i}] {(same ? "OK  identical" : "FAIL differs")}");
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine("ERROR: " + ex);
        }
        File.WriteAllText(path, sb.ToString());
    }

    /// <summary>Parses every firmware file of every model and prints sizes and packet fingerprints (--fwcheck file.txt).</summary>
    public static void FirmwareCheck(string path)
    {
        var sb = new StringBuilder();
        foreach (var m in DeviceModel.All)
        {
            foreach (string f in new[] { m.DeviceFWFile_00, m.DeviceFWFile_01, m.Receiver4K8KFWFile_00, m.Receiver4K8KFWFile_01 })
            {
                if (string.IsNullOrEmpty(f)) continue;
                try
                {
                    var img = System.Threading.Tasks.Task.Run(() => FirmwareUpdater.DownloadAsync(m, f)).GetAwaiter().GetResult();
                    sb.AppendLine($"{m.FWFolder}/{f} {img.Data.Length} {img.Packets.Count} {img.Fingerprint()}");
                }
                catch (Exception ex)
                {
                    sb.AppendLine($"{m.FWFolder}/{f} ERR {ex.Message}");
                }
            }
        }
        File.WriteAllText(path, sb.ToString());
    }

    /// <summary>
    /// Write-path check (WLMouseHub.exe --roundtrip profile file.txt): writes every setting of one profile back
    /// with its current value and reads it again; then stores, reads back and deletes a test macro in an empty slot.
    /// </summary>
    public static void RoundTrip(int p, string path)
    {
        var sb = new StringBuilder();
        void Check(string what, object before, object after) =>
            sb.AppendLine($"{(Equals(before, after) ? "OK  " : "FAIL")} {what}: {before} -> {after}");
        try
        {
            using var m = WlmMouse.Find() ?? throw new WlmException("no mouse");
            var stages = m.GetDpiStages(p);
            m.SetDpiStages(p, stages);
            Check("DPI stages", string.Join(",", stages), string.Join(",", m.GetDpiStages(p)));
            int active = m.GetActiveDpiStage(p); m.SetActiveDpiStage(p, active); Check("active stage", active, m.GetActiveDpiStage(p));
            var colors = m.GetDpiColors(p); m.SetDpiColors(p, colors); Check("DPI colours", Convert.ToHexString(colors), Convert.ToHexString(m.GetDpiColors(p)));
            int poll = m.GetPollingRate(p); m.SetPollingRate(p, poll); Check("polling", poll, m.GetPollingRate(p));
            double lod = m.GetLod(p); m.SetLod(p, lod); Check("LOD", lod, m.GetLod(p));
            int deb = m.GetDebounce(p); m.SetDebounce(p, deb); Check("debounce", deb, m.GetDebounce(p));
            int? sleep = m.GetSleepSeconds(p); m.SetSleepSeconds(p, sleep); Check("sleep", sleep ?? -1, m.GetSleepSeconds(p) ?? -1);
            int tune = m.GetAngleTune(p); m.SetAngleTune(p, tune); Check("angle tune", tune, m.GetAngleTune(p));
            foreach (var f in new[] { WlmMouse.Flag.MotionSync, WlmMouse.Flag.Ripple, WlmMouse.Flag.AngleSnap, WlmMouse.Flag.ButtonCombo })
            {
                bool v = m.GetFlag(p, f); m.SetFlag(p, f, v); Check(f.ToString(), v, m.GetFlag(p, f));
            }
            foreach (byte id in m.Model.ButtonIds)
            {
                var a = m.GetButton(p, id); m.SetButton(p, id, a);
                Check($"button {id}", Convert.ToHexString(a.Data) + (byte)a.Type, Convert.ToHexString(m.GetButton(p, id).Data) + (byte)m.GetButton(p, id).Type);
            }

            int slot = Enumerable.Range(1, WlmMouse.MacroSlots).LastOrDefault(i => m.GetMacroData(i).Length == 0);
            if (slot == 0) sb.AppendLine("SKIP macro: no empty slot");
            else
            {
                var events = new[]
                {
                    new MacroEvent(MacroEventKind.KeyDown, 0x04), new MacroEvent(MacroEventKind.Delay, 30),
                    new MacroEvent(MacroEventKind.KeyUp, 0x04), new MacroEvent(MacroEventKind.Delay, 300),
                    new MacroEvent(MacroEventKind.MouseDown, 1), new MacroEvent(MacroEventKind.MouseUp, 1),
                };
                byte[] data = MacroCodec.Encode(events);
                m.SetMacroData(slot, data);
                m.SetMacroName(slot, "roundtrip");
                Check($"macro {slot} data", Convert.ToHexString(data), Convert.ToHexString(m.GetMacroData(slot)));
                Check($"macro {slot} name", "roundtrip", m.GetMacroName(slot));
                m.DeleteMacro(slot);
                Check($"macro {slot} deleted", 0, m.GetMacroData(slot).Length);
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine("ERROR: " + ex);
        }
        File.WriteAllText(path, sb.ToString());
    }
}
