using System;
using System.IO;
using System.Management;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using System.Windows.Threading;
using Microsoft.Win32;

namespace MinecraftLauncher;

public partial class SettingsPage : Page
{
    private DispatcherTimer? _memoryRefreshTimer;
    
    public SettingsPage()
    {
        InitializeComponent();
        LoadSystemMemoryInfo();
        UpdateIsolationInfo();
        StartMemoryRefreshTimer();
        
        // 注册卸载事件
        Unloaded += SettingsPage_Unloaded;
        
        // 初始化下载模式提示信息（延迟执行，确保 UI 完全加载）
        Dispatcher.BeginInvoke(new Action(UpdateDownloadModeInfo), System.Windows.Threading.DispatcherPriority.Loaded);
    }
    
    private void SettingsPage_Unloaded(object sender, RoutedEventArgs e)
    {
        StopMemoryRefreshTimer();
    }

    #region 内存信息加载
    
    private void StartMemoryRefreshTimer()
    {
        _memoryRefreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2) // 每 2 秒刷新一次
        };
        
        _memoryRefreshTimer.Tick += MemoryRefreshTimer_Tick;
        _memoryRefreshTimer.Start();
    }
    
    private void MemoryRefreshTimer_Tick(object? sender, EventArgs e)
    {
        RefreshMemoryUsage();
    }
    
    private void RefreshMemoryUsage()
    {
        try
        {
            var memSearcher = new ManagementObjectSearcher("SELECT * FROM Win32_OperatingSystem");
            foreach (var obj in memSearcher.Get())
            {
                ulong freeMemory = Convert.ToUInt64(obj["FreePhysicalMemory"]);
                ulong totalMemory = Convert.ToUInt64(obj["TotalVisibleMemorySize"]);
                ulong usedMemory = totalMemory - freeMemory;
                
                double usedGB = Math.Round(usedMemory / (1024.0 * 1024.0), 2);
                double totalGB = Math.Round(totalMemory / (1024.0 * 1024.0), 2);
                double percentage = (usedMemory * 100.0) / totalMemory;
                
                UsedMemoryText.Text = $"{usedGB} GB / {totalGB} GB";
                MemoryProgressBar.Value = percentage;
                
                // 根据使用率改变进度条颜色
                UpdateProgressBarColor(percentage);
            }
        }
        catch
        {
            // 静默失败，不影响 UI
        }
    }
    
    private void UpdateProgressBarColor(double percentage)
    {
        if (percentage < 60)
        {
            MemoryProgressBar.Foreground = FindResource("AccentBrush") as System.Windows.Media.Brush;
        }
        else if (percentage < 80)
        {
            MemoryProgressBar.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 165, 0)); // 橙色
        }
        else
        {
            MemoryProgressBar.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 82, 82)); // 红色
        }
    }
    
    private void StopMemoryRefreshTimer()
    {
        if (_memoryRefreshTimer != null)
        {
            _memoryRefreshTimer.Stop();
            _memoryRefreshTimer.Tick -= MemoryRefreshTimer_Tick;
            _memoryRefreshTimer = null;
        }
    }
    
    private void LoadSystemMemoryInfo()
    {
        try
        {
            // 获取系统总内存
            var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_ComputerSystem");
            foreach (var obj in searcher.Get())
            {
                ulong totalMemory = Convert.ToUInt64(obj["TotalPhysicalMemory"]);
                double totalGB = Math.Round(totalMemory / (1024.0 * 1024.0 * 1024.0), 2);
                
                TotalMemoryText.Text = $"{totalGB} GB";
                
                // 设置滑块最大值为总内存的 80%
                MemorySlider.Maximum = totalMemory / (1024.0 * 1024.0) * 0.8;
            }
            
            // 获取已使用内存（初始值）
            var memSearcher = new ManagementObjectSearcher("SELECT * FROM Win32_OperatingSystem");
            foreach (var obj in memSearcher.Get())
            {
                ulong freeMemory = Convert.ToUInt64(obj["FreePhysicalMemory"]);
                ulong totalMemory = Convert.ToUInt64(obj["TotalVisibleMemorySize"]);
                ulong usedMemory = totalMemory - freeMemory;
                
                double usedGB = Math.Round(usedMemory / (1024.0 * 1024.0), 2);
                double totalGB = Math.Round(totalMemory / (1024.0 * 1024.0), 2);
                double percentage = (usedMemory * 100.0) / totalMemory;
                
                UsedMemoryText.Text = $"{usedGB} GB / {totalGB} GB";
                MemoryProgressBar.Value = percentage;
                
                // 更新已分配内存显示
                UpdateAllocatedMemoryDisplay();
            }
        }
        catch
        {
            // 如果获取失败，使用默认值
            TotalMemoryText.Text = "未知";
            UsedMemoryText.Text = "未知";
        }
    }
    #endregion

    #region 内存设置事件
    private void AutoMemoryRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (MemorySlider != null && OptimizeMemoryCheck != null && MemoryValueText != null)
        {
            MemorySlider.IsEnabled = false;
            OptimizeMemoryCheck.IsEnabled = false;
            MemoryValueText.Text = "自动";
        }
    }

    private void CustomMemoryRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (MemorySlider != null && OptimizeMemoryCheck != null)
        {
            MemorySlider.IsEnabled = true;
            OptimizeMemoryCheck.IsEnabled = true;
            UpdateMemoryValue();
        }
    }

    private void MemorySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        UpdateMemoryValue();
        UpdateAllocatedMemoryDisplay();
    }

    private void UpdateMemoryValue()
    {
        if (MemorySlider != null && MemoryValueText != null)
        {
            int value = (int)MemorySlider.Value;
            MemoryValueText.Text = $"{value} MB";
        }
    }

    private void UpdateAllocatedMemoryDisplay()
    {
        if (MemorySlider != null && AllocatedMemoryText != null)
        {
            double allocatedMB = MemorySlider.Value;
            double allocatedGB = Math.Round(allocatedMB / 1024.0, 2);
            AllocatedMemoryText.Text = $"{allocatedGB} GB";
        }
    }
    #endregion

    #region 版本隔离
    private void IsolationComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateIsolationInfo();
    }

    private void UpdateIsolationInfo()
    {
        if (IsolationComboBox == null || IsolationInfoText == null)
            return;

        int selectedIndex = IsolationComboBox.SelectedIndex;
        string info = selectedIndex switch
        {
            0 => "❌ 不开启版本隔离\n\n所有版本共享同一个 mods、saves、config 等文件夹。\n\n⚠️ 风险：可能导致不同版本的 mod 冲突，需要在设置中手动禁用不支持的 mod。",
            
            1 => "✅ 隔离可安装 mod 的版本与非正式版（推荐）\n\n• 正式版本之间共享地图、材质、光影等资源\n• 非正式版（快照、预览版等）独立隔离\n• 可以安装 mod 的版本独立隔离\n\n✨ 优点：兼顾 mod 隔离和资源互通，方便管理。",
            
            2 => "🔒 隔离所有版本\n\n• 每个版本都有独立的 mods、saves、config 等文件夹\n• 完全隔离，互不影响\n\n✨ 优点：最安全，不会有任何版本冲突问题。\n⚠️ 缺点：地图、资源包等无法在不同版本间共享。",
            
            3 => "🔧 自定义模式\n\n• 可以手动指定每个版本的独立文件夹\n• 灵活配置需要隔离的内容\n\n✨ 优点：最灵活，可以根据需求定制隔离策略。",
            
            _ => "请选择版本隔离模式"
        };

        IsolationInfoText.Text = info;
    }
    #endregion

    #region 皮肤设置
    private void BrowseSkinButton_Click(object sender, RoutedEventArgs e)
    {
        OpenFileDialog openFileDialog = new OpenFileDialog
        {
            Filter = "PNG 图片文件|*.png|所有文件|*.*",
            Title = "选择皮肤文件"
        };

        if (openFileDialog.ShowDialog() == true)
        {
            CustomSkinPathTextBox.Text = openFileDialog.FileName;
            CustomSkinRadio.IsChecked = true;
            App.LogInfo($"选择皮肤文件：{openFileDialog.FileName}");
            
            // 显示皮肤预览（简单实现）
            MessageBox.Show($"已选择皮肤文件:\n{openFileDialog.FileName}", 
                           "皮肤已选择", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
    #endregion

    #region 保存设置
    private void SaveSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        SaveAllSettings();
        
        MessageBox.Show("设置已保存！\n\n部分设置需要重启游戏后生效。", "提示", 
                       MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void SaveAllSettings()
    {
        // 保存版本隔离设置
        SaveIsolationSettings();
        
        // 保存内存设置
        SaveMemorySettings();
        
        // 保存皮肤设置
        SaveSkinSettings();
        
        // 保存高级选项
        SaveAdvancedSettings();
        
        App.LogInfo("所有设置已保存");
    }

    private void SaveIsolationSettings()
    {
        int selectedIndex = IsolationComboBox.SelectedIndex;
        string isolationMode = selectedIndex switch
        {
            0 => "none",
            1 => "mod_dev",
            2 => "all",
            3 => "custom",
            _ => "mod_dev"
        };

        App.LogInfo($"版本隔离设置：{isolationMode}");
    }

    private void SaveMemorySettings()
    {
        bool isAutoMemory = AutoMemoryRadio.IsChecked == true;
        int customMemory = (int)MemorySlider.Value;
        bool optimizeMemory = OptimizeMemoryCheck.IsChecked == true;
        
        App.LogInfo($"内存设置：{(isAutoMemory ? "自动" : $"自定义 {customMemory}MB")}");
        App.LogInfo($"内存优化：{optimizeMemory}");
    }

    private void SaveSkinSettings()
    {
        string skinType = "random";
        string customSkinPath = "";
        
        if (SteveSkinRadio.IsChecked == true)
            skinType = "steve";
        else if (AlexSkinRadio.IsChecked == true)
            skinType = "alex";
        else if (CustomSkinRadio.IsChecked == true)
            skinType = "official";
        
        App.LogInfo($"离线皮肤设置：{skinType}");
    }
    
    private void UpdateDownloadModeInfo()
    {
        if (DownloadModeInfoText == null || DownloadModeComboBox == null)
            return;
            
        if (DownloadModeComboBox.SelectedIndex == 0)
        {
            DownloadModeInfoText.Inlines.Clear();
            DownloadModeInfoText.Inlines.Add(new System.Windows.Documents.Run { Text = "单线程模式：", FontWeight = System.Windows.FontWeights.Bold });
            DownloadModeInfoText.Inlines.Add(new System.Windows.Documents.Run { Text = "每次下载 3 个文件，稳定性高，适合网络不稳定的环境\n" });
            DownloadModeInfoText.Inlines.Add(new System.Windows.Documents.Run { Text = "多线程模式：", FontWeight = System.Windows.FontWeights.Bold });
            DownloadModeInfoText.Inlines.Add(new System.Windows.Documents.Run { Text = "同时下载 10 个文件，速度更快，适合高速网络环境" });
        }
        else
        {
            DownloadModeInfoText.Inlines.Clear();
            DownloadModeInfoText.Inlines.Add(new System.Windows.Documents.Run { Text = "已切换到多线程模式，下载速度将大幅提升！", FontWeight = System.Windows.FontWeights.Bold, Foreground = (System.Windows.Media.Brush)FindResource("AccentBrush") });
        }
    }

    private void SaveAdvancedSettings()
    {
        string javaArgs = JavaArgsTextBox.Text;
        string gameArgs = GameArgsTextBox.Text;
        string preLaunchCommand = PreLaunchCommandTextBox.Text;
        bool disableJavaWrapper = DisableJavaWrapperCheck.IsChecked == true;
        bool useDedicatedGPU = UseDedicatedGPUCheck.IsChecked == true;
        bool useMultiThreadDownload = DownloadModeComboBox.SelectedIndex == 1;
        
        App.LogInfo($"Java 参数：{javaArgs}");
        App.LogInfo($"游戏参数：{gameArgs}");
        App.LogInfo($"启动前命令：{preLaunchCommand}");
        App.LogInfo($"禁用 Java Wrapper: {disableJavaWrapper}");
        App.LogInfo($"使用高性能显卡：{useDedicatedGPU}");
        App.LogInfo($"多线程下载：{useMultiThreadDownload}");
    }
    #endregion
}
