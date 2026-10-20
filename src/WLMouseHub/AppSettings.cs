using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WLMouseHub.Protocol;

namespace WLMouseHub;

/// <summary>Per-user app preferences (%AppData%\WLMouseHub\settings.json).</summary>
public sealed class AppSettings
{
    public string Theme { get; set; } = "dark";
    public string Language { get; set; } = "";
    public bool CloseToTray { get; set; } = true;
    public List<string> RecentColors { get; set; } = new();

    /// <summary>Colour picked in the Home carousel per model folder (hub: Colors/{id}.png), for mice that do not report it.</summary>
    public Dictionary<string, int> MouseColors { get; set; } = new();

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WLMouseHub", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch { /* corrupt file: start fresh */ }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* not critical */ }
    }
}

/// <summary>Model pictures from the hub (https://gm.wlmouse.gg/Config/…), downloaded once and kept on disk.</summary>
public static class AssetCache
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private static readonly Dictionary<string, ImageSource?> Memory = new();

    /// <summary>Returns the image at a hub path such as "Config/WLX_HUAN/Device_Key.png", or null when unavailable.</summary>
    public static async Task<ImageSource?> GetAsync(string hubPath)
    {
        if (Memory.TryGetValue(hubPath, out var cached)) return cached;
        string local = Path.Combine(ModelCatalog.CacheDir, hubPath.Replace('/', Path.DirectorySeparatorChar));
        try
        {
            if (!File.Exists(local))
            {
                string url = ModelCatalog.HubUrl + string.Join("/", hubPath.Split('/').Select(Uri.EscapeDataString));
                byte[] bytes = await Http.GetByteArrayAsync(url);
                if (bytes.Length < 300 || !LooksLikeImage(bytes)) { Memory[hubPath] = null; return null; } // tiny placeholders / HTML
                Directory.CreateDirectory(Path.GetDirectoryName(local)!);
                await File.WriteAllBytesAsync(local, bytes);
            }
            var img = new BitmapImage();
            img.BeginInit();
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.UriSource = new Uri(local);
            img.EndInit();
            img.Freeze();
            Memory[hubPath] = img;
            return img;
        }
        catch
        {
            Memory[hubPath] = null;
            return null;
        }
    }

    /// <summary>Raw bytes of a hub file (GIF animations are sent to the receiver unchanged).</summary>
    public static async Task<byte[]?> GetBytesAsync(string hubPath)
    {
        string local = Path.Combine(ModelCatalog.CacheDir, hubPath.Replace('/', Path.DirectorySeparatorChar));
        try
        {
            if (File.Exists(local)) return await File.ReadAllBytesAsync(local);
            string url = ModelCatalog.HubUrl + string.Join("/", hubPath.Split('/').Select(Uri.EscapeDataString));
            byte[] bytes = await Http.GetByteArrayAsync(url);
            if (bytes.Length < 300 || !LooksLikeImage(bytes)) return null;
            Directory.CreateDirectory(Path.GetDirectoryName(local)!);
            await File.WriteAllBytesAsync(local, bytes);
            return bytes;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>A small text/JSON file from the hub (cached like images).</summary>
    public static async Task<string?> GetTextAsync(string hubPath)
    {
        string local = Path.Combine(ModelCatalog.CacheDir, hubPath.Replace('/', Path.DirectorySeparatorChar));
        try
        {
            if (File.Exists(local)) return await File.ReadAllTextAsync(local);
            string url = ModelCatalog.HubUrl + string.Join("/", hubPath.Split('/').Select(Uri.EscapeDataString));
            string text = await Http.GetStringAsync(url);
            if (text.TrimStart().StartsWith("<")) return null; // HTML 404 page
            Directory.CreateDirectory(Path.GetDirectoryName(local)!);
            await File.WriteAllTextAsync(local, text);
            return text;
        }
        catch
        {
            return null;
        }
    }

    private static bool LooksLikeImage(byte[] b) =>
        (b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47) ||   // PNG
        (b[0] == 0xFF && b[1] == 0xD8) ||                                   // JPEG
        (b[0] == 0x47 && b[1] == 0x49 && b[2] == 0x46);                     // GIF

    /// <summary>First image that exists out of several candidates.</summary>
    public static async Task<ImageSource?> FirstAsync(params string[] hubPaths)
    {
        foreach (string p in hubPaths)
        {
            var img = await GetAsync(p);
            if (img != null) return img;
        }
        return null;
    }
}
