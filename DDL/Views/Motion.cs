using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace DDL.Views;

/// Small, consistent animations (about 0.15–0.3 s, eased).
/// Everything is skipped when Windows' "Animation effects" setting is off, or with DDL_NO_ANIMATION=1.
public static class Motion
{
    private static readonly IEasingFunction EaseOut = Frozen(new CubicEase { EasingMode = EasingMode.EaseOut });
    private static readonly IEasingFunction EaseIn = Frozen(new CubicEase { EasingMode = EasingMode.EaseIn });
    private static readonly IEasingFunction EaseInOut = Frozen(new CubicEase { EasingMode = EasingMode.EaseInOut });

    public static bool Enabled =>
        SystemParameters.ClientAreaAnimation && Environment.GetEnvironmentVariable("DDL_NO_ANIMATION") != "1";

    /// Fades in while sliding from an offset of (dx, dy) to its place.
    public static void Enter(UIElement element, double dx = 0, double dy = 12, int delayMs = 0, int durationMs = 240)
    {
        if (!Enabled) return;
        var move = new TranslateTransform(dx, dy);
        element.RenderTransform = move;
        element.Opacity = 0;   // stays hidden during the delay

        var begin = TimeSpan.FromMilliseconds(delayMs);
        var duration = Ms(durationMs);
        var fade = new DoubleAnimation(0, 1, duration) { BeginTime = begin, EasingFunction = EaseOut };
        fade.Completed += (_, _) =>
        {
            element.BeginAnimation(UIElement.OpacityProperty, null);
            element.Opacity = 1;
        };
        element.BeginAnimation(UIElement.OpacityProperty, fade);
        if (dx != 0) move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(dx, 0, duration) { BeginTime = begin, EasingFunction = EaseOut });
        if (dy != 0) move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(dy, 0, duration) { BeginTime = begin, EasingFunction = EaseOut });
    }

    /// Grows from zero height to full height (what's below slides down), fading in as it opens.
    public static void Expand(FrameworkElement element, int durationMs = 280)
    {
        if (!Enabled) return;
        var scale = new ScaleTransform(1, 0);
        element.LayoutTransform = scale;
        element.Opacity = 0;

        var grow = new DoubleAnimation(0, 1, Ms(durationMs)) { EasingFunction = EaseOut };
        grow.Completed += (_, _) => element.LayoutTransform = Transform.Identity;
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);

        var fade = new DoubleAnimation(0, 1, Ms(durationMs * 0.7)) { BeginTime = TimeSpan.FromMilliseconds(durationMs * 0.3), EasingFunction = EaseOut };
        fade.Completed += (_, _) =>
        {
            element.BeginAnimation(UIElement.OpacityProperty, null);
            element.Opacity = 1;
        };
        element.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    /// Fades out and folds to zero height (what's below slides up). Completes when done.
    public static Task Collapse(FrameworkElement element, int durationMs = 240)
    {
        element.IsHitTestVisible = false;
        if (!Enabled) return Task.CompletedTask;

        var done = new TaskCompletionSource();
        var scale = new ScaleTransform(1, 1);
        element.LayoutTransform = scale;
        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, 0, Ms(durationMs * 0.6)) { EasingFunction = EaseIn });
        var shrink = new DoubleAnimation(1, 0, Ms(durationMs * 0.8)) { BeginTime = TimeSpan.FromMilliseconds(durationMs * 0.2), EasingFunction = EaseInOut };
        shrink.Completed += (_, _) => done.TrySetResult();
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, shrink);
        return done.Task;
    }

    /// Quick springy scale-up (check marks).
    public static void Pop(UIElement element, int durationMs = 260)
    {
        if (!Enabled) return;
        var scale = new ScaleTransform(0.3, 0.3);
        element.RenderTransformOrigin = new Point(0.5, 0.5);
        element.RenderTransform = scale;
        var spring = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.6 };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.3, 1, Ms(durationMs)) { EasingFunction = spring });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.3, 1, Ms(durationMs)) { EasingFunction = spring });
    }

    /// Turns an element from an angle back to 0 (an arrow swinging into its new direction).
    public static void Turn(UIElement element, double fromAngle, int durationMs = 180)
    {
        if (!Enabled) return;
        var turn = new RotateTransform(fromAngle);
        element.RenderTransformOrigin = new Point(0.5, 0.5);
        element.RenderTransform = turn;
        turn.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(fromAngle, 0, Ms(durationMs)) { EasingFunction = EaseOut });
    }

    /// A short glow: quickly visible, then fading away (for "here it is" highlights).
    public static void Glow(UIElement layer, int durationMs = 1100)
    {
        if (!Enabled) return;
        var glow = new DoubleAnimationUsingKeyFrames { Duration = Ms(durationMs) };
        glow.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(0)));
        glow.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromPercent(0.15), EaseOut));
        glow.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromPercent(0.45)));
        glow.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(1), EaseInOut));
        layer.BeginAnimation(UIElement.OpacityProperty, glow);
    }

    /// Hides a window's content now and eases it in (slight rise and scale) once the window is loaded.
    public static void PopInOnLoad(Window window)
    {
        if (!Enabled || window.Content is not FrameworkElement content) return;
        content.Opacity = 0;
        window.Loaded += (_, _) =>
        {
            content.RenderTransformOrigin = new Point(0.5, 0.5);
            var scale = new ScaleTransform(0.97, 0.97);
            var move = new TranslateTransform(0, 10);
            content.RenderTransform = new TransformGroup { Children = { scale, move } };

            var fade = new DoubleAnimation(0, 1, Ms(180)) { EasingFunction = EaseOut };
            fade.Completed += (_, _) =>
            {
                content.BeginAnimation(UIElement.OpacityProperty, null);
                content.Opacity = 1;
            };
            content.BeginAnimation(UIElement.OpacityProperty, fade);
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.97, 1, Ms(240)) { EasingFunction = EaseOut });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.97, 1, Ms(240)) { EasingFunction = EaseOut });
            move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(10, 0, Ms(240)) { EasingFunction = EaseOut });
        };
    }

    /// Runs a visual change (like a theme switch) and cross-fades from a snapshot of the old look.
    public static void CrossFade(Panel root, Action change, int durationMs = 320)
    {
        if (!Enabled || root.ActualWidth < 1 || root.ActualHeight < 1)
        {
            change();
            return;
        }

        var dpi = VisualTreeHelper.GetDpi(root);
        var bitmap = new RenderTargetBitmap(
            (int)Math.Ceiling(root.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(root.ActualHeight * dpi.DpiScaleY),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(root);
        bitmap.Freeze();

        var snapshot = new Image { Source = bitmap, Stretch = Stretch.Fill, IsHitTestVisible = false };
        Grid.SetColumnSpan(snapshot, 99);
        Grid.SetRowSpan(snapshot, 99);
        Panel.SetZIndex(snapshot, 10_000);
        root.Children.Add(snapshot);

        change();

        var fade = new DoubleAnimation(1, 0, Ms(durationMs)) { EasingFunction = EaseInOut };
        fade.Completed += (_, _) => root.Children.Remove(snapshot);
        snapshot.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    private static Duration Ms(double milliseconds) => new(TimeSpan.FromMilliseconds(milliseconds));

    private static IEasingFunction Frozen(EasingFunctionBase easing)
    {
        easing.Freeze();
        return easing;
    }
}
