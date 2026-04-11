using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace MinecraftLauncher.Helpers;

public static class AnimationBehaviors
{
    #region 导航按钮动画
    
    public static readonly DependencyProperty EnableNavigationAnimationProperty =
        DependencyProperty.RegisterAttached(
            "EnableNavigationAnimation",
            typeof(bool),
            typeof(AnimationBehaviors),
            new PropertyMetadata(false, OnEnableNavigationAnimationChanged));
    
    public static void SetEnableNavigationAnimation(UIElement element, bool value) =>
        element.SetValue(EnableNavigationAnimationProperty, value);
    
    public static bool GetEnableNavigationAnimation(UIElement element) =>
        (bool)element.GetValue(EnableNavigationAnimationProperty);
    
    private static void OnEnableNavigationAnimationChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TextBlock textBlock && (bool)e.NewValue)
        {
            textBlock.MouseEnter += NavigationButton_MouseEnter;
            textBlock.MouseLeave += NavigationButton_MouseLeave;
            textBlock.MouseLeftButtonDown += NavigationButton_MouseLeftButtonDown;
        }
    }
    
    private static void NavigationButton_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is TextBlock textBlock)
        {
            var transformGroup = new TransformGroup();
            transformGroup.Children.Add(new ScaleTransform(1, 1));
            textBlock.RenderTransform = transformGroup;
            textBlock.RenderTransformOrigin = new Point(0.5, 0.5);
            
            var scaleX = new DoubleAnimation
            {
                To = 1.05,
                Duration = TimeSpan.FromMilliseconds(150),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            
            var scaleY = new DoubleAnimation
            {
                To = 1.05,
                Duration = TimeSpan.FromMilliseconds(150),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            
            ((ScaleTransform)transformGroup.Children[0]).BeginAnimation(ScaleTransform.ScaleXProperty, scaleX);
            ((ScaleTransform)transformGroup.Children[0]).BeginAnimation(ScaleTransform.ScaleYProperty, scaleY);
        }
    }
    
    private static void NavigationButton_MouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is TextBlock textBlock)
        {
            var transformGroup = textBlock.RenderTransform as TransformGroup;
            if (transformGroup?.Children[0] is ScaleTransform scaleTransform)
            {
                var scaleX = new DoubleAnimation
                {
                    To = 1,
                    Duration = TimeSpan.FromMilliseconds(150),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                
                var scaleY = new DoubleAnimation
                {
                    To = 1,
                    Duration = TimeSpan.FromMilliseconds(150),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                
                scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleX);
                scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleY);
            }
        }
    }
    
    private static void NavigationButton_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is TextBlock textBlock)
        {
            var transformGroup = textBlock.RenderTransform as TransformGroup;
            if (transformGroup?.Children[0] is ScaleTransform scaleTransform)
            {
                var scaleX = new DoubleAnimation
                {
                    To = 0.95,
                    Duration = TimeSpan.FromMilliseconds(100),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                
                var scaleY = new DoubleAnimation
                {
                    To = 0.95,
                    Duration = TimeSpan.FromMilliseconds(100),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                
                scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleX);
                scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleY);
            }
        }
    }
    
    #endregion
    
    #region 按钮动画
    
    public static readonly DependencyProperty EnableButtonAnimationProperty =
        DependencyProperty.RegisterAttached(
            "EnableButtonAnimation",
            typeof(bool),
            typeof(AnimationBehaviors),
            new PropertyMetadata(false, OnEnableButtonAnimationChanged));
    
    public static void SetEnableButtonAnimation(UIElement element, bool value) =>
        element.SetValue(EnableButtonAnimationProperty, value);
    
    public static bool GetEnableButtonAnimation(UIElement element) =>
        (bool)element.GetValue(EnableButtonAnimationProperty);
    
    private static void OnEnableButtonAnimationChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Button button && (bool)e.NewValue)
        {
            button.MouseEnter += Button_MouseEnter;
            button.MouseLeave += Button_MouseLeave;
            button.PreviewMouseLeftButtonDown += Button_PreviewMouseLeftButtonDown;
            button.PreviewMouseLeftButtonUp += Button_PreviewMouseLeftButtonUp;
        }
    }
    
    private static void Button_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is Button button)
        {
            var transformGroup = new TransformGroup();
            transformGroup.Children.Add(new ScaleTransform(1, 1));
            button.RenderTransform = transformGroup;
            button.RenderTransformOrigin = new Point(0.5, 0.5);
            
            var scaleX = new DoubleAnimation
            {
                To = 1.08,
                Duration = TimeSpan.FromMilliseconds(200),
                EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut }
            };
            
            var scaleY = new DoubleAnimation
            {
                To = 1.08,
                Duration = TimeSpan.FromMilliseconds(200),
                EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut }
            };
            
            ((ScaleTransform)transformGroup.Children[0]).BeginAnimation(ScaleTransform.ScaleXProperty, scaleX);
            ((ScaleTransform)transformGroup.Children[0]).BeginAnimation(ScaleTransform.ScaleYProperty, scaleY);
        }
    }
    
    private static void Button_MouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is Button button)
        {
            var transformGroup = button.RenderTransform as TransformGroup;
            if (transformGroup?.Children[0] is ScaleTransform scaleTransform)
            {
                var scaleX = new DoubleAnimation
                {
                    To = 1,
                    Duration = TimeSpan.FromMilliseconds(200),
                    EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut }
                };
                
                var scaleY = new DoubleAnimation
                {
                    To = 1,
                    Duration = TimeSpan.FromMilliseconds(200),
                    EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut }
                };
                
                scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleX);
                scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleY);
            }
        }
    }
    
    private static void Button_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is Button button)
        {
            var transformGroup = button.RenderTransform as TransformGroup;
            if (transformGroup?.Children[0] is ScaleTransform scaleTransform)
            {
                var scaleX = new DoubleAnimation
                {
                    To = 0.95,
                    Duration = TimeSpan.FromMilliseconds(100),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                
                var scaleY = new DoubleAnimation
                {
                    To = 0.95,
                    Duration = TimeSpan.FromMilliseconds(100),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                
                scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleX);
                scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleY);
            }
        }
    }
    
    private static void Button_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is Button button)
        {
            var transformGroup = button.RenderTransform as TransformGroup;
            if (transformGroup?.Children[0] is ScaleTransform scaleTransform)
            {
                var scaleX = new DoubleAnimation
                {
                    To = 1.08,
                    Duration = TimeSpan.FromMilliseconds(100),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                
                var scaleY = new DoubleAnimation
                {
                    To = 1.08,
                    Duration = TimeSpan.FromMilliseconds(100),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                
                scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleX);
                scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleY);
            }
        }
    }
    
    #endregion
    
    #region 列表项动画
    
    public static readonly DependencyProperty EnableListItemAnimationProperty =
        DependencyProperty.RegisterAttached(
            "EnableListItemAnimation",
            typeof(bool),
            typeof(AnimationBehaviors),
            new PropertyMetadata(false, OnEnableListItemAnimationChanged));
    
    public static void SetEnableListItemAnimation(UIElement element, bool value) =>
        element.SetValue(EnableListItemAnimationProperty, value);
    
    public static bool GetEnableListItemAnimation(UIElement element) =>
        (bool)element.GetValue(EnableListItemAnimationProperty);
    
    private static void OnEnableListItemAnimationChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ListBoxItem item && (bool)e.NewValue)
        {
            item.Loaded += ListBoxItem_Loaded;
        }
    }
    
    private static void ListBoxItem_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is ListBoxItem item)
        {
            var transformGroup = new TransformGroup();
            transformGroup.Children.Add(new TranslateTransform(-20, 0));
            item.RenderTransform = transformGroup;
            item.Opacity = 0;
            
            var translateX = new DoubleAnimation
            {
                From = -20,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(300),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            
            var opacity = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = TimeSpan.FromMilliseconds(300)
            };
            
            ((TranslateTransform)transformGroup.Children[0]).BeginAnimation(TranslateTransform.XProperty, translateX);
            item.BeginAnimation(UIElement.OpacityProperty, opacity);
            
            item.Loaded -= ListBoxItem_Loaded;
        }
    }
    
    #endregion
}
