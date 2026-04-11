using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Controls;

namespace MinecraftLauncher;

public static class ThemeAnimator
{
    public static void AnimateColor(UIElement target, DependencyProperty property, Color fromColor, Color toColor, int durationMs = 300)
    {
        var animation = new ColorAnimation
        {
            From = fromColor,
            To = toColor,
            Duration = TimeSpan.FromMilliseconds(durationMs),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        };
        
        animation.FillBehavior = FillBehavior.HoldEnd;
        target.BeginAnimation(property, animation);
    }
    
    public static void FadeIn(UIElement element, int durationMs = 300)
    {
        var animation = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(durationMs),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        
        animation.Completed += (s, e) =>
        {
            element.BeginAnimation(UIElement.OpacityProperty, null);
            element.Opacity = 1;
        };
        
        element.BeginAnimation(UIElement.OpacityProperty, animation);
    }
    
    public static void FadeOut(UIElement element, int durationMs = 300)
    {
        var animation = new DoubleAnimation
        {
            From = 1,
            To = 0,
            Duration = TimeSpan.FromMilliseconds(durationMs),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        
        animation.Completed += (s, e) => element.Opacity = 0;
        element.BeginAnimation(UIElement.OpacityProperty, animation);
    }
    
    public static void ScaleIn(UIElement element, double fromScale = 0.9, int durationMs = 200)
    {
        element.RenderTransform = new ScaleTransform(fromScale, fromScale);
        element.RenderTransformOrigin = new Point(0.5, 0.5);
        
        var scaleX = new DoubleAnimation
        {
            From = fromScale,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(durationMs),
            EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut }
        };
        
        var scaleY = new DoubleAnimation
        {
            From = fromScale,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(durationMs),
            EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut }
        };
        
