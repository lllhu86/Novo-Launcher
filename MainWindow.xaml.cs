﻿﻿﻿﻿﻿using MinecraftLauncher.Models;
using MinecraftLauncher.Services;
using System.Windows;
using System.Windows.Controls;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using Microsoft.Win32;
using Newtonsoft.Json;

namespace MinecraftLauncher;

public partial class MainWindow : Window
{
    private readonly MinecraftService _minecraftService;
    private readonly AccountService _accountService;
    private readonly SaveTrackingService _saveTrackingService;
    private readonly string _gameDir;
    private VersionManifest? _versionManifest;
    private VersionDetail? _selectedVersionDetail;
    private bool _isDownloading;
    private Process? _currentGameProcess;
    private SaveDetailPage? _currentSaveDetailPage;
    private CancellationTokenSource? _downloadCts;

    public MainWindow()
    {
        InitializeComponent();
        _minecraftService = new MinecraftService();
        _accountService = AccountService.Instance;
        _saveTrackingService = SaveTrackingService.Instance;
        _minecraftService.DownloadProgressChanged += OnDownloadProgressChanged;
        _gameDir = AppDomain.CurrentDomain.BaseDirectory;
        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        App.LogInfo("主窗口关闭，清理资源...");

        _saveTrackingService.SaveRecordsChanged -= OnSaveRecordsChanged;
        _minecraftService.DownloadProgressChanged -= OnDownloadProgressChanged;

        _minecraftService.Dispose();

        if (_currentGameProcess != null && !_currentGameProcess.HasExited)
        {
            try
            {
                App.LogInfo("检测到游戏仍在运行，终止游戏进程");
                _currentGameProcess.Kill();
                _currentGameProcess.WaitForExit(5000);
            }
            catch (Exception ex)
            {
                App.LogError("终止游戏进程失败", ex);
            }
        }

        App.LogInfo("资源清理完成，程序退出");
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        App.LogInfo("MainWindow 加载成功");
        StatusText.Text = "准备就绪，请点击「加载版本列表」按钮";
        UpdateAccountDisplay();

        _saveTrackingService.Initialize();
        _saveTrackingService.SaveRecordsChanged += OnSaveRecordsChanged;
        UpdateSaveRecordsList();
    }

    private void OnSaveRecordsChanged()
    {
        Dispatcher.Invoke(() => UpdateSaveRecordsList());
    }

