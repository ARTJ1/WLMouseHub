using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace WLMouseHub.Protocol;

/// <summary>Per-model capabilities, from the WL MOUSE HUB device database (env-models.json).</summary>
public sealed class DeviceModel
{
    public string ModelEN { get; set; } = "";
    public string ModelCN { get; set; } = "";
    public string UIFolder { get; set; } = "";
    public string FWFolder { get; set; } = "";
    public string KeyPosition { get; set; } = "";
    public string VIDWired { get; set; } = "36A7";
    public string PIDWired { get; set; } = "";
    public string PIDWireless { get; set; } = "";
    public string PIDWireless4K8K { get; set; } = "";
    [JsonPropertyName("_1KDongle")] public string D1KDongle { get; set; } = "";
    [JsonPropertyName("_8KDongle")] public string D8KDongle { get; set; } = "";
    /// <summary>Second 1K receiver variant with its own vendor id (373E:0065 in the hub database).</summary>
    [JsonPropertyName("_1KDongleIDVDVID")] public string D1KIdvdVid { get; set; } = "";
    [JsonPropertyName("_1KDongleIDVDPID")] public string D1KIdvdPid { get; set; } = "";
    public string PollingRateWired { get; set; } = "";
    public string PollingRateWireless { get; set; } = "";
    [JsonPropertyName("_1KDonglePollingRate")] public string D1KDonglePollingRate { get; set; } = "";
    [JsonPropertyName("_8KDonglePollingRate")] public string D8KDonglePollingRate { get; set; } = "";
    public string DPIMax { get; set; } = "26000";
    public string DPIMaxStageNum { get; set; } = "6";
    public string DPISliderStep { get; set; } = "50";
    public string LOD { get; set; } = "1;2";
    public string HyperModeEnable { get; set; } = "0";
    public string AngleTuneEnable { get; set; } = "0";
    public string CombineKeyEnable { get; set; } = "0";
    public string DPIXYEnable { get; set; } = "0";
    public string SleepTimeEnable { get; set; } = "0";
    public string DPIIndicatorEnable { get; set; } = "0";
    public string KeyBindingSeq { get; set; } = "01;04;05;02;03";
    public string LightEnablePID { get; set; } = "";
    public string LightDeviceID { get; set; } = "1";
    public string LightEffects { get; set; } = "";
    public string DefaultDPIColors { get; set; } = "";
    public string ScreenSetEnable { get; set; } = "0";

    public string DeviceBLVID { get; set; } = "";
    public string DeviceBLPID { get; set; } = "";
    public string DeviceFWFile_00 { get; set; } = "";
    public string DeviceFWFile_01 { get; set; } = "";
    public string ReceiverOneMcuMultiHex { get; set; } = "0";
    public string Receiver4K8KBLVID { get; set; } = "";
    public string Receiver4K8KBLPID { get; set; } = "";
    public string Receiver4K8KFWFile_00 { get; set; } = "";
    public string Receiver4K8KFWFile_01 { get; set; } = "";

    [JsonIgnore] public int MaxDpi => ParseInt(DPIMax, 26000);
    [JsonIgnore] public int MaxStages => ParseInt(DPIMaxStageNum, 6);
    [JsonIgnore] public int DpiStep => Math.Max(1, ParseInt(DPISliderStep, 50));
    [JsonIgnore] public bool HasHyperMode => HyperModeEnable == "1";
    [JsonIgnore] public bool HasAngleTune => AngleTuneEnable == "1";
    [JsonIgnore] public bool HasButtonCombine => CombineKeyEnable == "1";
    [JsonIgnore] public bool HasDpiXY => DPIXYEnable == "1";
    [JsonIgnore] public bool HasSleepTime => SleepTimeEnable == "1";
    [JsonIgnore] public bool HasDpiIndicator => DPIIndicatorEnable == "1";
    [JsonIgnore] public byte LightDevice => (byte)ParseInt(LightDeviceID, 1);
    [JsonIgnore] public bool UsesWideReceiverChannel => FWFolder == "WL_X_V2" || UIFolder == "WL_X_V2";

