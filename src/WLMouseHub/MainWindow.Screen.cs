using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using WLMouseHub.Protocol;

namespace WLMouseHub;

/// <summary>Dongle Screen panel (HUAN receiver display): library, custom pictures, Main/Random slots, switches.</summary>
public partial class MainWindow
{
    private const int RandomSlots = 5;
    private bool _screenMode;             // the 4th nav button shows Dongle Screen instead of Wheel Lighting
    private bool _screenLoaded;
    private ScreenImage? _picked;          // library/custom item chosen for the next slot click
    private FrameworkElement? _pickedTile;
    private ScreenImage? _screenMain;
    private readonly ScreenImage?[] _screenRandom = new ScreenImage?[RandomSlots];

    private static string CustomDir(DeviceModel m) =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WLMouseHub", "screen", m.UIFolder);

    // ===================================================================== navigation

    private void SetupScreenNav()
    {
        bool screen = _mouse?.ScreenSupported == true;
        NavLightChevron.Visibility = screen ? Visibility.Visible : Visibility.Collapsed;
        if (!screen) _screenMode = false;
        TxtNavLight.SetResourceReference(TextBlock.TextProperty, _screenMode ? "S.DongleScreen" : "S.Lighting");
        _screenLoaded = false;
    }

    private void NavLight_Click(object sender, RoutedEventArgs e)
    {
        if (_mouse?.ScreenSupported != true) return;
        var menu = new ContextMenu { PlacementTarget = NavLight, Placement = PlacementMode.Bottom };
        foreach (var (key, screen) in new[] { ("Lighting", false), ("DongleScreen", true) })
        {
            var item = new MenuItem { Header = Loc.T(key), IsChecked = _screenMode == screen };
            item.Click += (_, _) =>
            {
                _screenMode = screen;
                TxtNavLight.SetResourceReference(TextBlock.TextProperty, screen ? "S.DongleScreen" : "S.Lighting");
                Nav_Checked(NavLight, new RoutedEventArgs());
            };
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    private async Task EnsureScreenLoadedAsync()
    {
        var m = _mouse;
        if (m is not { ScreenSupported: true } || _screenLoaded) return;
        _screenLoaded = true;
        RebuildScreenSlots();                       // empty "+" slots right away, even if reading fails
        await BuildScreenLibraryAsync(m.Model);
        BuildScreenCustom(m.Model);

        ShowBusy(Loc.T("ScreenReading"));
        try
        {
            var progress = new Progress<int>(p => TxtOverlay.Text = $"{Loc.T("ScreenReading")} {p}%");
            var (flags, images) = await Task.Run(() =>
            {
                var f = Enum.GetValues<ScreenFlag>().ToDictionary(x => x, m.GetScreenFlag);
                return (f, m.GetScreenImages(progress));
            });
            _loading = true;
            ChkScreenStatus.IsChecked = flags[ScreenFlag.Status];
            ChkScreenEffect.IsChecked = flags[ScreenFlag.Effect];
            ChkScreenVoice.IsChecked = flags[ScreenFlag.Voice];
            _loading = false;
            _screenMain = images.FirstOrDefault();
            for (int i = 0; i < RandomSlots; i++) _screenRandom[i] = images.Skip(1 + i).FirstOrDefault();
            RebuildScreenSlots();
        }
        catch (WlmException ex)
        {
            Status(ex.Message);
            _screenLoaded = false;
        }
        finally
        {
            HideBusy();
        }
    }

    // ===================================================================== library and custom pictures

    private async Task BuildScreenLibraryAsync(DeviceModel model)
    {
        PanelScreenLibrary.Children.Clear();
        string? json = await AssetCache.GetTextAsync($"Config/{model.UIFolder}/Screen/WL/img.json");
        if (json == null) return;
        List<string> names = new();
        try
        {
            using var doc = JsonDocument.Parse(json);
            foreach (var e in doc.RootElement.EnumerateArray())
                if (e.TryGetProperty("name", out var n) && n.GetString() is { } s) names.Add(s);
        }
        catch (JsonException) { return; }

        foreach (string name in names)
        {
            byte[]? bytes = await AssetCache.GetBytesAsync($"Config/{model.UIFolder}/Screen/WL/{name}");
            if (bytes == null) continue;
            var img = ToScreenImage(bytes);
            if (img != null) PanelScreenLibrary.Children.Add(Tile(img, bytes, 26));
        }
    }

    private void BuildScreenCustom(DeviceModel model)
    {
        PanelScreenCustom.Children.Clear();
        string dir = CustomDir(model);
        if (!Directory.Exists(dir)) return;
        foreach (string file in Directory.GetFiles(dir).OrderBy(File.GetCreationTime))
        {
            byte[] bytes = File.ReadAllBytes(file);
            var img = ToScreenImage(bytes);
            if (img == null) continue;
            var tile = Tile(img, bytes, 26);
            tile.Tag = (img, file);
            PanelScreenCustom.Children.Add(tile);
        }
    }

    /// <summary>GIF 224x224 → sent unchanged (type 4). PNG/JPG → centre-cropped to 360x360 RGB565 (type 1).</summary>
    private static ScreenImage? ToScreenImage(byte[] bytes)
    {
        try
        {
            if (bytes.Length > 3 && bytes[0] == 'G' && bytes[1] == 'I' && bytes[2] == 'F')
            {
                var frames = GifPlayer.Decode(bytes);
                if (frames.Count == 0) return null;
                var f = frames[0].Frame;
                return f.PixelWidth == WlmMouse.GifSide && f.PixelHeight == WlmMouse.GifSide
                    ? new ScreenImage(WlmMouse.GifSide, WlmMouse.GifSide, 4, bytes)
                    : null;
            }
            var bmp = BitmapFrame.Create(new MemoryStream(bytes), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            return new ScreenImage(WlmMouse.PictureSide, WlmMouse.PictureSide, 1, GifPlayer.ToRgb565(bmp, WlmMouse.PictureSide));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>A round tile: white disc, the animation clipped to an exact circle, and an outline ring on top.</summary>
    private sealed class ScreenTile : Grid
    {
        public readonly System.Windows.Shapes.Ellipse Ring;

        public ScreenTile(double size, ImageSource? still, Action<Image>? play)
        {
            Width = Height = size;
            Cursor = Cursors.Hand;
            HorizontalAlignment = HorizontalAlignment.Center;
            VerticalAlignment = VerticalAlignment.Center;
            // The screen is round and black means "off": the pictures are black squares, shown as one clean circle.
            var disc = new System.Windows.Shapes.Ellipse { Fill = play == null ? Brushes.White : Brushes.Black };
            var image = new Image { Stretch = Stretch.UniformToFill, Width = size, Height = size, Source = still };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            play?.Invoke(image);
            image.Clip = new EllipseGeometry(new Point(size / 2, size / 2), size / 2, size / 2);
            Ring = new System.Windows.Shapes.Ellipse { StrokeThickness = 1.2, Opacity = 0.7 };
            Ring.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "B.Outline");
            Children.Add(disc);
            Children.Add(image);
            Children.Add(Ring);
        }

        public void SetRing(string brushKey)
        {
            Ring.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, brushKey);
            Ring.Opacity = brushKey == "B.Outline" ? 0.7 : 1;
        }
    }

    /// <summary>A round, animated preview. Click to pick it; drag it onto a slot.</summary>
    private FrameworkElement Tile(ScreenImage img, byte[]? preview, double size)
    {
        var ring = new ScreenTile(size, null, image =>
        {
            if (preview != null) GifPlayer.Show(image, preview);
            else GifPlayer.Show(image, img.Data, img.Width, img.Height, img.Type);
        });
        ring.Margin = new Thickness(3);
        Point down = default;
        ring.PreviewMouseLeftButtonDown += (_, e) => down = e.GetPosition(ring);
        ring.MouseMove += (_, e) =>
        {
            if (e.LeftButton != MouseButtonState.Pressed) return;
            var p = e.GetPosition(ring);
            if (Math.Abs(p.X - down.X) + Math.Abs(p.Y - down.Y) < 6) return;
            DragDrop.DoDragDrop(ring, new DataObject(typeof(ScreenImage), img), DragDropEffects.Copy);
        };
        ring.MouseLeftButtonUp += (_, _) => PickTile(ring, img);
        return ring;
    }

    private void PickTile(FrameworkElement tile, ScreenImage img)
    {
        if (_pickedTile is ScreenTile old) { old.SetRing("B.Outline"); old.Ring.StrokeThickness = 1.2; }
        if (ReferenceEquals(_pickedTile, tile))
        {
            _picked = null;
            _pickedTile = null;
            return;
        }
        _picked = img;
        _pickedTile = tile;
        if (tile is ScreenTile t) { t.SetRing("B.Accent"); t.Ring.StrokeThickness = 3; }
    }

    private void ScreenAdd_Click(object sender, RoutedEventArgs e)
    {
        var model = _mouse?.Model;
        if (model == null) return;
        var dlg = new OpenFileDialog { Filter = "PNG / JPG / GIF|*.png;*.jpg;*.jpeg;*.gif", Multiselect = true };
        if (dlg.ShowDialog(this) != true) return;
        Directory.CreateDirectory(CustomDir(model));
        foreach (string file in dlg.FileNames)
        {
            byte[] bytes = File.ReadAllBytes(file);
            if (ToScreenImage(bytes) == null)
            {
                Status(Loc.T("ScreenBadGif"));
                continue;
            }
            File.Copy(file, Path.Combine(CustomDir(model), Guid.NewGuid().ToString("N") + Path.GetExtension(file)));
        }
        BuildScreenCustom(model);
        Anim.Stagger(PanelScreenCustom, 25, 8);
    }

    private void ScreenRemoveCustom_Click(object sender, RoutedEventArgs e)
    {
        if (_pickedTile?.Tag is not (ScreenImage, string file) || _mouse == null) return;
        try { File.Delete(file); } catch (IOException) { }
        _picked = null;
        _pickedTile = null;
        BuildScreenCustom(_mouse.Model);
    }

    // ===================================================================== slots

    private void RebuildScreenSlots()
    {
        PanelScreenMain.Children.Clear();
        PanelScreenMain.Children.Add(Slot(_screenMain, i => _screenMain = i, 44));
        PanelScreenRandom.Children.Clear();
        for (int k = 0; k < RandomSlots; k++)
        {
            int index = k;
            PanelScreenRandom.Children.Add(Slot(_screenRandom[k], i => _screenRandom[index] = i, 40));
        }
        long bytes = (_screenMain?.Data.Length ?? 0) + _screenRandom.Sum(r => (long)(r?.Data.Length ?? 0));
        TxtScreenSize.Text = Loc.F("ScreenUsed", bytes / 1024);
        TxtScreenSize.SetResourceReference(TextBlock.ForegroundProperty, bytes / 1024 >= WlmMouse.ScreenSizeLimitKb ? "B.Danger" : "B.TextMuted");
    }

    private FrameworkElement Slot(ScreenImage? content, Action<ScreenImage?> set, double size)
    {
        FrameworkElement view;
        if (content != null)
        {
            view = Tile(content, content.Type == 4 ? content.Data : null, size);
            if (view is ScreenTile t) { t.SetRing("B.Logo"); t.Ring.StrokeThickness = 2.5; }   // the hub's orange ring
        }
        else
        {
            var plus = new ScreenTile(size, null, null);
            plus.SetRing("B.Pill");
            plus.Children.Add(new TextBlock
            {
                Text = "+", FontSize = size * 0.45, FontWeight = FontWeights.Light, Foreground = Brushes.Black,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, -size * 0.08, 0, 0),
            });
            view = plus;
        }
        view.Margin = new Thickness(0, 0, 10, 0);
        view.AllowDrop = true;
        view.Drop += (_, e) =>
        {
            if (e.Data.GetData(typeof(ScreenImage)) is ScreenImage img) { set(img); RebuildScreenSlots(); }
        };
        view.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (_picked == null) return;
            e.Handled = true;
            set(_picked);
            RebuildScreenSlots();
        };
        view.MouseRightButtonUp += (_, _) => { set(null); RebuildScreenSlots(); };
        return view;
    }

    // ===================================================================== actions

    private async void ScreenFlag_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading || sender is not CheckBox { Tag: string tag } box) return;
        var flag = Enum.Parse<ScreenFlag>(tag);
        bool on = box.IsChecked == true;
        await Run(Loc.T("Saved"), m => m.SetScreenFlag(flag, on));
    }

    private async void ScreenUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (_screenMain == null)
        {
            Status(Loc.T("ScreenNeedMain"));
            return;
        }
        var images = new List<ScreenImage> { _screenMain };
        images.AddRange(_screenRandom.Where(r => r != null)!);
        if (images.Sum(i => (long)i.Data.Length) / 1024 >= WlmMouse.ScreenSizeLimitKb)
        {
            Status(Loc.T("ScreenTooBig"));
            return;
        }
        ShowBusy(Loc.F("ScreenWriting", 0));
        try
        {
            var progress = new Progress<int>(p => TxtOverlay.Text = Loc.F("ScreenWriting", p));
            await Run(Loc.T("Saved"), m => m.UploadScreenImages(images, progress));
        }
        finally
        {
            HideBusy();
        }
    }

    private async void ScreenDelete_Click(object sender, RoutedEventArgs e)
    {
        if (!ThemedDialog.Confirm(Loc.T("Delete"), Loc.T("ScreenDeleteConfirm"))) return;
        if (await Run(Loc.T("Saved"), m => m.DeleteScreenImages()))
        {
            _screenMain = null;
            Array.Clear(_screenRandom);
            RebuildScreenSlots();
        }
    }
}