    private void UpdateSaveRecordsList()
    {
        var records = _saveTrackingService.GetSaveRecords();
        SaveRecordListBox.Items.Clear();

        foreach (var record in records)
        {
            SaveRecordListBox.Items.Add(record);
        }

        SaveRecordCountText.Text = $"{records.Count} 个存档";
        NoSaveRecordText.Visibility = records.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        SaveRecordListBox.Visibility = records.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void LoadVersionsButton_Click(object sender, RoutedEventArgs e)
    {
        await LoadVersionsAsync();
    }

    private async Task LoadVersionsAsync()
    {
        StatusText.Text = "正在获取版本列表...";
        LoadVersionsButton.IsEnabled = false;
        try
        {
            _versionManifest = await _minecraftService.GetVersionManifestAsync();
            if (_versionManifest?.Versions != null)
            {
                VersionListBox.Items.Clear();
                ReleaseListBox.Items.Clear();
                SnapshotListBox.Items.Clear();
                OldVersionListBox.Items.Clear();
                AprilFoolsListBox.Items.Clear();

                var categorized = VersionCategorizer.Categorize(_versionManifest.Versions);

                foreach (var version in categorized.Releases)
                {
                    ReleaseListBox.Items.Add(version);
                }

                foreach (var version in categorized.Snapshots)
                {
                    SnapshotListBox.Items.Add(version);
                }

                foreach (var version in categorized.OldVersions)
                {
                    OldVersionListBox.Items.Add(version);
                }

                foreach (var version in categorized.AprilFools)
                {
                    AprilFoolsListBox.Items.Add(version);
                }

                VersionListBox.Visibility = Visibility.Collapsed;
                CategorizedVersionPanel.Visibility = Visibility.Visible;

                StatusText.Text = $"已加载 {_versionManifest.Versions.Count} 个版本（分类显示）";
                ThemeAnimator.Flash(StatusText, 200);
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"加载失败：{ex.Message}";
            App.LogError("加载版本列表失败", ex);
            MessageBox.Show($"无法获取版本列表：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            ThemeAnimator.Shake(StatusText, 100);
        }
        finally
        {
            LoadVersionsButton.IsEnabled = true;
        }
    }

    private async void VersionListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        VersionInfo? versionInfo = null;

        if (sender == VersionListBox && VersionListBox.SelectedItem is VersionInfo v1)
        {
            versionInfo = v1;
        }
        else if (sender == ReleaseListBox && ReleaseListBox.SelectedItem is VersionInfo v2)
        {
            versionInfo = v2;
        }
        else if (sender == SnapshotListBox && SnapshotListBox.SelectedItem is VersionInfo v3)
        {
            versionInfo = v3;
        }
        else if (sender == OldVersionListBox && OldVersionListBox.SelectedItem is VersionInfo v4)
        {
            versionInfo = v4;
        }
        else if (sender == AprilFoolsListBox && AprilFoolsListBox.SelectedItem is VersionInfo v5)
        {
            versionInfo = v5;
        }

        if (versionInfo != null)
        {
            StatusText.Text = $"正在获取 {versionInfo.Id} 的详细信息...";
            try
            {
                _selectedVersionDetail = await _minecraftService.GetVersionDetailAsync(versionInfo);
                if (_selectedVersionDetail != null)
                {
                    var javaVersion = _selectedVersionDetail.JavaVersion?.MajorVersion ?? 8;
                    VersionInfoText.Text = $"版本：{_selectedVersionDetail.Id}\n" +
                                          $"类型：{_selectedVersionDetail.Type}\n" +
                                          $"主类：{_selectedVersionDetail.MainClass}\n" +
                                          $"Java 版本：{javaVersion}\n" +
                                          $"资源索引：{_selectedVersionDetail.Assets}";

                    PlayButton.IsEnabled = true;
                    StatusText.Text = $"已选择版本：{versionInfo.Id}";
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = $"获取版本详情失败：{ex.Message}";
                App.LogError("获取版本详情失败", ex);
            }
        }
    }

    private async void PlayButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedVersionDetail == null)
            return;

        if (_isDownloading)
        {
            App.LogInfo("检测到下载正在进行中，取消之前的下载...");
            _downloadCts?.Cancel();

            await Task.Delay(1000);

            _downloadCts?.Dispose();
            _downloadCts = null;
            _isDownloading = false;
        }

        var account = _accountService.GetSelectedAccount();
        if (account == null)
        {
            MessageBox.Show("请先登录或选择一个账号", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _isDownloading = true;
        PlayButton.IsEnabled = false;
        LoadVersionsButton.IsEnabled = false;

        try
        {
            ShowProgress("正在检查游戏文件...", 0);

            _downloadCts = new CancellationTokenSource();

            var settingsPage = App.Current.Windows.OfType<SettingsPage>().FirstOrDefault();
            bool useMultiThreadDownload = settingsPage?.DownloadModeComboBox?.SelectedIndex == 1;

            _minecraftService.UseMirror = true;
            _minecraftService.SetDownloadMode(useMultiThreadDownload);

            await _minecraftService.PrepareVersionAsync(_selectedVersionDetail, _gameDir, _downloadCts.Token);

            UpdateProgressStage("正在启动游戏...", 90);

            var launchService = new LaunchService(_gameDir);
            var javaPath = JavaPathTextBox.Text.Trim();
            if (string.IsNullOrEmpty(javaPath))
                javaPath = null;

            var process = launchService.LaunchGame(_selectedVersionDetail, account, javaPath);

            if (process != null)
            {
                _currentGameProcess = process;
                _saveTrackingService.StartGameSession(process, _selectedVersionDetail.Id ?? "unknown");

                UpdateProgressStage("游戏启动成功", 100);
                StatusText.Text = $"游戏已启动 (PID: {process.Id})";

                await Task.Delay(1500);
                HideProgress();

                var aiPage = AIAssistantFrame.Content as AIAssistantPage;
                if (aiPage != null)
                {
                    aiPage.StartGameMonitoring(process, _selectedVersionDetail.Id ?? "unknown");
                }

                _ = Task.Run(async () =>
                {
                    await process.WaitForExitAsync();
                    Dispatcher.Invoke(() =>
                    {
                        _saveTrackingService.EndGameSession();
                        _currentGameProcess = null;
                        PlayButton.IsEnabled = true;
                        LoadVersionsButton.IsEnabled = true;
                        
                        var aiPage = AIAssistantFrame.Content as AIAssistantPage;
                        if (aiPage != null)
                        {
                            aiPage.StopGameMonitoring();
                        }
                    });
                });
            }
            else
            {
                StatusText.Text = "启动游戏失败";
                HideProgress();
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"错误：{ex.Message}";
            App.LogError("启动失败", ex);
            HideProgress();
            MessageBox.Show($"启动失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _isDownloading = false;
            _downloadCts?.Dispose();
            _downloadCts = null;
            PlayButton.IsEnabled = true;
            LoadVersionsButton.IsEnabled = true;
        }
    }

    private void OnDownloadProgressChanged(object? sender, DownloadProgressEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            if (DownloadProgressPanel.Visibility == Visibility.Collapsed)
            {
                DownloadProgressPanel.Visibility = Visibility.Visible;
            }

            var progressValue = e.Progress;
            if (progressValue >= 0 && progressValue <= 100)
            {
                DownloadProgressBar.Value = progressValue;
                ProgressPercentText.Text = $"{progressValue:F1}%";
            }

            if (!string.IsNullOrEmpty(e.FileName))
            {
                var shortName = e.FileName.Length > 40 ? "..." + e.FileName.Substring(e.FileName.Length - 37) : e.FileName;
                ProgressStageText.Text = $"下载中：{shortName}";
            }
        });
    }

    private void ShowProgress(string stage, double progress)
    {
        DownloadProgressPanel.Visibility = Visibility.Visible;
        ProgressStageText.Text = stage;
        DownloadProgressBar.Value = progress;
        ProgressPercentText.Text = $"{progress:F0}%";
    }

    private void UpdateProgressStage(string stage, double progress)
    {
        ProgressStageText.Text = stage;
        DownloadProgressBar.Value = progress;
        ProgressPercentText.Text = $"{progress:F0}%";
        StatusText.Text = stage;
    }

    private void HideProgress()
    {
        DownloadProgressPanel.Visibility = Visibility.Collapsed;
        DownloadProgressBar.Value = 0;
        ProgressStageText.Text = "";
        ProgressPercentText.Text = "";
    }

    private void NavigationTab_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is TextBlock navButton)
        {
            switch (navButton.Name)
            {
                case "HomeNavButton":
                    ShowHomePage();
                    break;
                case "AIAssistantNavButton":
                    ShowAIAssistantPage();
                    break;
                case "ModsNavButton":
                    ShowModsPage();
                    break;
                case "AccountNavButton":
                    ShowAccountPage();
                    break;
                case "MultiplayerNavButton":
                    ShowMultiplayerPage();
                    break;
                case "ModpacksNavButton":
                    ShowModpacksPage();
                    break;
                case "SettingsNavButton":
                    ShowSettingsPage();
                    break;
            }
        }
    }

    private void ShowHomePage()
    {
        CloseSaveDetailPage();

        HomePage.Visibility = Visibility.Visible;
        AIAssistantPageGrid.Visibility = Visibility.Collapsed;
        ModsPage.Visibility = Visibility.Collapsed;
        AccountPageGrid.Visibility = Visibility.Collapsed;
        MultiplayerPageGrid.Visibility = Visibility.Collapsed;
        ModpacksPageGrid.Visibility = Visibility.Collapsed;
        SettingsPageGrid.Visibility = Visibility.Collapsed;
        SaveDetailPageGrid.Visibility = Visibility.Collapsed;

        HomeNavButton.Style = (Style)FindResource("NavigationButtonActive");
        AIAssistantNavButton.Style = (Style)FindResource("NavigationButton");
        ModsNavButton.Style = (Style)FindResource("NavigationButton");
        AccountNavButton.Style = (Style)FindResource("NavigationButton");
        MultiplayerNavButton.Style = (Style)FindResource("NavigationButton");
        ModpacksNavButton.Style = (Style)FindResource("NavigationButton");
        SettingsNavButton.Style = (Style)FindResource("NavigationButton");

        PageTitleText.Text = "主页";

        UpdateSaveRecordsList();

        ThemeAnimator.FadeIn(HomePage, 300);
    }

    private void ShowAIAssistantPage()
    {
        CloseSaveDetailPage();

        if (AIAssistantFrame.Content == null)
        {
            AIAssistantFrame.Navigate(new AIAssistantPage());
        }

        HomePage.Visibility = Visibility.Collapsed;
        AIAssistantPageGrid.Visibility = Visibility.Visible;
        ModsPage.Visibility = Visibility.Collapsed;
        AccountPageGrid.Visibility = Visibility.Collapsed;
        MultiplayerPageGrid.Visibility = Visibility.Collapsed;
        ModpacksPageGrid.Visibility = Visibility.Collapsed;
        SettingsPageGrid.Visibility = Visibility.Collapsed;
        SaveDetailPageGrid.Visibility = Visibility.Collapsed;

        HomeNavButton.Style = (Style)FindResource("NavigationButton");
        AIAssistantNavButton.Style = (Style)FindResource("NavigationButtonActive");
        ModsNavButton.Style = (Style)FindResource("NavigationButton");
        AccountNavButton.Style = (Style)FindResource("NavigationButton");
        MultiplayerNavButton.Style = (Style)FindResource("NavigationButton");
        ModpacksNavButton.Style = (Style)FindResource("NavigationButton");
        SettingsNavButton.Style = (Style)FindResource("NavigationButton");

        PageTitleText.Text = "AI 助手";

        ThemeAnimator.FadeIn(AIAssistantPageGrid, 300);
    }

    private void ShowModsPage()
    {
        CloseSaveDetailPage();

        if (ModsContent.Content == null)
        {
            ModsContent.Content = new ModPage();
        }

        HomePage.Visibility = Visibility.Collapsed;
        AIAssistantPageGrid.Visibility = Visibility.Collapsed;
        ModsPage.Visibility = Visibility.Visible;
        AccountPageGrid.Visibility = Visibility.Collapsed;
        MultiplayerPageGrid.Visibility = Visibility.Collapsed;
        ModpacksPageGrid.Visibility = Visibility.Collapsed;
        SettingsPageGrid.Visibility = Visibility.Collapsed;
        SaveDetailPageGrid.Visibility = Visibility.Collapsed;

        HomeNavButton.Style = (Style)FindResource("NavigationButton");
        AIAssistantNavButton.Style = (Style)FindResource("NavigationButton");
        ModsNavButton.Style = (Style)FindResource("NavigationButtonActive");
        AccountNavButton.Style = (Style)FindResource("NavigationButton");
        MultiplayerNavButton.Style = (Style)FindResource("NavigationButton");
        ModpacksNavButton.Style = (Style)FindResource("NavigationButton");
        SettingsNavButton.Style = (Style)FindResource("NavigationButton");

        PageTitleText.Text = "MOD";

        ThemeAnimator.FadeIn(ModsPage, 300);
    }

    private void ShowAccountPage()
    {
        CloseSaveDetailPage();

        if (AccountFrame.Content == null)
        {
            AccountFrame.Navigate(new AccountPage());
        }

        HomePage.Visibility = Visibility.Collapsed;
        AIAssistantPageGrid.Visibility = Visibility.Collapsed;
        ModsPage.Visibility = Visibility.Collapsed;
        AccountPageGrid.Visibility = Visibility.Visible;
        MultiplayerPageGrid.Visibility = Visibility.Collapsed;
        ModpacksPageGrid.Visibility = Visibility.Collapsed;
        SettingsPageGrid.Visibility = Visibility.Collapsed;
        SaveDetailPageGrid.Visibility = Visibility.Collapsed;

        HomeNavButton.Style = (Style)FindResource("NavigationButton");
        AIAssistantNavButton.Style = (Style)FindResource("NavigationButton");
        ModsNavButton.Style = (Style)FindResource("NavigationButton");
        AccountNavButton.Style = (Style)FindResource("NavigationButtonActive");
        MultiplayerNavButton.Style = (Style)FindResource("NavigationButton");
        ModpacksNavButton.Style = (Style)FindResource("NavigationButton");
        SettingsNavButton.Style = (Style)FindResource("NavigationButton");

        PageTitleText.Text = "账号";

        ThemeAnimator.FadeIn(AccountPageGrid, 300);
    }

    private void ShowMultiplayerPage()
    {
        CloseSaveDetailPage();

        if (MultiplayerFrame.Content == null)
        {
            MultiplayerFrame.Navigate(new MultiplayerPage());
        }

        HomePage.Visibility = Visibility.Collapsed;
        AIAssistantPageGrid.Visibility = Visibility.Collapsed;
        ModsPage.Visibility = Visibility.Collapsed;
        AccountPageGrid.Visibility = Visibility.Collapsed;
        MultiplayerPageGrid.Visibility = Visibility.Visible;
        ModpacksPageGrid.Visibility = Visibility.Collapsed;
        SettingsPageGrid.Visibility = Visibility.Collapsed;
        SaveDetailPageGrid.Visibility = Visibility.Collapsed;

        HomeNavButton.Style = (Style)FindResource("NavigationButton");
        AIAssistantNavButton.Style = (Style)FindResource("NavigationButton");
        ModsNavButton.Style = (Style)FindResource("NavigationButton");
        AccountNavButton.Style = (Style)FindResource("NavigationButton");
        MultiplayerNavButton.Style = (Style)FindResource("NavigationButtonActive");
        ModpacksNavButton.Style = (Style)FindResource("NavigationButton");
        SettingsNavButton.Style = (Style)FindResource("NavigationButton");

        PageTitleText.Text = "联机";

        ThemeAnimator.FadeIn(MultiplayerPageGrid, 300);
    }

    private void ShowModpacksPage()
    {
        CloseSaveDetailPage();

        if (ModpacksContent.Content == null)
        {
            ModpacksContent.Content = new ModpackPage();
        }

        HomePage.Visibility = Visibility.Collapsed;
        AIAssistantPageGrid.Visibility = Visibility.Collapsed;
        ModsPage.Visibility = Visibility.Collapsed;
        AccountPageGrid.Visibility = Visibility.Collapsed;
        MultiplayerPageGrid.Visibility = Visibility.Collapsed;
        ModpacksPageGrid.Visibility = Visibility.Visible;
        SettingsPageGrid.Visibility = Visibility.Collapsed;
        SaveDetailPageGrid.Visibility = Visibility.Collapsed;

        HomeNavButton.Style = (Style)FindResource("NavigationButton");
        AIAssistantNavButton.Style = (Style)FindResource("NavigationButton");
        ModsNavButton.Style = (Style)FindResource("NavigationButton");
        AccountNavButton.Style = (Style)FindResource("NavigationButton");
        MultiplayerNavButton.Style = (Style)FindResource("NavigationButton");
        ModpacksNavButton.Style = (Style)FindResource("NavigationButtonActive");
        SettingsNavButton.Style = (Style)FindResource("NavigationButton");

        PageTitleText.Text = "整合包";

        ThemeAnimator.FadeIn(ModpacksPageGrid, 300);
    }

    private void ShowSettingsPage()
    {
        CloseSaveDetailPage();

        if (SettingsFrame.Content == null)
        {
            SettingsFrame.Navigate(new SettingsPage());
        }

        HomePage.Visibility = Visibility.Collapsed;
        AIAssistantPageGrid.Visibility = Visibility.Collapsed;
        ModsPage.Visibility = Visibility.Collapsed;
        AccountPageGrid.Visibility = Visibility.Collapsed;
        MultiplayerPageGrid.Visibility = Visibility.Collapsed;
        ModpacksPageGrid.Visibility = Visibility.Collapsed;
        SettingsPageGrid.Visibility = Visibility.Visible;
        SaveDetailPageGrid.Visibility = Visibility.Collapsed;

        HomeNavButton.Style = (Style)FindResource("NavigationButton");
        AIAssistantNavButton.Style = (Style)FindResource("NavigationButton");
        ModsNavButton.Style = (Style)FindResource("NavigationButton");
        AccountNavButton.Style = (Style)FindResource("NavigationButton");
        MultiplayerNavButton.Style = (Style)FindResource("NavigationButton");
        ModpacksNavButton.Style = (Style)FindResource("NavigationButton");
        SettingsNavButton.Style = (Style)FindResource("NavigationButtonActive");

        PageTitleText.Text = "设置";

        ThemeAnimator.FadeIn(SettingsPageGrid, 300);
    }

    private void CloseSaveDetailPage()
    {
        if (SaveDetailPageGrid.Visibility == Visibility.Visible)
        {
            SaveDetailPageGrid.Visibility = Visibility.Collapsed;
            if (_currentSaveDetailPage != null)
            {
                _currentSaveDetailPage.BackRequested -= OnSaveDetailBackRequested;
                _currentSaveDetailPage.PlaySaveRequested -= OnPlaySaveRequested;
                _currentSaveDetailPage = null;
            }
        }
    }

    private void OnSaveDetailBackRequested()
    {
        ShowHomePage();
    }

    private void ShowSaveDetailPage(SaveRecord record)
    {
        _currentSaveDetailPage = new SaveDetailPage(record);
        _currentSaveDetailPage.BackRequested += OnSaveDetailBackRequested;
        _currentSaveDetailPage.PlaySaveRequested += OnPlaySaveRequested;

        SaveDetailFrame.Navigate(_currentSaveDetailPage);

        HomePage.Visibility = Visibility.Collapsed;
        AIAssistantPageGrid.Visibility = Visibility.Collapsed;
        ModsPage.Visibility = Visibility.Collapsed;
        AccountPageGrid.Visibility = Visibility.Collapsed;
        MultiplayerPageGrid.Visibility = Visibility.Collapsed;
        SettingsPageGrid.Visibility = Visibility.Collapsed;
        SaveDetailPageGrid.Visibility = Visibility.Visible;

        HomeNavButton.Style = (Style)FindResource("NavigationButton");
        AIAssistantNavButton.Style = (Style)FindResource("NavigationButton");
        ModsNavButton.Style = (Style)FindResource("NavigationButton");
        AccountNavButton.Style = (Style)FindResource("NavigationButton");
        MultiplayerNavButton.Style = (Style)FindResource("NavigationButton");
        SettingsNavButton.Style = (Style)FindResource("NavigationButton");

        PageTitleText.Text = "存档详情";

        ThemeAnimator.FadeIn(SaveDetailPageGrid, 300);
    }

    private async void OnPlaySaveRequested(SaveRecord saveRecord)
    {
        try
        {
            await PlaySaveRecord(saveRecord);
        }
        catch (Exception ex)
        {
            App.LogError("存档启动失败", ex);
            Dispatcher.Invoke(() =>
            {
                HideProgress();
                _isDownloading = false;
                PlayButton.IsEnabled = true;
                LoadVersionsButton.IsEnabled = true;
                MessageBox.Show($"启动失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            });
        }
    }

    private void SaveRecordDetailButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is SaveRecord record)
        {
            ShowSaveDetailPage(record);
        }
    }

    private async void SaveRecordListBox_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (SaveRecordListBox.SelectedItem is SaveRecord record)
        {
            try
            {
                App.LogInfo($"双击存档启动游戏：{record.SaveName} (v{record.GameVersion})");
                await PlaySaveRecord(record);
            }
            catch (Exception ex)
            {
                App.LogError("存档启动失败", ex);
                MessageBox.Show($"启动失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private async Task PlaySaveRecord(SaveRecord record)
    {
        App.LogInfo($"PlaySaveRecord 开始：{record.SaveName}");

        if (_isDownloading)
        {
            App.LogInfo("正在下载中，跳过启动");
            return;
        }

        var account = _accountService.GetSelectedAccount();
        if (account == null)
        {
            App.LogInfo("未选择账号");
            MessageBox.Show("请先登录或选择一个账号", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        App.LogInfo($"获取版本详情：{record.GameVersion}");
        var versionDetail = await GetVersionDetailByName(record.GameVersion);
        if (versionDetail == null)
        {
            App.LogError($"无法找到版本：{record.GameVersion}");
            MessageBox.Show($"无法找到版本：{record.GameVersion}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        App.LogInfo($"版本详情获取成功：{versionDetail.Id}");

        _isDownloading = true;
        PlayButton.IsEnabled = false;
        LoadVersionsButton.IsEnabled = false;

        try
        {
            ShowHomePage();
            ShowProgress("正在检查游戏文件...", 0);

            var cts = new CancellationTokenSource();

            UpdateProgressStage("正在下载游戏资源...", 10);
            await _minecraftService.PrepareVersionAsync(versionDetail, _gameDir, cts.Token);

            UpdateProgressStage("正在启动游戏...", 90);

            var launchService = new LaunchService(_gameDir);
            var javaPath = JavaPathTextBox.Text.Trim();
            if (string.IsNullOrEmpty(javaPath))
                javaPath = null;

            App.LogInfo($"启动游戏，存档：{record.SaveName}");
            var process = launchService.LaunchGame(versionDetail, account, javaPath, record.SaveName);

            if (process != null)
            {
                _currentGameProcess = process;
                _saveTrackingService.StartGameSession(process, versionDetail.Id ?? "unknown");

                UpdateProgressStage("游戏启动成功", 100);
                StatusText.Text = $"游戏已启动 (PID: {process.Id}) - 存档：{record.SaveName}";

                await Task.Delay(1500);
                HideProgress();

                var aiPage = AIAssistantFrame.Content as AIAssistantPage;
                if (aiPage != null)
                {
                    aiPage.StartGameMonitoring(process, versionDetail.Id ?? "unknown");
                }

                _ = Task.Run(async () =>
                {
                    await process.WaitForExitAsync();
                    Dispatcher.Invoke(() =>
                    {
                        _saveTrackingService.EndGameSession();
                        _currentGameProcess = null;
                        PlayButton.IsEnabled = true;
                        LoadVersionsButton.IsEnabled = true;
                        
                        var aiPage = AIAssistantFrame.Content as AIAssistantPage;
                        if (aiPage != null)
                        {
                            aiPage.StopGameMonitoring();
                        }
                    });
                });
            }
            else
            {
                StatusText.Text = "启动游戏失败";
                HideProgress();
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"错误：{ex.Message}";
            App.LogError("启动失败", ex);
            HideProgress();
            throw;
        }
        finally
        {
            _isDownloading = false;
            PlayButton.IsEnabled = true;
            LoadVersionsButton.IsEnabled = true;
        }
    }

    private async Task<VersionDetail?> GetVersionDetailByName(string versionName)
    {
        try
        {
            var localVersionJsonPath = Path.Combine(_gameDir, ".minecraft", "versions", versionName, $"{versionName}.json");
            if (File.Exists(localVersionJsonPath))
            {
                App.LogInfo($"从本地加载版本详情：{versionName}");
                var json = await File.ReadAllTextAsync(localVersionJsonPath);
                return JsonConvert.DeserializeObject<VersionDetail>(json);
            }

            if (_versionManifest == null)
            {
                _versionManifest = await _minecraftService.GetVersionManifestAsync();
            }

            var versionInfo = _versionManifest?.Versions?.FirstOrDefault(v => v.Id == versionName);
            if (versionInfo != null)
            {
                return await _minecraftService.GetVersionDetailAsync(versionInfo);
            }

            App.LogInfo($"版本 {versionName} 不在清单中，尝试从 BMCLAPI 获取");
            var bmclVersionUrl = $"https://bmclapi2.bangbang93.com/v1/games/{versionName}";
            try
            {
                var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(30);
                var response = await client.GetAsync(bmclVersionUrl);
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    return JsonConvert.DeserializeObject<VersionDetail>(content);
                }
            }
            catch (Exception ex)
            {
                App.LogError($"从 BMCLAPI 获取版本详情失败：{versionName}", ex);
            }
        }
        catch (Exception ex)
        {
            App.LogError($"获取版本详情失败：{versionName}", ex);
        }

        return null;
    }

    private void UpdateAccountDisplay()
    {
        var account = _accountService.GetSelectedAccount();
        if (account != null)
        {
            var accountType = account.Type == "microsoft" ? "微软正版" : "离线账号";
            CurrentAccountText.Text = $"{account.Name} ({accountType})";
        }
        else
        {
            CurrentAccountText.Text = "未选择账号";
        }
    }

    public void UpdateAccountDisplayFromAccountPage()
    {
        UpdateAccountDisplay();
    }

    private void ThemeToggleButton_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        ThemeManager.ToggleTheme();
    }

    private void MinimizeButton_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void CloseButton_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        Close();
    }

    private void WindowDragBorder_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            return;
        }
        DragMove();
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void BrowseJavaButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Java 可执行文件|java.exe|所有文件|*.*",
            Title = "选择 Java 路径"
        };

        if (dialog.ShowDialog() == true)
        {
            JavaPathTextBox.Text = dialog.FileName;
        }
    }
}