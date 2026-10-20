using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using WLMouseHub.Hid;

namespace WLMouseHub.Protocol;

public enum FirmwareTarget { Mouse, Receiver }

/// <summary>What can be flashed for one model and target, from the hub database.</summary>
public sealed record FirmwarePlan(
    DeviceModel Model, FirmwareTarget Target,
    ushort Vid, ushort Pid, ushort BootVid, ushort BootPid,
    string File0, string File1, bool OneMcu)
{
    public string AvailableVersion => FirmwareImage.VersionFromFileName(File0);
    public bool Supported => File0.Length > 0 && Pid != 0 && BootPid != 0
                             && !(Target == FirmwareTarget.Receiver && Model.UsesWideReceiverChannel && File1.Length > 0);

    public static FirmwarePlan? For(DeviceModel m, FirmwareTarget target)
    {
        static ushort Hex(string s) => ushort.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out var v) ? v : (ushort)0;
        return target == FirmwareTarget.Mouse
            ? new FirmwarePlan(m, target, Hex(m.VIDWired), Hex(m.PIDWired), Hex(m.DeviceBLVID), Hex(m.DeviceBLPID),
                m.DeviceFWFile_00, m.DeviceFWFile_01, OneMcu: false)
            : new FirmwarePlan(m, target, Hex(m.VIDWired), Hex(m.D8KDongle), Hex(m.Receiver4K8KBLVID), Hex(m.Receiver4K8KBLPID),
                m.Receiver4K8KFWFile_00, m.Receiver4K8KFWFile_01, OneMcu: m.ReceiverOneMcuMultiHex == "1");
    }
}

