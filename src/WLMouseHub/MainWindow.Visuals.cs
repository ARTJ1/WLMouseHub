using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using WLMouseHub.Protocol;

namespace WLMouseHub;

/// <summary>Mouse picture and colour carousel, sliding tab capsules, page transitions and the loading overlay.</summary>
public partial class MainWindow
{
    private const int ThumbSize = 36, ThumbGap = 6, VisibleThumbs = 4; // the hub shows 4 per page
    private int _eid = 255;
    private readonly List<int> _colorIds = new();
    private readonly List<Border> _colorRings = new();
    private int _colorOffset;

    // ===================================================================== mouse picture

    /// <summary>
    /// Same rule as the hub: a colour picked in the carousel wins, then the colour the mouse reports (EID),
    /// then the default picture. Home and Setting show the same top view (382x738, drawn 410 px tall).
    /// </summary>
    private async Task UpdateMousePictureAsync()
    {
        var model = _mouse?.Model;
        if (model == null) return;
        string f = "Config/" + model.UIFolder + "/";
        var candidates = new List<string>();
        if (App.Settings.MouseColors.TryGetValue(model.UIFolder, out int picked)) candidates.Add($"{f}Colors/{picked}.png");
        if (_eid != 255) candidates.Add(_eid > 255 ? $"{f}Device_{_eid:X4}.png" : $"{f}Device_{_eid}.png");
        candidates.Add(f + "Device_255.png");
        candidates.Add(f + "Device.png");

        var img = await AssetCache.FirstAsync(candidates.ToArray());
        Anim.SwapImage(ImgHomeMouse, img);
        Anim.SwapImage(ImgKeyMouse, img);
        _hasKeyImage = img != null;
        RebuildMarkers();
        HighlightColorThumb();
    }

    private async Task BuildColorCarouselAsync(DeviceModel model)
    {
        _colorIds.Clear();
        _colorRings.Clear();
        PanelColors.Children.Clear();
        _colorOffset = 0;

        string? json = await AssetCache.GetTextAsync($"Config/{model.UIFolder}/Colors/colorList.json");
        var ids = new List<int>();
        if (json != null)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                foreach (var e in doc.RootElement.EnumerateArray())
                    if (e.TryGetProperty("ID", out var id) && id.TryGetInt32(out int v) && !ids.Contains(v)) ids.Add(v);
            }
            catch (JsonException) { }
        }

        foreach (int id in ids)
        {
            var thumb = await AssetCache.GetAsync($"Config/{model.UIFolder}/Colors/{id}.png");
            if (thumb == null) continue;
            _colorIds.Add(id);
            int colorId = id;
            var ring = new Border
            {
                Width = ThumbSize, Height = ThumbSize, CornerRadius = new CornerRadius(ThumbSize / 2.0),
                Margin = new Thickness(0, 0, ThumbGap, 0), BorderThickness = new Thickness(1.5), Cursor = Cursors.Hand, Tag = id,
                Child = new Image { Source = thumb, Margin = new Thickness(2), Stretch = Stretch.Uniform },
                ToolTip = $"#{id}",
            };
            ring.SetResourceReference(Border.BackgroundProperty, "B.Thumb2");
            ring.MouseLeftButtonUp += async (_, _) =>
            {
                App.Settings.MouseColors[model.UIFolder] = colorId;
                App.Settings.Save();
                await UpdateMousePictureAsync();
            };
            _colorRings.Add(ring);
        }
        ColorCarousel.Visibility = _colorIds.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        HighlightColorThumb();
        // open on the page that holds the chosen colour, like the hub
        int chosen = App.Settings.MouseColors.TryGetValue(model.UIFolder, out int c) ? _colorIds.IndexOf(c) : -1;
        ShowColorPage(Math.Max(0, chosen) / VisibleThumbs, animate: false);
    }

    private void HighlightColorThumb()
    {
        var model = _mouse?.Model;
        int? picked = model != null && App.Settings.MouseColors.TryGetValue(model.UIFolder, out int p) ? p : null;
        foreach (Border ring in _colorRings)
        {
            if (Equals(ring.Tag, picked)) ring.SetResourceReference(Border.BorderBrushProperty, "B.Outline");
            else ring.BorderBrush = Brushes.Transparent;
        }
    }

    private void ColorPrev_Click(object sender, RoutedEventArgs e) => ScrollColors(-1);
    private void ColorNext_Click(object sender, RoutedEventArgs e) => ScrollColors(+1);

    /// <summary>Hub behaviour: the arrows turn whole pages of 4 and stop at the first/last page (never empty).</summary>
    private void ScrollColors(int direction)
    {
        int lastPage = Math.Max(0, (_colorIds.Count - 1) / VisibleThumbs);
        int page = Math.Clamp(_colorOffset / VisibleThumbs + direction, 0, lastPage);
        ShowColorPage(page, animate: true);
    }

    /// <summary>Shows one page (the hub's colorList.slice(start, start + 4)); paging slides the new page in.</summary>
    private void ShowColorPage(int page, bool animate)
    {
        int direction = Math.Sign(page * VisibleThumbs - _colorOffset);
        _colorOffset = page * VisibleThumbs;
        PanelColors.Children.Clear();
        foreach (var ring in _colorRings.Skip(_colorOffset).Take(VisibleThumbs)) PanelColors.Children.Add(ring);
        if (animate)
        {
            PanelColors.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250)));
            ColorsShift.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(24 * direction, 0, TimeSpan.FromMilliseconds(300)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }
        int pages = Math.Max(1, (_colorRings.Count + VisibleThumbs - 1) / VisibleThumbs);
        BtnColorPrev.Opacity = page > 0 ? 1 : 0.3;
        BtnColorNext.Opacity = page + 1 < pages ? 1 : 0.3;
        TxtColorPage.Text = pages > 1 ? $"{page + 1} / {pages}" : "";
        HighlightColorThumb();
    }

    // ===================================================================== capsules and transitions

    private void PlaceCapsules(bool animate)
    {
        var header = HeaderTabs.Children.OfType<RadioButton>().FirstOrDefault(r => r.IsChecked == true);
        if (header != null) Anim.MoveCapsule(HeaderCapsule, header, HeaderTabsHost, animate);
        var profile = PanelProfiles.Children.OfType<RadioButton>().FirstOrDefault(r => r.IsChecked == true);
        if (profile != null) Anim.MoveCapsule(ProfileCapsule, profile, ProfileHost, animate);
    }

    private void ShowPage(FrameworkElement page)
    {
        foreach (var p in new FrameworkElement[] { PageHome, PageSettings, PageFirmware })
            p.Visibility = p == page ? Visibility.Visible : Visibility.Collapsed;
        Anim.FadeIn(page, 16, 350);
        Dispatcher.BeginInvoke(() => PlaceCapsules(true), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    // ===================================================================== loading overlay

    private int _busy;

    private void ShowBusy(string text)
    {
        if (_busy++ > 0) { TxtOverlay.Text = text; return; }
        SpinnerHost.Content ??= new Spinner();
        TxtOverlay.Text = text;
        Overlay.Visibility = Visibility.Visible;
        Overlay.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)));
    }

    private void HideBusy()
    {
        if (_busy == 0 || --_busy > 0) return;
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));
        fade.Completed += (_, _) => { if (_busy == 0) Overlay.Visibility = Visibility.Collapsed; };
        Overlay.BeginAnimation(OpacityProperty, fade);
    }
}
