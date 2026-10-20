using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace WLMouseHub;

/// <summary>Small animation helpers that mirror the hub's transitions.</summary>
public static class Anim
{
    private static readonly IEasingFunction Ease = new CubicEase { EasingMode = EasingMode.EaseOut };

    /// <summary>Route change: fade in while sliding up a little (hub "fade" transition, 0.2-0.4 s).</summary>
    public static void FadeIn(UIElement e, double fromY = 14, int ms = 300)
    {
        if (e.RenderTransform is not TranslateTransform t)
        {
            t = new TranslateTransform();
            e.RenderTransform = t;
        }
        var d = TimeSpan.FromMilliseconds(ms);
        e.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, d) { EasingFunction = Ease });
        t.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(fromY, 0, d) { EasingFunction = Ease });
    }

    /// <summary>"list-complete" transition: items rise 30 px while fading in, staggered.</summary>
    public static void Stagger(Panel panel, int stepMs = 40, double fromY = 18)
    {
        int i = 0;
        foreach (UIElement child in panel.Children)
        {
            var t = new TranslateTransform();
            child.RenderTransform = t;
            child.Opacity = 0;
            var begin = TimeSpan.FromMilliseconds(i++ * stepMs);
            var d = TimeSpan.FromMilliseconds(350);
            child.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, d) { BeginTime = begin, EasingFunction = Ease });
            t.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(fromY, 0, d) { BeginTime = begin, EasingFunction = Ease });
        }
    }

    public static void To(FrameworkElement e, DependencyProperty p, double to, int ms = 400) =>
        e.BeginAnimation(p, new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = Ease });

    /// <summary>Swaps an image source with a quick cross-fade.</summary>
    public static void SwapImage(Image img, ImageSource? source)
    {
        if (img.Source == null || source == null || ReferenceEquals(img.Source, source))
        {
            img.Source = source;
            return;
        }
        var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(120));
        fadeOut.Completed += (_, _) =>
        {
            img.Source = source;
            img.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
        };
        img.BeginAnimation(UIElement.OpacityProperty, fadeOut);
    }

    /// <summary>Slides a capsule (the selected-tab background) under the checked button of a row (Naive UI segment tabs).</summary>
    public static void MoveCapsule(FrameworkElement capsule, FrameworkElement target, FrameworkElement container, bool animate = true)
    {
        if (target.ActualWidth <= 0) return;
        var p = target.TranslatePoint(new Point(0, 0), container);
        if (capsule.RenderTransform is not TranslateTransform t)
        {
            t = new TranslateTransform();
            capsule.RenderTransform = t;
        }
        int ms = animate && capsule.Width > 0 ? 300 : 0;
        var d = TimeSpan.FromMilliseconds(ms);
        t.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(p.X, d) { EasingFunction = Ease });
        t.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(p.Y, d) { EasingFunction = Ease });
        capsule.BeginAnimation(FrameworkElement.WidthProperty, new DoubleAnimation(target.ActualWidth, d) { EasingFunction = Ease });
        capsule.Height = target.ActualHeight;
    }

    /// <summary>Opacity pulse (hub "twinkle": 1 → 0.5 → 1 every 2 s).</summary>
    public static void Twinkle(UIElement e, bool on)
    {
        if (!on)
        {
            e.BeginAnimation(UIElement.OpacityProperty, null);
            return;
        }
        e.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, 0.35, TimeSpan.FromSeconds(1))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
        });
    }
}

/// <summary>The hub's loader: 8 dots on a circle, each shrinking and fading in turn (loaders.css ball-spin-clockwise, 1 s).</summary>
public sealed class Spinner : Canvas
{
    private static readonly (double X, double Y)[] Spots =
    {
        (50, 5), (81.8, 18.2), (95, 50), (81.8, 81.8), (50, 95), (18.2, 81.8), (5, 50), (18.2, 18.2),
    };

    public Spinner()
    {
        Width = Height = 40;
        for (int i = 0; i < Spots.Length; i++)
        {
            var dot = new Ellipse { Width = 8, Height = 8, RenderTransformOrigin = new Point(0.5, 0.5) };
            dot.SetResourceReference(Shape.FillProperty, "B.Text");
            var scale = new ScaleTransform();
            dot.RenderTransform = scale;
            SetLeft(dot, Spots[i].X / 100 * Width - 4);
            SetTop(dot, Spots[i].Y / 100 * Height - 4);
            Children.Add(dot);

            var begin = TimeSpan.FromSeconds(-0.875 + i * 0.125);
            var size = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever, BeginTime = begin, Duration = TimeSpan.FromSeconds(1) };
            size.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(0)));
            size.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(0.8), new SineEase()));
            size.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromPercent(1), new SineEase()));
            var fade = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever, BeginTime = begin, Duration = TimeSpan.FromSeconds(1) };
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(0.2)));
            fade.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(0.8), new SineEase()));
            fade.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromPercent(1), new SineEase()));
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, size);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, size);
            dot.BeginAnimation(OpacityProperty, fade);
        }
    }
}
