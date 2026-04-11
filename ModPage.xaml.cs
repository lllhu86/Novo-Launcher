using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using MinecraftLauncher.Services;
using Newtonsoft.Json;

namespace MinecraftLauncher;

public partial class ModPage : UserControl
{
    private readonly ModService _modService;
    private readonly TranslationService _translationService;
    private readonly string _modsDownloadPath;
    private List<ModrinthMod> _currentMods = new();
    private int _currentOffset = 0;
    private const int PageSize = 20;
    private string _lastSearchQuery = "";
    private string[] _lastLoaders = null;
    private ModrinthMod _selectedMod;
    private List<ModService.ModDep> _currentDependencies = new();
    private VersionFile _selectedVersion;

    public ModPage()
    {
        InitializeComponent();
        _modService = new ModService();
        _translationService = new TranslationService();
        _modsDownloadPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".minecraft", "mods");

        _modService.DownloadProgressChanged += (sender, e) =>
        {
            App.LogInfo($"MOD 下载进度：{e.FileName} - {e.Progress:F1}%");
        };

        StatusText.Text = "提示：使用 Modrinth API 搜索 MOD，无需 API Key。";

        Loaded += async (s, e) => await LoadPopularModsAsync();
    }

    private void SearchTextBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (SearchTextBox.Text == "搜索 MOD...")
        {
            SearchTextBox.Text = "";
            SearchTextBox.Foreground = System.Windows.Media.Brushes.White;
        }
    }

    private void SearchTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(SearchTextBox.Text))
        {
            SearchTextBox.Text = "搜索 MOD...";
            SearchTextBox.Foreground = System.Windows.Media.Brushes.Gray;
        }
    }

    private async void SearchButton_Click(object sender, RoutedEventArgs e)
    {
        await PerformSearchAsync();
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await PerformSearchAsync();
    }

    private async Task PerformSearchAsync()
    {
        try
        {
            SearchButton.IsEnabled = false;
            LoadMoreButton.IsEnabled = false;
            StatusText.Text = "正在搜索...";

            var searchQuery = SearchTextBox.Text == "搜索 MOD..." ? "" : SearchTextBox.Text;
            string[] loaders = GetSelectedLoaders();

            _lastSearchQuery = searchQuery;
            _lastLoaders = loaders;
            _currentOffset = 0;
            _currentMods.Clear();

            var mods = await _modService.SearchModsAsync(searchQuery, loaders, offset: 0, limit: PageSize);
            _currentMods.AddRange(mods);
            _currentOffset = mods.Count;

            ModsListBox.ItemsSource = null;
            ModsListBox.ItemsSource = _currentMods;

            StatusText.Text = mods.Count > 0 ? $"已加载 {mods.Count} 个 MOD（共 {_currentMods.Count} 个）" : "未找到相关 MOD";
            LoadMoreButton.IsEnabled = mods.Count == PageSize;

            DetailPanel.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            StatusText.Text = $"搜索失败：{ex.Message}";
            App.LogError("搜索 MOD 失败", ex);
        }
        finally
        {
            SearchButton.IsEnabled = true;
        }
    }

    private async void LoadMoreButton_Click(object sender, RoutedEventArgs e)
    {
        await LoadMoreModsAsync();
    }

    private async Task LoadMoreModsAsync()
    {
        try
        {
            LoadMoreButton.IsEnabled = false;
            StatusText.Text = "正在加载更多...";

            var mods = await _modService.SearchModsAsync(_lastSearchQuery, _lastLoaders, offset: _currentOffset, limit: PageSize);

            foreach (var mod in mods)
            {
                _currentMods.Add(mod);
            }
            _currentOffset += mods.Count;

            ModsListBox.ItemsSource = null;
            ModsListBox.ItemsSource = _currentMods;

            StatusText.Text = $"已加载 {_currentMods.Count} 个 MOD";
            LoadMoreButton.IsEnabled = mods.Count == PageSize;
        }
        catch (Exception ex)
        {
            StatusText.Text = $"加载更多失败：{ex.Message}";
            App.LogError("加载更多 MOD 失败", ex);
        }
        finally
        {
            LoadMoreButton.IsEnabled = true;
        }
    }

    private string[] GetSelectedLoaders()
    {
        if (ForgeRadio.IsChecked == true)
            return new[] { "forge" };
        if (FabricRadio.IsChecked == true)
            return new[] { "fabric" };
        return null;
    }

    private async Task LoadPopularModsAsync()
    {
        try
        {
            StatusText.Text = "正在加载热门 MOD...";
            var mods = await _modService.GetPopularModsAsync(PageSize);
            _currentMods = mods;
            _currentOffset = mods.Count;
            ModsListBox.ItemsSource = mods;
            StatusText.Text = $"已加载 {mods.Count} 个热门 MOD";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"加载失败：{ex.Message}";
            App.LogError("加载热门 MOD 失败", ex);
        }
    }

    private int GetSelectedModLoader()
    {
        if (ForgeRadio.IsChecked == true)
            return 1;
        if (FabricRadio.IsChecked == true)
            return 4;
        return 0;
    }

    private async void ModLoader_Changed(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(SearchTextBox.Text) && SearchTextBox.Text != "搜索 MOD...")
        {
            await PerformSearchAsync();
        }
    }

    private async void ModsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ModsListBox.SelectedItem is ModrinthMod mod)
        {
            _selectedMod = mod;
            await ShowModDetailsAsync(mod);
        }
    }

    private async Task ShowModDetailsAsync(ModrinthMod mod)
    {
        try
        {
            DetailPanel.Visibility = Visibility.Visible;
            StatusText.Text = $"正在加载 MOD 详情：{mod.Title}";

            DetailTitle.Text = mod.Title;
            DetailAuthor.Text = mod.Author;
            DetailDownloads.Text = mod.Downloads.ToString("N0");
            DetailDescription.Text = "正在加载详细介绍...";

            if (!string.IsNullOrEmpty(mod.IconUrl))
            {
                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(mod.IconUrl);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    DetailIcon.Source = bitmap;
                }
                catch
                {
                    DetailIcon.Source = null;
                }
            }
            else
            {
                DetailIcon.Source = null;
            }

            var modDetails = await _modService.GetModDetailsAsync(mod.ProjectId);

            if (modDetails.VersionFiles != null && modDetails.VersionFiles.Count > 0)
            {
                DetailVersion.Text = modDetails.VersionFiles[0].Version;
                VersionSelector.ItemsSource = modDetails.VersionFiles;
                VersionSelector.SelectedIndex = 0;
            }
            else
            {
                DetailVersion.Text = "无版本信息";
                VersionSelector.ItemsSource = null;
            }

            await LoadModDescriptionAsync(mod);

            await LoadDependenciesAsync(mod.ProjectId);
        }
        catch (Exception ex)
        {
            DetailDescription.Text = $"加载详情失败：{ex.Message}";
            App.LogError("加载 MOD 详情失败", ex);
        }
    }

    private async Task LoadModDescriptionAsync(ModrinthMod mod)
    {
        try
        {
            var description = await _modService.GetModDescriptionAsync(mod.ProjectId);
            if (!string.IsNullOrEmpty(description))
            {
                DetailDescription.Text = "正在翻译为中文...";
                var chineseDescription = await _translationService.TranslateToChineseAsync(description);
                DetailDescription.Text = chineseDescription;
            }
            else
            {
                var fallbackDesc = mod.Description ?? "暂无详细介绍";
                if (mod.Description != null && mod.Description != fallbackDesc)
                {
                    DetailDescription.Text = "正在翻译为中文...";
                    fallbackDesc = await _translationService.TranslateToChineseAsync(mod.Description);
                }
                DetailDescription.Text = fallbackDesc;
            }
        }
        catch
        {
            DetailDescription.Text = mod.Description ?? "暂无详细介绍";
        }
    }

    private async Task LoadDependenciesAsync(string projectId)
    {
        try
        {
            _currentDependencies.Clear();
            var dependencies = await _modService.GetModDependenciesAsync(projectId);

            if (dependencies != null && dependencies.Count > 0)
            {
                _currentDependencies = dependencies;
                DependenciesList.ItemsSource = _currentDependencies;
                DependenciesPanel.Visibility = Visibility.Visible;
                NoDependenciesText.Visibility = Visibility.Collapsed;
            }
            else
            {
                DependenciesPanel.Visibility = Visibility.Collapsed;
                NoDependenciesText.Visibility = Visibility.Visible;
            }
        }
        catch (Exception ex)
        {
            App.LogError("加载依赖失败", ex);
            DependenciesPanel.Visibility = Visibility.Collapsed;
            NoDependenciesText.Visibility = Visibility.Visible;
        }
    }

    private void VersionSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (VersionSelector.SelectedItem is VersionFile version)
        {
            _selectedVersion = version;
        }
    }

    private async void DownloadButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is ModrinthMod mod)
        {
            await DownloadModAsync(mod, null);
        }
    }

    private async void DetailDownloadButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedMod != null)
        {
            await DownloadModAsync(_selectedMod, _selectedVersion);
        }
    }

    private async Task DownloadModAsync(ModrinthMod mod, VersionFile preselectedFile)
    {
        try
        {
            DetailDownloadButton.IsEnabled = false;
            DetailDownloadButton.Content = "获取信息...";
            StatusText.Text = $"正在获取版本信息...";

            var modDetails = await _modService.GetModDetailsAsync(mod.ProjectId);

            if (modDetails.VersionFiles == null || modDetails.VersionFiles.Count == 0)
            {
                MessageBox.Show("该 MOD 没有可用的文件", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                DetailDownloadButton.IsEnabled = true;
                DetailDownloadButton.Content = "下载此版本";
                return;
            }

            VersionFile file = preselectedFile ?? modDetails.VersionFiles[0];

            var dialog = new SaveFileDialog
            {
                Filter = "MOD 文件 (*.jar)|*.jar|所有文件 (*.*)|*.*",
                FileName = file.Filename ?? $"{mod.Slug}-{file.Version}.jar",
                InitialDirectory = _modsDownloadPath
            };

            if (dialog.ShowDialog() == true)
            {
                DetailDownloadButton.Content = "下载中...";
                DetailDownloadButton.IsEnabled = true;
                StatusText.Text = $"正在下载：{file.Filename}";

                await _modService.DownloadModFileAsync(file.Url, dialog.FileName);

                StatusText.Text = "下载完成！";
                var result = MessageBox.Show(
                    $"MOD 下载完成！\n文件位置：{dialog.FileName}\n\n是否继续下载？",
                    "下载完成",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);

                if (result == MessageBoxResult.Yes)
                {
                    DetailDownloadButton.Content = "下载此版本";
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"下载失败：{ex.Message}";
            App.LogError("下载 MOD 失败", ex);
            MessageBox.Show($"下载失败：{ex.Message}", "错误",
                          MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            DetailDownloadButton.IsEnabled = true;
            DetailDownloadButton.Content = "下载此版本";
        }
    }
}