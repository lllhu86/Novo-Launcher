using System.Windows;
using System.Windows.Controls;
using MinecraftLauncher.Models;
using MinecraftLauncher.Services;

namespace MinecraftLauncher;

public partial class SaveDetailPage : Page
{
    private SaveRecord _saveRecord;
    private System.Timers.Timer? _autoRefreshTimer;
    public event Action? BackRequested;
    public event Action<SaveRecord>? PlaySaveRequested;
    
    public SaveDetailPage(SaveRecord record)
    {
        InitializeComponent();
        _saveRecord = record;
        DataContext = _saveRecord;
        LoadSaveDetails();
        
        StartAutoRefreshTimer();
    }
    
    private void LoadSaveDetails()
    {
        if (!string.IsNullOrEmpty(_saveRecord.SavePath))
        {
            _saveRecord = SaveTrackingService.Instance.RefreshSaveInfoByPath(_saveRecord.SavePath);
        }
        else
        {
            _saveRecord = SaveTrackingService.Instance.RefreshSaveInfo(_saveRecord.SaveName);
        }
        DataContext = _saveRecord;
        
        ModsListBox.Items.Clear();
        foreach (var mod in _saveRecord.EnabledMods)
        {
            ModsListBox.Items.Add(new ModItem { Name = mod, IsEnabled = true });
        }
        foreach (var mod in _saveRecord.DisabledMods)
        {
            ModsListBox.Items.Add(new ModItem { Name = mod, IsEnabled = false });
        }
        
        NoModsText.Visibility = ModsListBox.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
    
    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        BackRequested?.Invoke();
    }
    
    private void PlayButton_Click(object sender, RoutedEventArgs e)
    {
        PlaySaveRequested?.Invoke(_saveRecord);
    }
    
    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        LoadSaveDetails();
    }
    
    private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var savePath = _saveRecord.SavePath;
            if (string.IsNullOrEmpty(savePath))
            {
                savePath = System.IO.Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory, 
                    ".minecraft", 
                    "saves", 
                    _saveRecord.SaveName);
            }
            
            if (System.IO.Directory.Exists(savePath))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = savePath,
                    UseShellExecute = true,
                    Verb = "open"
                });
            }
            else
            {
                MessageBox.Show($"存档文件夹不存在: {savePath}", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            App.LogError("打开存档文件夹失败", ex);
            MessageBox.Show($"打开文件夹失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
    
    private void OpenModsFolderButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var gameDir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".minecraft");
            var modsDir = System.IO.Path.Combine(gameDir, "mods");
            
            if (!System.IO.Directory.Exists(modsDir))
            {
                System.IO.Directory.CreateDirectory(modsDir);
            }
            
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = modsDir,
                UseShellExecute = true,
                Verb = "open"
            });
        }
        catch (Exception ex)
        {
            App.LogError("打开模组文件夹失败", ex);
            MessageBox.Show($"打开文件夹失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
    
    private void CopySeedButton_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_saveRecord.Seed))
        {
            Clipboard.SetText(_saveRecord.Seed);
            MessageBox.Show("种子已复制到剪贴板", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
    
    private void StartAutoRefreshTimer()
    {
        _autoRefreshTimer = new System.Timers.Timer(10000);
        _autoRefreshTimer.Elapsed += async (s, e) => await Dispatcher.InvokeAsync(() => LoadSaveDetails());
        _autoRefreshTimer.AutoReset = true;
        _autoRefreshTimer.Start();
        
        App.LogInfo("存档详情自动刷新已启动（每10秒刷新一次）");
    }
    
    public void StopAutoRefresh()
    {
        _autoRefreshTimer?.Stop();
        _autoRefreshTimer?.Dispose();
        _autoRefreshTimer = null;
        
        App.LogInfo("存档详情自动刷新已停止");
    }
    
    private void ToggleModButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is ModItem modItem)
        {
            var modsDir = System.IO.Path.Combine(_saveRecord.SavePath, "mods");
            var disabledModsDir = System.IO.Path.Combine(_saveRecord.SavePath, "disabled_mods");
            
            if (!System.IO.Directory.Exists(disabledModsDir))
            {
                System.IO.Directory.CreateDirectory(disabledModsDir);
            }
            
            string sourcePath, destPath;
            
            if (modItem.IsEnabled)
            {
                sourcePath = System.IO.Path.Combine(modsDir, $"{modItem.Name}.jar");
                destPath = System.IO.Path.Combine(disabledModsDir, $"{modItem.Name}.jar");
            }
            else
            {
                sourcePath = System.IO.Path.Combine(disabledModsDir, $"{modItem.Name}.jar");
                destPath = System.IO.Path.Combine(modsDir, $"{modItem.Name}.jar");
            }
            
            try
            {
                if (System.IO.File.Exists(sourcePath))
                {
                    System.IO.File.Move(sourcePath, destPath);
                    modItem.IsEnabled = !modItem.IsEnabled;
                    LoadSaveDetails();
                    App.LogInfo($"模组 {(modItem.IsEnabled ? "启用" : "禁用")}: {modItem.Name}");
                }
            }
            catch (Exception ex)
            {
                App.LogError($"切换模组状态失败: {modItem.Name}", ex);
                MessageBox.Show($"操作失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
    
    private class ModItem
    {
        public string Name { get; set; } = "";
        public bool IsEnabled { get; set; }
    }
}
