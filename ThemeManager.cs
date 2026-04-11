using System.Windows;

namespace MinecraftLauncher;

public static class ThemeManager
{
    private const string DarkThemeKey = "DarkTheme";
    private const string LightThemeKey = "LightTheme";
    private static readonly string IsDarkThemeKey = "IsDarkTheme";

    public static bool IsDarkTheme { get; private set; } = true;

    public static void Initialize()
    {
        IsDarkTheme = true;
        Application.Current.Resources[IsDarkThemeKey] = true;
        ApplyTheme();
    }

    public static void ToggleTheme()
    {
        IsDarkTheme = !IsDarkTheme;
        Application.Current.Resources[IsDarkThemeKey] = IsDarkTheme;
        ApplyThemeWithAnimation();
        
        // 触发属性变化通知
        OnThemeChanged?.Invoke(null, IsDarkTheme);
    }
    
    public static event EventHandler<bool>? OnThemeChanged;

    private static void ApplyTheme()
    {
        var theme = IsDarkTheme ? GetDarkTheme() : GetLightTheme();
        
        foreach (var key in theme.Keys)
        {
            // 直接替换整个画刷对象，避免修改只读属性
            Application.Current.Resources[key] = theme[key];
        }
    }
    
    private static void ApplyThemeWithAnimation()
    {
        if (Application.Current.MainWindow is Window mainWindow)
        {
            // 淡出效果
            var fadeOut = new System.Windows.Media.Animation.DoubleAnimation
            {
                From = 1,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(200),
                EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseIn }
            };
            
            fadeOut.Completed += (s, e) =>
            {
                // 应用新主题
                ApplyTheme();
                
                // 淡入效果
                var fadeIn = new System.Windows.Media.Animation.DoubleAnimation
                {
                    From = 0,
                    To = 1,
                    Duration = TimeSpan.FromMilliseconds(300),
                    EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
                };
                
                fadeIn.Completed += (s2, e2) =>
                {
                    // 清除动画，避免透明度被锁定
                    mainWindow.BeginAnimation(Window.OpacityProperty, null);
                    mainWindow.Opacity = 1;
                };
                
                mainWindow.BeginAnimation(Window.OpacityProperty, fadeIn);
            };
            
            mainWindow.BeginAnimation(Window.OpacityProperty, fadeOut);
        }
        else
        {
            ApplyTheme();
        }
    }

    private static ResourceDictionary GetDarkTheme()
    {
        return new ResourceDictionary
        {
            ["PrimaryBackgroundBrush"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(30, 30, 30)),
            ["SecondaryBackgroundBrush"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(40, 40, 40)),
            ["TertiaryBackgroundBrush"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(50, 50, 50)),
            ["CardBackgroundBrush"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(30, 30, 30)),
            ["PrimaryForegroundBrush"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 255, 255)),
            ["SecondaryForegroundBrush"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(200, 200, 200)),
            ["AccentBrush"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 215, 0)),
            ["AccentForegroundBrush"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(30, 30, 30)),
            ["BorderBrush"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(60, 60, 60)),
            ["HoverBrush"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 223, 100))
        };
    }

    private static ResourceDictionary GetLightTheme()
    {
        return new ResourceDictionary
        {
            ["PrimaryBackgroundBrush"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 255, 255)),
            ["SecondaryBackgroundBrush"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(245, 245, 245)),
            ["TertiaryBackgroundBrush"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(240, 240, 240)),
            ["CardBackgroundBrush"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(235, 235, 235)), // 浅灰色卡片
            ["PrimaryForegroundBrush"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(30, 30, 30)),
            ["SecondaryForegroundBrush"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(50, 50, 50)), // 深灰色/接近黑色，提高对比度
            ["AccentBrush"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 193, 7)), // 深黄色/琥珀色
            ["AccentForegroundBrush"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(30, 30, 30)),
            ["BorderBrush"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 220, 220)),
            ["HoverBrush"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 211, 87)) // 浅黄色
        };
    }
}
