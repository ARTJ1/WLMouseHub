using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using WLMouseHub.Hid;

namespace WLMouseHub.Protocol;

public sealed record DpiStage(int X, int Y);

public sealed record LightState(int Effect, int SpeedLevel, byte R, byte G, byte B);

/// <summary>A connected WLMOUSE (wired, or through its receiver) and every configuration command.</summary>
public sealed partial class WlmMouse : IDisposable
{
    public const ushort VendorId = 0x36A7;
    private const byte Mouse = 2;

    private static readonly Dictionary<int, int> PollCodeToHz = new()
        { [8] = 125, [4] = 250, [2] = 500, [1] = 1000, [16] = 1000, [32] = 2000, [64] = 4000, [128] = 8000 };

    private readonly WlmTransport _io;
    private static readonly Encoding Gbk;

    static WlmMouse()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Gbk = Encoding.GetEncoding(936);
    }

    public DeviceModel Model { get; }
    public ushort ProductId { get; }
    public bool IsWired { get; }
    public string DevicePath { get; }

    private WlmMouse(WlmTransport io, DeviceModel model, ushort pid, bool wired, string path)
    {
        _io = io;
        Model = model;
        ProductId = pid;
        IsWired = wired;
        DevicePath = path;
    }

    /// <summary>Finds the first supported mouse or receiver. Returns null when none is plugged in.</summary>
    /// <summary>All vendor ids used by WLMOUSE mice and receivers (36A7, plus the 1K receiver variant from the hub database).</summary>
    public static IReadOnlyList<ushort> VendorIds =>
        DeviceModel.All.SelectMany(m => DeviceModel.HexList(m.D1KIdvdVid)).Append(VendorId).Distinct().ToList();

    public static List<HidDeviceInfo> EnumerateAll() =>
        VendorIds.SelectMany(v => HidDeviceInfo.Enumerate(v)).ToList();

    public static WlmMouse? Find()
    {
        var candidates = EnumerateAll()
            .Where(d => d.UsagePage == 0xFFFF && d.FeatureLength == WlmTransport.ReportSize)
            .ToList();

        foreach (var hid in candidates)
        {
            bool vid36 = hid.VendorId == VendorId;
            var wired = vid36 ? DeviceModel.All.FirstOrDefault(m => DeviceModel.HexList(m.PIDWired).Contains(hid.ProductId)) : null;
            var dongle = DeviceModel.All.Where(m => vid36
                ? DeviceModel.HexList(m.PIDWireless4K8K).Contains(hid.ProductId) || DeviceModel.HexList(m.PIDWireless).Contains(hid.ProductId)
                : DeviceModel.HexList(m.D1KIdvdVid).Contains(hid.VendorId) && DeviceModel.HexList(m.D1KIdvdPid).Contains(hid.ProductId)).ToList();
            if (wired == null && dongle.Count == 0) continue;

            var io = new WlmTransport(hid.Path);
            try
            {
                if (wired != null) return new WlmMouse(io, wired, hid.ProductId, true, hid.Path);

                // Several models share a receiver PID: ask the receiver which mouse is paired.
                var model = dongle.Count == 1 ? dongle[0] : IdentifyPaired(io, dongle);
                return new WlmMouse(io, model, hid.ProductId, false, hid.Path);
            }
            catch
            {
                io.Dispose();
                throw;
            }
        }
        return null;
    }

    private static DeviceModel IdentifyPaired(WlmTransport io, List<DeviceModel> options)
    {
        try
        {
            var r = io.Transact(1, 6, 0, 0x8B, 2);
            ushort pid = (ushort)((r[11] << 8) | r[12]);
            return options.FirstOrDefault(m => DeviceModel.HexList(m.PIDWired).Contains(pid)) ?? options[0];
        }
        catch (WlmException)
        {
            return options[0];
        }
    }

    private static int U16(byte[] b, int i) => (b[i] << 8) | b[i + 1];
    private static byte Hi(int v) => (byte)(v >> 8);
    private static byte Lo(int v) => (byte)v;

    // ---- Device info -------------------------------------------------------------------

    public string GetFirmware()
    {
        var r = _io.Transact(Mouse, 16, 0, 0x81);
        return $"{r[7]}.{r[8]}.{r[9]}.{r[10]}";
    }

    public string GetReceiverFirmware()
    {
        var r = _io.Transact(0, 16, 0, 0x81);
        return $"{r[7]}.{r[8]}.{r[9]}.{r[10]}";
    }

    public string GetSerial()
    {
        var r = _io.Transact(Mouse, 20, 0, 0x82);
        return Encoding.ASCII.GetString(r, 9, 15).TrimEnd('\0', ' ');
    }

    public (bool Charging, int Percent) GetBattery()
    {
        var r = _io.Transact(Mouse, 2, 0, 0x83);
        return (r[7] == 1, r[8]);
    }

    /// <summary>
    /// Colour/edition id the mouse reports (hub getEID). 255 means "not reported"; the hub then shows Device_255.png.
    /// </summary>
    public int GetEid()
    {
        var r = _io.Transact(Mouse, 2, 0, 0x82);
        int lo = r[7], hi = r[8];
        int eid = hi is 0 or 0xFF ? lo : lo + (hi << 8);
        return eid == 0 ? 255 : eid;
    }

    public int GetMaxDpi() => U16(_io.Transact(Mouse, 2, 1, 0x8C), 7);

    // ---- Profiles ----------------------------------------------------------------------

    public int GetActiveProfile() => _io.Transact(Mouse, 1, 0, 0x85)[7];
    public void SetActiveProfile(int p) => _io.Transact(Mouse, 1, 0, 0x05, (byte)p);
    public int GetProfileCount() => Math.Max(1, (int)_io.Transact(Mouse, 1, 0, 0x86)[7]);

    public string GetProfileName(int p)
    {
        var r = _io.Transact(Mouse, 58, 0, 0x8E, (byte)p);
        int n = Math.Min((int)r[8], WlmTransport.ReportSize - 9);
        var bytes = r.Skip(9).Take(n).ToArray();
        if (bytes.Contains((byte)0)) return "";
        return Gbk.GetString(bytes);
    }

    public void SetProfileName(int p, string name)
    {
        byte[] bytes = Gbk.GetBytes(name);
        if (bytes.Length > 50) bytes = bytes.Take(50).ToArray();
        var payload = new byte[] { (byte)p, (byte)bytes.Length }.Concat(bytes).ToArray();
        _io.Transact(Mouse, (byte)(bytes.Length + 2), 0, 0x0E, payload);
    }

    public void ResetProfile(int p) => _io.Transact(Mouse, 1, 0, 0x0D, (byte)p);

    /// <summary>Restores every profile to factory defaults. The device does not acknowledge it.</summary>
    public void FactoryReset() => _io.SendOnly(Mouse, 2, 0, 0x00, 0xC0, 0x01);

    // ---- DPI ---------------------------------------------------------------------------

    public List<DpiStage> GetDpiStages(int p)
    {
        var r = _io.Transact(Mouse, 10, 1, 0x81, (byte)p, 6);
        int n = Math.Min((int)r[8], 6);
        var list = new List<DpiStage>();
        for (int k = 0; k < n; k++) list.Add(new DpiStage(U16(r, 9 + 4 * k), U16(r, 11 + 4 * k)));
        return list;
    }

    public void SetDpiStages(int p, IReadOnlyList<DpiStage> stages)
    {
        var payload = new List<byte> { (byte)p, (byte)stages.Count };
        foreach (var s in stages) { payload.Add(Hi(s.X)); payload.Add(Lo(s.X)); payload.Add(Hi(s.Y)); payload.Add(Lo(s.Y)); }
        _io.Transact(Mouse, 26, 1, 0x01, payload.ToArray());
    }

    public int GetActiveDpiStage(int p) => _io.Transact(Mouse, 2, 1, 0x82, (byte)p)[8];
    public void SetActiveDpiStage(int p, int stage) => _io.Transact(Mouse, 2, 1, 0x02, (byte)p, (byte)stage);

    public byte[] GetDpiColors(int p)
    {
        var r = _io.Transact(Mouse, 19, 2, 0x81, (byte)p);
        return r.Skip(8).Take(18).ToArray();
    }

    public void SetDpiColors(int p, byte[] rgb18)
    {
        var payload = new byte[19];
        payload[0] = (byte)p;
        Array.Copy(rgb18, 0, payload, 1, Math.Min(18, rgb18.Length));
        _io.Transact(Mouse, 19, 2, 0x01, payload);
    }

    // ---- Sensor and performance --------------------------------------------------------

    public int GetPollingRate(int p) =>
        PollCodeToHz.TryGetValue(_io.Transact(Mouse, 2, 1, 0x80, (byte)p)[8], out int hz) ? hz : 1000;

    public void SetPollingRate(int p, int hz)
    {
        byte code = (byte)PollCodeToHz.First(kv => kv.Value == hz && kv.Key != 16).Key;
        _io.Transact(Mouse, 2, 1, 0x00, (byte)p, code);
    }

    public int GetDebounce(int p) => _io.Transact(Mouse, 2, 0, 0x88, (byte)p)[8];
    public void SetDebounce(int p, int ms) => _io.Transact(Mouse, 2, 0, 0x08, (byte)p, (byte)ms);

    /// <summary>Seconds until sleep, or null when sleep is disabled.</summary>
    public int? GetSleepSeconds(int p)
    {
        int v = U16(_io.Transact(Mouse, 3, 0, 0x87, (byte)p), 8);
        return v is 0 or 0xFF00 or 0xFFFF ? null : v;
    }

    public void SetSleepSeconds(int p, int? seconds)
    {
        int v = seconds ?? 0xFFFF;
        _io.Transact(Mouse, 3, 0, 0x07, (byte)p, Hi(v), Lo(v));
    }

    public double GetLod(int p)
    {
        byte v = _io.Transact(Mouse, 3, 1, 0x88, (byte)p)[8];
        if (v >= 128) return (v - 128) / 10.0;
        if (v is 1 or 2) return v;
        return ((v >> 4) * 10 + (v & 15)) / 10.0;
    }

    public void SetLod(int p, double mm)
    {
        byte v = mm < 1 ? (byte)(Math.Round(mm * 10) + 128)
            : mm > 1 && mm < 2 ? (byte)(((int)mm << 4) | (int)Math.Round(mm * 10 % 10))
            : (byte)mm;
        _io.Transact(Mouse, 3, 1, 0x08, (byte)p, v, 3);
    }

    public enum Flag { AngleSnap, MotionSync, Ripple, HighSpeed, SeparateXY, Tracking, DpiIndicator, ButtonCombo }

    private static (byte Cls, byte Cmd) FlagCommand(Flag f) => f switch
    {
        Flag.AngleSnap => (1, 0x04),
        Flag.MotionSync => (1, 0x09),
        Flag.Ripple => (1, 0x0A),
        Flag.HighSpeed => (1, 0x0B),
        Flag.SeparateXY => (1, 0x0D),
        Flag.Tracking => (1, 0x13),
        Flag.DpiIndicator => (2, 0x04),
        Flag.ButtonCombo => (3, 0x01),
        _ => throw new ArgumentOutOfRangeException(nameof(f)),
    };

    public bool GetFlag(int p, Flag f)
    {
        var (cls, cmd) = FlagCommand(f);
        return _io.Transact(Mouse, 2, cls, (byte)(cmd | 0x80), (byte)p)[8] == 1;
    }

    public void SetFlag(int p, Flag f, bool on)
    {
        var (cls, cmd) = FlagCommand(f);
        _io.Transact(Mouse, 2, cls, cmd, (byte)p, (byte)(on ? 1 : 0));
    }

    public int GetAngleTune(int p) => (sbyte)_io.Transact(Mouse, 2, 1, 0x94, (byte)p)[8];
    public void SetAngleTune(int p, int degrees) => _io.Transact(Mouse, 2, 1, 0x14, (byte)p, (byte)(sbyte)Math.Clamp(degrees, -30, 30));

    // ---- Lighting ----------------------------------------------------------------------

    public bool LightingSupported => Model.LightingSupported(ProductId);
    private byte LightTarget => IsWired ? (byte)2 : Model.LightDevice;

    // Wave (effect 2) uses its own speed scale; level 1..6 maps to these values before the "7 - x" encoding.
    private static readonly int[] WaveSpeeds = { 28, 48, 68, 88, 108, 128 };

    public LightState GetLight()
    {
        var r = _io.Transact(LightTarget, 26, 2, 0x80, 1);
        int effect = r[9];
        int level = effect == 2
            ? Array.IndexOf(WaveSpeeds, (byte)(7 - r[11])) + 1
            : 7 - r[11];
        return new LightState(effect, Math.Clamp(level, 1, 6), r[12], r[13], r[14]);
    }

    public void SetLight(LightState s)
    {
        int level = Math.Clamp(s.SpeedLevel, 1, 6);
        byte speed = (byte)(7 - (s.Effect == 2 ? WaveSpeeds[level - 1] : level));
        byte p2 = (byte)(s.Effect == 2 ? 1 : 0);
        _io.Transact(LightTarget, 8, 2, 0x00, 1, 0, (byte)s.Effect, p2, speed, s.R, s.G, s.B);
    }

    public int GetBrightness() => _io.Transact(LightTarget, 3, 2, 0x82, 1, (byte)(IsWired ? 1 : 0))[9] * 100 / 255;

    public void SetBrightness(int percent) =>
        _io.Transact(LightTarget, 3, 2, 0x02, 1, (byte)(IsWired ? 1 : 0), (byte)(Math.Clamp(percent, 0, 100) * 255 / 100));

    // ---- Buttons -----------------------------------------------------------------------

    public ButtonAction GetButton(int p, byte button)
    {
        var r = _io.Transact(Mouse, 15, 3, 0x80, (byte)p, button, 0, 0xFF, 10);
        int n = Math.Min((int)r[11], 10);
        return new ButtonAction((ActionType)r[10], r.Skip(12).Take(n).ToArray());
    }

    public void SetButton(int p, byte button, ButtonAction action)
    {
        var payload = new List<byte> { (byte)p, button, 0, (byte)action.Type, (byte)action.Data.Length };
        payload.AddRange(action.Data);
        _io.Transact(Mouse, (byte)(5 + action.Data.Length), 3, 0x00, payload.ToArray());
    }

    // ---- Macros ------------------------------------------------------------------------

    public const int MacroSlots = 4;

    public byte[] GetMacroData(int id)
    {
        byte[] size;
        try
        {
            size = _io.Transact(Mouse, 6, 4, 0x81, Hi(id), Lo(id));
        }
        catch (WlmRejectedException)
        {
            return Array.Empty<byte>(); // empty slot answers with status A2
        }
        int total = (size[9] << 24) | (size[10] << 16) | (size[11] << 8) | size[12];
        if (total <= 0 || total > 64 * 1024) return Array.Empty<byte>();

        var data = new byte[total];
        for (int offset = 0; offset < total; offset += 50)
        {
            int n = Math.Min(50, total - offset);
            var r = _io.Transact(Mouse, (byte)(n + 7), 4, 0x83,
                Hi(id), Lo(id), (byte)(offset >> 24), (byte)(offset >> 16), (byte)(offset >> 8), (byte)offset, (byte)n);
            Array.Copy(r, 14, data, offset, n);
        }
        return data;
    }

    public void SetMacroData(int id, byte[] data)
    {
        int size = data.Length;
        _io.Transact(Mouse, 6, 4, 0x01, Hi(id), Lo(id), (byte)(size >> 24), (byte)(size >> 16), (byte)(size >> 8), (byte)size);
        for (int offset = 0; offset < size; offset += 51)
        {
            int n = Math.Min(51, size - offset);
            var payload = new List<byte> { Hi(id), Lo(id), (byte)(offset >> 24), (byte)(offset >> 16), (byte)(offset >> 8), (byte)offset, (byte)n };
            payload.AddRange(data.Skip(offset).Take(n));
            _io.Transact(Mouse, (byte)(7 + n), 4, 0x03, payload.ToArray());
            Thread.Sleep(10);
        }
    }

    public void DeleteMacro(int id) => _io.Transact(Mouse, 2, 4, 0x02, Hi(id), Lo(id));

    public string GetMacroName(int id)
    {
        var r = _io.Transact(Mouse, 58, 4, 0x86, (byte)id, 56);
        int n = Math.Min((int)r[8], WlmTransport.ReportSize - 9);
        var bytes = r.Skip(9).Take(n).TakeWhile(b => b != 0).ToArray();
        return Gbk.GetString(bytes);
    }

    public void SetMacroName(int id, string name)
    {
        byte[] bytes = Gbk.GetBytes(name);
        if (bytes.Length > 50) bytes = bytes.Take(50).ToArray();
        var payload = new byte[] { (byte)id, (byte)bytes.Length }.Concat(bytes).ToArray();
        _io.Transact(Mouse, (byte)(bytes.Length + 2), 4, 0x06, payload);
    }

    public void Dispose() => _io.Dispose();
}