    [JsonIgnore]
    public IReadOnlyList<double> LodOptions =>
        LOD.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(s => double.Parse(s, CultureInfo.InvariantCulture)).ToList();

    [JsonIgnore]
    public IReadOnlyList<int> LightEffectIds =>
        LightEffects.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToList();

    /// <summary>Button ids in the order the hub lists them.</summary>
    [JsonIgnore]
    public IReadOnlyList<byte> ButtonIds =>
        KeyBindingSeq.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(s => byte.Parse(s, NumberStyles.HexNumber)).ToList();

    /// <summary>Marker position (top, left) in pixels over the 410 px tall Device_Key image, for buttons 1..5.</summary>
    public (double Top, double Left)? MarkerFor(byte button)
    {
        var v = KeyPosition.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => double.Parse(s, CultureInfo.InvariantCulture)).ToArray();
        int slot = button switch { 1 => 0, 3 => 1, 2 => 2, 5 => 3, 4 => 4, _ => -1 }; // order: left, middle, right, forward, back
        if (slot < 0 || v.Length < slot * 2 + 2) return null;
        return (v[slot * 2], v[slot * 2 + 1]);
    }

    public bool LightingSupported(ushort pid) => HexList(LightEnablePID).Contains(pid);

    public IReadOnlyList<int> PollingRates(ushort pid, bool wired)
    {
        string list = wired ? PollingRateWired
            : HexList(D8KDongle).Contains(pid) ? D8KDonglePollingRate
            : HexList(D1KDongle).Contains(pid) ? D1KDonglePollingRate
            : PollingRateWireless;
        return list.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToList();
    }

    public static IReadOnlyList<ushort> HexList(string s) =>
        s.Split(';', StringSplitOptions.RemoveEmptyEntries)
         .Select(x => ushort.TryParse(x, NumberStyles.HexNumber, null, out var v) ? v : (ushort)0)
         .Where(v => v != 0).ToList();

    private static int ParseInt(string s, int fallback) => int.TryParse(s, out int v) ? v : fallback;

    public static IReadOnlyList<DeviceModel> All => ModelCatalog.Models;
}

/// <summary>The model database: refreshed from the hub, cached on disk, with a built-in copy as fallback.</summary>
public static class ModelCatalog
{
    public const string HubUrl = "https://gm.wlmouse.gg/";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private static List<DeviceModel>? _models;

    public static string CacheDir { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WLMouseHub", "cache");

    private static string CacheFile => Path.Combine(CacheDir, "env-models.json");

    public static IReadOnlyList<DeviceModel> Models
    {
        get
        {
            if (_models != null) return _models;
            try
            {
                if (File.Exists(CacheFile)) _models = Parse(File.ReadAllText(CacheFile));
            }
            catch { /* fall back to the built-in copy */ }
            if (_models == null || _models.Count == 0)
            {
                using Stream s = typeof(DeviceModel).Assembly.GetManifestResourceStream("models.json")
                    ?? throw new InvalidOperationException("models.json resource missing");
                using var reader = new StreamReader(s);
                _models = Parse(reader.ReadToEnd());
            }
            return _models;
        }
    }

    private static List<DeviceModel> Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var list = new List<DeviceModel>();
        foreach (var e in doc.RootElement.EnumerateArray())
        {
            if (!e.TryGetProperty("PIDWired", out _)) continue; // the first entry is the hub's own settings
            var m = e.Deserialize<DeviceModel>();
            if (m != null && m.ModelEN.Length > 0) list.Add(m);
        }
        return list;
    }

    /// <summary>Downloads the latest model list. Returns true when it changed.</summary>
    public static async Task<bool> RefreshAsync()
    {
        try
        {
            string json = await Http.GetStringAsync(HubUrl + "Config/env-models.json");
            var parsed = Parse(json);
            if (parsed.Count == 0) return false;
            Directory.CreateDirectory(CacheDir);
            string old = File.Exists(CacheFile) ? await File.ReadAllTextAsync(CacheFile) : "";
            await File.WriteAllTextAsync(CacheFile, json);
            _models = parsed;
            return old != json;
        }
        catch
        {
            return false;
        }
    }
}