/// <summary>Flashes mouse or 8K receiver firmware exactly the way WL MOUSE HUB does (64-byte channel).</summary>
public sealed class FirmwareUpdater
{
    public const string FirmwareBaseUrl = "https://gm.wlmouse.gg/Config/fwfiles/";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };

    private readonly FirmwarePlan _plan;
    private readonly IProgress<(string Message, int Percent)> _progress;

    public FirmwareUpdater(FirmwarePlan plan, IProgress<(string, int)> progress)
    {
        _plan = plan;
        _progress = progress;
    }

    public static async Task<FirmwareImage> DownloadAsync(DeviceModel model, string file, CancellationToken ct = default)
    {
        string url = FirmwareBaseUrl + Uri.EscapeDataString(model.FWFolder) + "/" + Uri.EscapeDataString(file);
        string text = await Http.GetStringAsync(url, ct).ConfigureAwait(false);
        if (!text.TrimStart().StartsWith(':')) throw new WlmException($"{file} is not an Intel HEX file.");
        return FirmwareImage.Parse(text);
    }

    private void Report(string text, int percent = -1) => _progress.Report((text, percent));

    /// <summary>Runs the whole update. The device must be connected in normal mode (mouse: by cable).</summary>
    public async Task RunAsync(CancellationToken ct = default)
    {
        if (!_plan.Supported) throw new WlmException("This device's firmware can only be updated with the official hub.");

        Report("Downloading firmware…");
        var image0 = await DownloadAsync(_plan.Model, _plan.File0, ct);
        var image1 = _plan.File1.Length > 0 ? await DownloadAsync(_plan.Model, _plan.File1, ct) : null;

        await Task.Run(() => Flash(image0, image1, ct), ct);
    }

    private void Flash(FirmwareImage image0, FirmwareImage? image1, CancellationToken ct)
    {
        // 1. Normal mode → bootloader.
        using (var normal = OpenFirst(_plan.Vid, _plan.Pid))
        {
            if (normal == null) throw new WlmException("Device not found in normal mode.");
            Report("Switching to bootloader…");
            normal.SetRaw(Command(0, 1, 0, 0, 0xB0));
        }
        Thread.Sleep(500);

        var boot = WaitForDevice(_plan.BootVid, _plan.BootPid, TimeSpan.FromSeconds(25))
                   ?? throw new WlmException("The bootloader did not appear. Unplug and replug the device, then try again.");
        try
        {
            string bl = WaitBootloaderVersion(boot, 0, resendEnter: false);
            Report($"Bootloader {bl}");
            Thread.Sleep(1000);

            // 2. Chip 0.
            WriteChip(boot, 0, image0, programDelay: 1, pageDelay: 1, verifyDelay: 1, ct);

            bool mouse = _plan.Target == FirmwareTarget.Mouse;
            bool special = _plan.Model.FWFolder.Contains("WLX_HUANM") || _plan.Model.FWFolder.Contains("WL_X_V2");
            bool resetDefaults = mouse && !special;

            // The hub leaves the bootloader after chip 0 for mice, for multi-MCU receivers, and when there is no chip 1.
            bool exitAfterChip0 = mouse || !_plan.OneMcu || image1 == null;
            if (exitAfterChip0)
            {
                Report("Restarting…");
                boot.SetRaw(Command(0, 1, 0xB0, 0x04, (byte)(mouse && special ? 0x01 : 0xB0)));
                boot.Dispose();
                if (image1 == null)
                {
                    FinishInNormalMode(chip: 0, resetDefaults);
                    return;
                }
                Thread.Sleep(mouse ? 1000 : 2000);
                boot = WaitForDevice(_plan.Vid, _plan.Pid, TimeSpan.FromSeconds(25))
                       ?? throw new WlmException("Device did not come back after the first chip.");
                if (resetDefaults)
                {
                    boot.SendOnly(2, 2, 0, 0x00, 0xC0, 0x01);
                    Thread.Sleep(1000);
                }
            }

            // 3. Chip 1. One-MCU receivers continue in the same bootloader session.
            if (!_plan.OneMcu)
            {
                boot.SetRaw(Command(1, 1, 0, 0, 0xB0));
                Thread.Sleep(50);
            }
            if (exitAfterChip0)
            {
                bl = WaitBootloaderVersion(boot, 1, resendEnter: true);
                Report($"Bootloader (chip 2) {bl}");
                Thread.Sleep(500);
            }
            WriteChip(boot, 1, image1!, programDelay: 4, pageDelay: 5, verifyDelay: 5, ct);
            Report("Restarting…");
            boot.SetRaw(Command(1, 1, 0xB0, 0x04, 0xB0));
            boot.Dispose();
            FinishInNormalMode(chip: 1, resetDefaults: false);
        }
        finally
        {
            boot.Dispose();
        }
    }

    private void WriteChip(WlmTransport dev, byte chip, FirmwareImage image, int programDelay, int pageDelay, int verifyDelay, CancellationToken ct)
    {
        Report($"Erasing chip {chip + 1}…", 0);
        var erase = Command(chip, 8, 0xB0, 0x01);
        var r = SendAndWait(dev, erase, delayUs: 100_000, accept: StatusOk);
        if (r == null) throw new WlmException("Erase failed.");

        int cache = 4096;
        int total = image.Packets.Count;
        for (int i = 0; i < total; i++)
        {
            ct.ThrowIfCancellationRequested();
            var packet = (byte[])image.Packets[i].Clone();
            packet[3] = chip;
            int baseDelay;
            if (cache > 32) { cache -= 32; baseDelay = programDelay; }
            else { cache = 4096; baseDelay = pageDelay; }

            bool ok = false;
            for (int attempt = 0, extraUs = 0; attempt < 5 && !ok; attempt++, extraUs += 500)
            {
                var reply = SendAndWait(dev, packet, baseDelay * 1000 + extraUs, rep => StatusOk(rep) && EchoMatches(packet, rep));
                ok = reply != null;
            }
            if (!ok) throw new WlmException($"Programming failed at packet {i + 1} of {total}. Do not unplug; run the update again.");
            if (i % 16 == 0 || i == total - 1) Report($"Program: {(i + 1) * 100 / total}%", (i + 1) * 100 / total);
        }

        // Verify: read back every packet's address and compare with the image.
        var readBack = new List<byte>(image.Data.Length);
        for (int i = 0; i < total; i++)
        {
            ct.ThrowIfCancellationRequested();
            var packet = image.Packets[i];
            var req = Command(chip, 32, 0xB0, 0x83, 32);
            Array.Copy(packet, 8, req, 8, 4);
            byte[]? reply = null;
            for (int attempt = 0, extraUs = 0; attempt < 5 && reply == null; attempt++, extraUs += 500)
                reply = SendAndWait(dev, req, verifyDelay * 1000 + extraUs, rep => StatusOk(rep) && EchoMatches(packet, rep), polls: 30);
            if (reply == null) throw new WlmException($"Verify failed at packet {i + 1}. Run the update again.");
            for (int k = 0; k < packet[7]; k++) readBack.Add((byte)(reply[12 + k] ^ 0x55));
            if (i % 16 == 0 || i == total - 1) Report($"Verify: {(i + 1) * 100 / total}%", (i + 1) * 100 / total);
        }
        for (int k = 0; k < image.Data.Length; k++)
            if (k >= readBack.Count || readBack[k] != image.Data[k])
                throw new WlmException("Verify failed: the written firmware does not match. Run the update again.");
        Report("Verify OK", 100);
    }

    private void FinishInNormalMode(byte chip, bool resetDefaults)
    {
        Report("Waiting for the device to restart…");
        Thread.Sleep(1000);
        using var dev = WaitForDevice(_plan.Vid, _plan.Pid, TimeSpan.FromSeconds(100))
                        ?? throw new WlmException("Update written, but the device did not come back. Unplug and replug it.");
        string version = "";
        for (int i = 0; i < 50; i++)
        {
            try
            {
                var r = dev.Transact(chip, 16, 0, 0x81);
                version = $"{r[7]}.{r[8]}.{r[9]}.{r[10]}";
                if (version != "0.0.0.0") break;
            }
            catch (WlmException) { }
            Thread.Sleep(500);
        }
        if (resetDefaults)
        {
            dev.SendOnly(2, 2, 0, 0x00, 0xC0, 0x01);
            Thread.Sleep(1000);
        }
        Report($"Update complete. Firmware {version}", 100);
    }

    // ---- helpers ------------------------------------------------------------------------

    private static byte[] Command(byte chip, byte length, byte cls, byte cmd, params byte[] payload)
    {
        var b = new byte[WlmTransport.ReportSize];
        b[3] = chip;
        b[4] = length;
        b[5] = cls;
        b[6] = cmd;
        Array.Copy(payload, 0, b, 7, payload.Length);
        return b;
    }

    private static bool StatusOk(byte[] r) => r[1] == 0xA1 || r[1] == 0x02;

    private static bool EchoMatches(byte[] packet, byte[] reply)
    {
        int size = packet[7];
        for (int i = 0; i < 4; i++) if (packet[8 + i] != reply[8 + i]) return false;
        for (int i = 0; i < size; i++) if (packet[12 + i] != reply[12 + i]) return false;
        return true;
    }

    private static byte[]? SendAndWait(WlmTransport dev, byte[] request, int delayUs, Func<byte[], bool> accept, int polls = 30)
    {
        dev.SetRaw(request);
        SleepUs(delayUs);
        for (int i = 0; i < polls; i++)
        {
            var reply = dev.GetRaw();
            if (accept(reply)) return reply;
            if (reply[1] > 0xA1) return null; // error: caller resends
            SleepUs((i + 1) * 1000);
        }
        return null;
    }

    private static void SleepUs(int us)
    {
        if (us >= 2000) { Thread.Sleep(us / 1000); return; }
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed.TotalMilliseconds * 1000 < us) Thread.SpinWait(50);
    }

    private string WaitBootloaderVersion(WlmTransport dev, byte chip, bool resendEnter)
    {
        for (int i = 0; i < 50; i++)
        {
            try
            {
                dev.SetRaw(Command(chip, 6, 0xB0, 0x80));
                Thread.Sleep(100);
                var r = dev.GetRaw();
                if (StatusOk(r))
                {
                    string v = $"{r[9]:x}.{r[10]:x2}.{r[11]:x2}.{r[12]:x2}";
                    if (v.ToUpperInvariant().Contains('B')) return v;
                }
            }
            catch (WlmException) { }
            Thread.Sleep(500);
            if (resendEnter && i != 0 && i % 4 == 0)
            {
                dev.SetRaw(Command(chip, 1, 0, 0, 0xB0));
                Thread.Sleep(500);
            }
        }
        throw new WlmException("The device did not enter bootloader mode.");
    }

    private static WlmTransport? OpenFirst(ushort vid, ushort pid)
    {
        var hid = HidDeviceInfo.Enumerate(vid)
            .FirstOrDefault(d => d.ProductId == pid && d.FeatureLength == WlmTransport.ReportSize);
        return hid == null ? null : new WlmTransport(hid.Path);
    }

    private static WlmTransport? WaitForDevice(ushort vid, ushort pid, TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            try
            {
                var dev = OpenFirst(vid, pid);
                if (dev != null) return dev;
            }
            catch (WlmException) { }
            Thread.Sleep(500);
        }
        return null;
    }
}