        ((ScaleTransform)element.RenderTransform).BeginAnimation(ScaleTransform.ScaleXProperty, scaleX);
        ((ScaleTransform)element.RenderTransform).BeginAnimation(ScaleTransform.ScaleYProperty, scaleY);
    }
    
    public static void SlideIn(UIElement element, double fromX = 0, double fromY = 30, int durationMs = 300)
    {
        var transformGroup = new TransformGroup();
        transformGroup.Children.Add(new TranslateTransform(fromX, fromY));
        element.RenderTransform = transformGroup;
        
        var translateX = new DoubleAnimation
        {
            From = fromX,
            To = 0,
            Duration = TimeSpan.FromMilliseconds(durationMs),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        
        var translateY = new DoubleAnimation
        {
            From = fromY,
            To = 0,
            Duration = TimeSpan.FromMilliseconds(durationMs),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        
        var opacity = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(durationMs)
        };
        
        ((TranslateTransform)transformGroup.Children[0]).BeginAnimation(TranslateTransform.XProperty, translateX);
        ((TranslateTransform)transformGroup.Children[0]).BeginAnimation(TranslateTransform.YProperty, translateY);
        element.BeginAnimation(UIElement.OpacityProperty, opacity);
    }
    
    public static void AnimateMargin(FrameworkElement element, Thickness toMargin, int durationMs = 200)
    {
        var animation = new ThicknessAnimation
        {
            To = toMargin,
            Duration = TimeSpan.FromMilliseconds(durationMs),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        
        element.BeginAnimation(FrameworkElement.MarginProperty, animation);
    }
    
    public static void Pulse(UIElement element, int durationMs = 1000)
    {
        var animation = new DoubleAnimation
        {
            From = 0.6,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(durationMs),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
        
        element.BeginAnimation(UIElement.OpacityProperty, animation);
    }
    
    public static void StopPulse(UIElement element)
    {
        element.BeginAnimation(UIElement.OpacityProperty, null);
        element.Opacity = 1;
    }
    
    public static void Rotate(UIElement element, double fromAngle = 0, double toAngle = 360, int durationMs = 1000)
    {
        element.RenderTransform = new RotateTransform(fromAngle);
        element.RenderTransformOrigin = new Point(0.5, 0.5);
        
        var animation = new DoubleAnimation
        {
            From = fromAngle,
            To = toAngle,
            Duration = TimeSpan.FromMilliseconds(durationMs),
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
        
        ((RotateTransform)element.RenderTransform).BeginAnimation(RotateTransform.AngleProperty, animation);
    }
    
    public static void StopRotate(UIElement element)
    {
        element.BeginAnimation(UIElement.RenderTransformProperty, null);
        if (element.RenderTransform is RotateTransform rotate)
        {
            rotate.Angle = 0;
        }
    }
    
    public static void BounceIn(UIElement element, int durationMs = 500)
    {
        element.RenderTransform = new ScaleTransform(0, 0);
        element.RenderTransformOrigin = new Point(0.5, 0.5);
        element.Opacity = 0;
        
        var scaleX = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(durationMs),
            EasingFunction = new BounceEase { EasingMode = EasingMode.EaseOut, Bounces = 3 }
        };
        
        var scaleY = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(durationMs),
            EasingFunction = new BounceEase { EasingMode = EasingMode.EaseOut, Bounces = 3 }
        };
        
        var opacity = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(durationMs)
        };
        
        ((ScaleTransform)element.RenderTransform).BeginAnimation(ScaleTransform.ScaleXProperty, scaleX);
        ((ScaleTransform)element.RenderTransform).BeginAnimation(ScaleTransform.ScaleYProperty, scaleY);
        element.BeginAnimation(UIElement.OpacityProperty, opacity);
    }
    
    public static void Shake(UIElement element, int durationMs = 100)
    {
        var transformGroup = element.RenderTransform as TransformGroup ?? new TransformGroup();
        var translate = transformGroup.Children.OfType<TranslateTransform>().FirstOrDefault();
        
        if (translate == null)
        {
            translate = new TranslateTransform();
            transformGroup.Children.Add(translate);
            element.RenderTransform = transformGroup;
        }
        
        var animation = new DoubleAnimationUsingKeyFrames
        {
            Duration = TimeSpan.FromMilliseconds(durationMs * 4)
        };
        
        animation.KeyFrames.Add(new LinearDoubleKeyFrame { KeyTime = TimeSpan.FromMilliseconds(0), Value = 0 });
        animation.KeyFrames.Add(new LinearDoubleKeyFrame { KeyTime = TimeSpan.FromMilliseconds(durationMs), Value = -10 });
        animation.KeyFrames.Add(new LinearDoubleKeyFrame { KeyTime = TimeSpan.FromMilliseconds(durationMs * 2), Value = 10 });
        animation.KeyFrames.Add(new LinearDoubleKeyFrame { KeyTime = TimeSpan.FromMilliseconds(durationMs * 3), Value = -10 });
        animation.KeyFrames.Add(new LinearDoubleKeyFrame { KeyTime = TimeSpan.FromMilliseconds(durationMs * 4), Value = 0 });
        
        translate.BeginAnimation(TranslateTransform.XProperty, animation);
    }
    
    public static void Flash(UIElement element, int durationMs = 200)
    {
        var animation = new DoubleAnimationUsingKeyFrames
        {
            Duration = TimeSpan.FromMilliseconds(800)
        };
        
        animation.KeyFrames.Add(new LinearDoubleKeyFrame { KeyTime = TimeSpan.FromMilliseconds(0), Value = 0 });
        animation.KeyFrames.Add(new LinearDoubleKeyFrame { KeyTime = TimeSpan.FromMilliseconds(durationMs), Value = 1 });
        animation.KeyFrames.Add(new LinearDoubleKeyFrame { KeyTime = TimeSpan.FromMilliseconds(500), Value = 1 });
        animation.KeyFrames.Add(new LinearDoubleKeyFrame { KeyTime = TimeSpan.FromMilliseconds(800), Value = 0 });
        
        element.BeginAnimation(UIElement.OpacityProperty, animation);
    }
    
    public static async Task FadeAndSlide(UIElement element, double fromX = 0, double fromY = 30, int durationMs = 300)
    {
        var transformGroup = new TransformGroup();
        var translate = new TranslateTransform(fromX, fromY);
        transformGroup.Children.Add(translate);
        element.RenderTransform = transformGroup;
        element.Opacity = 0;
        
        var tasks = new List<Task>();
        
        var translateX = new DoubleAnimation
        {
            From = fromX,
            To = 0,
            Duration = TimeSpan.FromMilliseconds(durationMs),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        
        var translateY = new DoubleAnimation
        {
            From = fromY,
            To = 0,
            Duration = TimeSpan.FromMilliseconds(durationMs),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        
        var opacity = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(durationMs)
        };
        
        var tcs = new TaskCompletionSource<bool>();
        opacity.Completed += (s, e) => tcs.SetResult(true);
        
        translate.BeginAnimation(TranslateTransform.XProperty, translateX);
        translate.BeginAnimation(TranslateTransform.YProperty, translateY);
        element.BeginAnimation(UIElement.OpacityProperty, opacity);
        
        await tcs.Task;
    }
    
    public static void AnimateWidth(FrameworkElement element, double toWidth, int durationMs = 300)
    {
        var animation = new DoubleAnimation
        {
            To = toWidth,
            Duration = TimeSpan.FromMilliseconds(durationMs),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        
        element.BeginAnimation(FrameworkElement.WidthProperty, animation);
    }
    
    public static void AnimateHeight(FrameworkElement element, double toHeight, int durationMs = 300)
    {
        var animation = new DoubleAnimation
        {
            To = toHeight,
            Duration = TimeSpan.FromMilliseconds(durationMs),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        
        element.BeginAnimation(FrameworkElement.HeightProperty, animation);
    }
}
