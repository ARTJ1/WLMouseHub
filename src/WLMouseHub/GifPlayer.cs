using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace WLMouseHub;

/// <summary>Plays animated GIFs in a WPF Image (frames composited with their offsets, delays and disposal).</summary>
public static class GifPlayer
{
    private static readonly ConditionalWeakTable<byte[], List<(BitmapSource Frame, TimeSpan Delay)>> Cache = new();

    /// <summary>Shows a picture: GIF bytes are animated, PNG/JPG bytes shown still, raw RGB565 decoded.</summary>
    public static void Show(Image target, byte[] data, int width = 0, int height = 0, int type = 0)
    {
        target.BeginAnimation(Image.SourceProperty, null);
        if (type == 1 && width > 0)
        {
            target.Source = Rgb565ToBitmap(data, width, height);
            return;
        }
        var frames = Decode(data);
        if (frames.Count == 0) { target.Source = null; return; }
        if (frames.Count == 1) { target.Source = frames[0].Frame; return; }

        var anim = new ObjectAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };
        var t = TimeSpan.Zero;
        foreach (var (frame, delay) in frames)
        {
            anim.KeyFrames.Add(new DiscreteObjectKeyFrame(frame, KeyTime.FromTimeSpan(t)));
            t += delay;
        }
        anim.Duration = t;
        target.BeginAnimation(Image.SourceProperty, anim);
    }

    public static List<(BitmapSource Frame, TimeSpan Delay)> Decode(byte[] data)
    {
        if (Cache.TryGetValue(data, out var cached)) return cached;
        var result = new List<(BitmapSource, TimeSpan)>();
        try
        {
            bool gif = data.Length > 3 && data[0] == 'G' && data[1] == 'I' && data[2] == 'F';
            if (!gif)
            {
                var single = BitmapFrame.Create(new MemoryStream(data), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                single.Freeze();
                result.Add((single, TimeSpan.FromSeconds(1)));
                Cache.Add(data, result);
                return result;
            }

            var decoder = new GifBitmapDecoder(new MemoryStream(data), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            int w = decoder.Frames[0].PixelWidth, h = decoder.Frames[0].PixelHeight;
            if (decoder.Metadata is BitmapMetadata meta)
            {
                if (meta.GetQuery("/logscrdesc/Width") is ushort lw) w = lw;
                if (meta.GetQuery("/logscrdesc/Height") is ushort lh) h = lh;
            }
            var canvas = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            BitmapSource? previous = null;

            foreach (var f in decoder.Frames)
            {
                var md = f.Metadata as BitmapMetadata;
                int left = Q(md, "/imgdesc/Left"), top = Q(md, "/imgdesc/Top");
                int delay = Q(md, "/grctlext/Delay") * 10;
                int disposal = Q(md, "/grctlext/Disposal");
                if (delay < 20) delay = 100;

                var snapshot = disposal == 3 ? Snapshot(canvas) : null;
                var dv = new DrawingVisual();
                using (var dc = dv.RenderOpen())
                {
                    if (previous != null) dc.DrawImage(previous, new Rect(0, 0, w, h));
                    dc.DrawImage(f, new Rect(left, top, f.PixelWidth, f.PixelHeight));
                }
                canvas.Clear();
                canvas.Render(dv);
                var shown = Snapshot(canvas);
                result.Add((shown, TimeSpan.FromMilliseconds(delay)));

                previous = disposal switch
                {
                    2 => ClearRect(shown, left, top, f.PixelWidth, f.PixelHeight, w, h),
                    3 => snapshot ?? previous,
                    _ => shown,
                };
            }
        }
        catch
        {
            // not decodable: no frames
        }
        Cache.Add(data, result);
        return result;
    }

    private static int Q(BitmapMetadata? md, string query)
    {
        try { return md?.GetQuery(query) is { } v ? Convert.ToInt32(v) : 0; }
        catch { return 0; }
    }

    private static BitmapSource Snapshot(RenderTargetBitmap rt)
    {
        var copy = new WriteableBitmap(rt);
        copy.Freeze();
        return copy;
    }

    private static BitmapSource ClearRect(BitmapSource src, int x, int y, int fw, int fh, int w, int h)
    {
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.PushClip(new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(new Rect(0, 0, w, h)), new RectangleGeometry(new Rect(x, y, fw, fh))));
            dc.DrawImage(src, new Rect(0, 0, w, h));
        }
        var rt = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rt.Render(dv);
        rt.Freeze();
        return rt;
    }

    // ---- RGB565 (the HUAN receiver's raw picture format, big-endian, as the hub produces it) ----

    public static byte[] ToRgb565(BitmapSource source, int side)
    {
        // Center-crop to a square, scale to side x side.
        int s = Math.Min(source.PixelWidth, source.PixelHeight);
        var cropped = new CroppedBitmap(source, new Int32Rect((source.PixelWidth - s) / 2, (source.PixelHeight - s) / 2, s, s));
        var scaled = new TransformedBitmap(cropped, new ScaleTransform(side / (double)s, side / (double)s));
        var bgra = new FormatConvertedBitmap(scaled, PixelFormats.Bgra32, null, 0);
        var px = new byte[side * side * 4];
        bgra.CopyPixels(px, side * 4, 0);
        var outBytes = new byte[side * side * 2];
        for (int i = 0, k = 0; i < px.Length; i += 4)
        {
            int b = px[i], g = px[i + 1], r = px[i + 2], a = px[i + 3];
            // transparent areas become black, like the hub's canvas
            r = r * a / 255; g = g * a / 255; b = b * a / 255;
            int v = ((r >> 3) << 11) | ((g >> 2) << 5) | (b >> 3);
            outBytes[k++] = (byte)(v >> 8);
            outBytes[k++] = (byte)v;
        }
        return outBytes;
    }

    public static BitmapSource Rgb565ToBitmap(byte[] data, int w, int h)
    {
        var px = new byte[w * h * 4];
        for (int i = 0, k = 0; i + 1 < data.Length && k < px.Length; i += 2, k += 4)
        {
            int v = (data[i] << 8) | data[i + 1];
            px[k + 2] = (byte)((v >> 11 & 31) * 255 / 31);
            px[k + 1] = (byte)((v >> 5 & 63) * 255 / 63);
            px[k] = (byte)((v & 31) * 255 / 31);
            px[k + 3] = 255;
        }
        var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, px, w * 4);
        bmp.Freeze();
        return bmp;
    }
}
