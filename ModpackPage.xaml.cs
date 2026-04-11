using System.IO;
using System.IO.Compression;
using System.Windows;
using System.Windows.Controls;
using MinecraftLauncher.Services;
using Microsoft.Win32;
using Newtonsoft.Json;

namespace MinecraftLauncher;

public partial class ModpackPage : UserControl
{
    private readonly ModpackService _modpackService;
    private readonly string _modpacksPath;
    private readonly string _instancesPath;
    private readonly TranslationService _translationService;
    private List<ModpackInfo> _modpacks = new();
    private ModpackInfo _selectedModpack;
    private ModpackDetails _selectedModpackDetails;
    private bool _isLocalModpacksView = true;

    public ModpackPage()
    {
        InitializeComponent();

        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        _modpacksPath = Path.Combine(baseDir, ".minecraft", "modpacks");
        _instancesPath = Path.Combine(baseDir, ".minecraft", "instances");
        _modpackService = new ModpackService();
        _translationService = new TranslationService();

        if (!Directory.Exists(_modpacksPath))
        {
            Directory.CreateDirectory(_modpacksPath);
        }

        _modpackService.DownloadProgressChanged += (sender, e) =>
        {
            Dispatcher.Invoke(() =>
            {
                if (DownloadProgressPanel.Visibility == Visibility.Collapsed)
                {
                    DownloadProgressPanel.Visibility = Visibility.Visible;
                }
                DownloadProgressBar.Value = e.Progress;
                StatusText.Text = $"下载中：{e.FileName} - {e.Progress:F1}%";
            });
        };

        Loaded += (s, e) => LoadLocalModpacks();
    }

    private void LoadLocalModpacks()
    {
        try
        {
            _modpacks.Clear();

            if (Directory.Exists(_modpacksPath))
            {
                var jsonFiles = Directory.GetFiles(_modpacksPath, "*.json", SearchOption.AllDirectories);
                foreach (var jsonFile in jsonFiles)
                {
                    try
                    {
                        var json = File.ReadAllText(jsonFile);
                        var modpack = JsonConvert.DeserializeObject<ModpackInfo>(json);
                        if (modpack != null)
                        {
                            modpack.ConfigPath = jsonFile;
                            _modpacks.Add(modpack);
                        }
                    }
                    catch (Exception ex)
                    {
                        App.LogError($"加载整合包配置失败: {jsonFile}", ex);
                    }
                }
            }

            ModpacksListBox.ItemsSource = null;
            ModpacksListBox.ItemsSource = _modpacks;
            ModpackCountText.Text = $"共 {_modpacks.Count} 个本地整合包";

            EmptyStatePanel.Visibility = _modpacks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            ModpacksListBox.Visibility = _modpacks.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

            if (_modpacks.Count == 0)
            {
                StatusText.Text = "暂无本地整合包，点击「浏览在线」下载整合包";
            }
            else
            {
                StatusText.Text = $"已加载 {_modpacks.Count} 个本地整合包";
            }
        }
        catch (Exception ex)
        {
            App.LogError("加载整合包列表失败", ex);
            MessageBox.Show($"加载整合包列表失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void LoadOnlineModpacks()
    {
        try
        {
            StatusText.Text = "正在加载热门整合包...";
            EmptyStatePanel.Visibility = Visibility.Collapsed;
            ModpacksListBox.Visibility = Visibility.Visible;

            var modpacks = await _modpackService.GetFeaturedModpacksAsync(30);
            _modpacks = modpacks;

            ModpacksListBox.ItemsSource = null;
            ModpacksListBox.ItemsSource = _modpacks;
            ModpackCountText.Text = $"共 {_modpacks.Count} 个热门整合包";

            StatusText.Text = modpacks.Count > 0 ? $"已加载 {modpacks.Count} 个热门整合包" : "加载失败，请检查网络连接";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"加载失败：{ex.Message}";
            App.LogError("加载在线整合包失败", ex);
        }
    }

    private async void ImportModpackButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isLocalModpacksView)
        {
            var dialog = new OpenFileDialog
            {
                Title = "选择整合包文件",
                Filter = "整合包文件 (*.zip;*.mrpack)|*.zip;*.mrpack|所有文件 (*.*)|*.*",
                Multiselect = false
            };

            if (dialog.ShowDialog() == true)
            {
                await ImportLocalModpackAsync(dialog.FileName);
            }
        }
        else
        {
            if (_selectedModpack != null)
            {
                await DownloadSelectedModpackAsync();
            }
            else
            {
                MessageBox.Show("请先选择一个要下载的整合包", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
    }

    private void ViewModeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isLocalModpacksView)
        {
            _isLocalModpacksView = false;
            ViewModeButton.Content = "本地整合包";
            TitleText.Text = "浏览整合包";
            LoadOnlineModpacks();
        }
        else
        {
            _isLocalModpacksView = true;
            ViewModeButton.Content = "浏览在线";
            TitleText.Text = "本地整合包";
            LoadLocalModpacks();
        }
    }

    private async Task DownloadSelectedModpackAsync()
    {
        try
        {
            StatusText.Text = $"正在获取整合包信息：{_selectedModpack.Name}...";

            var source = _selectedModpack.Source ?? "modrinth";
            _selectedModpackDetails = await _modpackService.GetModpackDetailsAsync(_selectedModpack.ProjectId, source);
            if (_selectedModpackDetails == null || string.IsNullOrEmpty(_selectedModpackDetails.FileUrl))
            {
                MessageBox.Show("无法获取整合包下载链接，请稍后重试", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var downloadsDir = Path.Combine(_modpacksPath, "Downloads");
            if (!Directory.Exists(downloadsDir))
            {
                Directory.CreateDirectory(downloadsDir);
            }

            var fileName = _selectedModpackDetails.FileName ?? $"{_selectedModpack.Slug}-latest.mrpack";
            var savePath = Path.Combine(downloadsDir, fileName);

            DownloadProgressPanel.Visibility = Visibility.Visible;
            ImportModpackButton.IsEnabled = false;

            await _modpackService.DownloadModpackAsync(_selectedModpackDetails.FileUrl, savePath);

            DownloadProgressPanel.Visibility = Visibility.Collapsed;
            ImportModpackButton.IsEnabled = true;

            var modpackInfo = new ModpackInfo
            {
                Name = _selectedModpackDetails.Name,
                Slug = _selectedModpackDetails.Slug,
                ProjectId = _selectedModpackDetails.ProjectId,
                GameVersion = _selectedModpackDetails.GameVersion,
                ModLoader = _selectedModpackDetails.ModLoader,
                Description = _selectedModpackDetails.Description,
                Author = _selectedModpackDetails.Author,
                Downloads = _selectedModpackDetails.Downloads,
                IconUrl = _selectedModpackDetails.IconUrl,
                DownloadPath = savePath
            };

            var configPath = Path.Combine(_modpacksPath, $"{_selectedModpackDetails.Slug}.json");
            var configJson = JsonConvert.SerializeObject(modpackInfo, Formatting.Indented);
            await File.WriteAllTextAsync(configPath, configJson);

            StatusText.Text = $"整合包「{modpackInfo.Name}」下载成功！";
            MessageBox.Show($"整合包「{modpackInfo.Name}」下载成功！", "下载完成", MessageBoxButton.OK, MessageBoxImage.Information);

            LoadOnlineModpacks();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"下载失败：{ex.Message}";
            App.LogError($"下载整合包失败：{_selectedModpack?.Name}", ex);
            MessageBox.Show($"下载失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            DownloadProgressPanel.Visibility = Visibility.Collapsed;
            ImportModpackButton.IsEnabled = true;
        }
    }

    private async Task ImportLocalModpackAsync(string filePath)
    {
        try
        {
            var fileName = Path.GetFileNameWithoutExtension(filePath);
            var modpackDir = Path.Combine(_modpacksPath, fileName);
            var configPath = Path.Combine(modpackDir, "modpack.json");

            StatusText.Text = $"正在导入整合包：{fileName}...";

            if (!Directory.Exists(modpackDir))
            {
                Directory.CreateDirectory(modpackDir);
            }

            if (filePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                await Task.Run(() => ZipFile.ExtractToDirectory(filePath, modpackDir, true));
            }
            else if (filePath.EndsWith(".mrpack", StringComparison.OrdinalIgnoreCase))
            {
                await ExtractMrpackAsync(filePath, modpackDir);
            }

            if (File.Exists(configPath))
            {
                var json = await File.ReadAllTextAsync(configPath);
                var modpack = JsonConvert.DeserializeObject<ModpackInfo>(json);
                if (modpack != null)
                {
                    modpack.ConfigPath = configPath;
                    var updatedJson = JsonConvert.SerializeObject(modpack, Formatting.Indented);
                    await File.WriteAllTextAsync(configPath, updatedJson);
                }
            }
            else
            {
                var newModpack = new ModpackInfo
                {
                    Name = fileName,
                    ConfigPath = configPath,
                    Description = "用户导入的整合包",
                    GameVersion = "未知",
                    ModLoader = "未知",
                    ModCount = 0
                };
                var json = JsonConvert.SerializeObject(newModpack, Formatting.Indented);
                await File.WriteAllTextAsync(configPath, json);
            }

            LoadLocalModpacks();
            StatusText.Text = $"整合包导入成功：{fileName}";
            MessageBox.Show($"整合包「{fileName}」导入成功！", "导入成功", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            App.LogError($"导入整合包失败：{filePath}", ex);
            MessageBox.Show($"导入整合包失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task ExtractMrpackAsync(string mrpackPath, string destDir)
    {
        await Task.Run(() =>
        {
            using var archive = ZipFile.OpenRead(mrpackPath);

            var overridesEntry = archive.GetEntry("modrinth.index.json");
            string overridesFolder = "overrides";
            if (overridesEntry != null)
            {
                using var stream = overridesEntry.Open();
                using var reader = new StreamReader(stream);
                var indexJson = reader.ReadToEnd();

                var index = JsonConvert.DeserializeObject<dynamic>(indexJson);
                if (index != null && index.overrides != null)
                {
                    overridesFolder = index.overrides.ToString();
                }
            }

            var overridesDir = Path.Combine(destDir, overridesFolder);
            if (!Directory.Exists(overridesDir))
            {
                Directory.CreateDirectory(overridesDir);
            }

            foreach (var entry in archive.Entries)
            {
                if (entry.FullName.StartsWith("files/") && !entry.FullName.EndsWith("/"))
                {
                    var relativePath = entry.FullName.Substring(6);
                    var destPath = Path.Combine(destDir, relativePath);
                    var fileDir = Path.GetDirectoryName(destPath);
                    if (!string.IsNullOrEmpty(fileDir) && !Directory.Exists(fileDir))
                    {
                        Directory.CreateDirectory(fileDir);
                    }
                    if (!string.IsNullOrEmpty(entry.Name))
                    {
                        entry.ExtractToFile(destPath, true);
                    }
                }
            }
        });

        App.LogInfo($"解压 mrpack 完成：{destDir}");
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isLocalModpacksView)
        {
            LoadLocalModpacks();
        }
        else
        {
            LoadOnlineModpacks();
        }
    }

    private void ModpacksListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ModpacksListBox.SelectedItem is ModpackInfo modpack)
        {
            _selectedModpack = modpack;
        }
    }

    private async void LaunchModpackButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is ModpackInfo modpack)
        {
            await LaunchModpackAsync(modpack);
        }
    }

    private async Task LaunchModpackAsync(ModpackInfo modpack)
    {
        try
        {
            string modpackDir;

            if (!string.IsNullOrEmpty(modpack.DownloadPath) && File.Exists(modpack.DownloadPath))
            {
                modpackDir = Path.Combine(_modpacksPath, modpack.Slug ?? modpack.Name);
                if (!Directory.Exists(modpackDir))
                {
                    Directory.CreateDirectory(modpackDir);
                }
                await ExtractMrpackAsync(modpack.DownloadPath, modpackDir);
            }
            else if (!string.IsNullOrEmpty(modpack.ConfigPath) && Directory.Exists(Path.GetDirectoryName(modpack.ConfigPath)))
            {
                modpackDir = Path.GetDirectoryName(modpack.ConfigPath);
            }
            else
            {
                MessageBox.Show("整合包文件不存在，请检查整合包是否正确导入", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var instanceName = modpack.Slug ?? modpack.Name;
            var instancePath = Path.Combine(_instancesPath, instanceName);

            if (!Directory.Exists(instancePath))
            {
                Directory.CreateDirectory(instancePath);
            }

            var overridesDir = Path.Combine(modpackDir, "overrides");
            if (Directory.Exists(overridesDir))
            {
                CopyDirectory(overridesDir, instancePath, true);
            }

            var modsDir = Path.Combine(modpackDir, "mods");
            if (Directory.Exists(modsDir))
            {
                var instanceModsDir = Path.Combine(instancePath, "mods");
                if (!Directory.Exists(instanceModsDir))
                {
                    Directory.CreateDirectory(instanceModsDir);
                }
                CopyDirectory(modsDir, instanceModsDir, true);
            }

            var configDir = Path.Combine(modpackDir, "config");
            if (Directory.Exists(configDir))
            {
                var instanceConfigDir = Path.Combine(instancePath, "config");
                if (!Directory.Exists(instanceConfigDir))
                {
                    Directory.CreateDirectory(instanceConfigDir);
                }
                CopyDirectory(configDir, instanceConfigDir, true);
            }

            MessageBox.Show(
                $"整合包「{modpack.Name}」已准备就绪！\n\n实例路径：{instancePath}\n\n请在主页选择该版本后启动游戏。",
                "整合包已就绪",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            App.LogInfo($"整合包 {modpack.Name} 已准备就绪，实例路径: {instancePath}");
        }
        catch (Exception ex)
        {
            App.LogError($"启动整合包失败：{modpack.Name}", ex);
            MessageBox.Show($"启动整合包失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void DeleteModpackButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is ModpackInfo modpack)
        {
            var result = MessageBox.Show(
                $"确定要删除整合包「{modpack.Name}」吗？\n\n这将删除所有相关文件，但不会影响已创建的实例。",
                "确认删除",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                DeleteModpack(modpack);
            }
        }
    }

    private void DeleteModpack(ModpackInfo modpack)
    {
        try
        {
            if (!string.IsNullOrEmpty(modpack.ConfigPath))
            {
                var modpackDir = Path.GetDirectoryName(modpack.ConfigPath);
                if (!string.IsNullOrEmpty(modpackDir) && Directory.Exists(modpackDir))
                {
                    Directory.Delete(modpackDir, true);
                }
            }

            if (!string.IsNullOrEmpty(modpack.DownloadPath) && File.Exists(modpack.DownloadPath))
            {
                File.Delete(modpack.DownloadPath);
            }

            LoadLocalModpacks();
            MessageBox.Show($"整合包「{modpack.Name}」已删除", "删除成功", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            App.LogError($"删除整合包失败：{modpack.Name}", ex);
            MessageBox.Show($"删除整合包失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CopyDirectory(string sourceDir, string destDir, bool recursive)
    {
        var dir = new DirectoryInfo(sourceDir);
        if (!dir.Exists)
            return;

        if (!Directory.Exists(destDir))
        {
            Directory.CreateDirectory(destDir);
        }

        foreach (FileInfo file in dir.GetFiles())
        {
            var destPath = Path.Combine(destDir, file.Name);
            try
            {
                file.CopyTo(destPath, true);
            }
            catch (Exception ex)
            {
                App.LogError($"复制文件失败：{file.FullName} -> {destPath}", ex);
            }
        }

        if (recursive)
        {
            foreach (DirectoryInfo subDir in dir.GetDirectories())
            {
                var newDestDir = Path.Combine(destDir, subDir.Name);
                CopyDirectory(subDir.FullName, newDestDir, true);
            }
        }
    }
}

public class ModpackInfo
{
    [JsonProperty("project_id")]
    public string ProjectId { get; set; }

    [JsonProperty("slug")]
    public string Slug { get; set; }

    [JsonProperty("name")]
    public string Name { get; set; }

    [JsonProperty("version")]
    public string Version { get; set; }

    [JsonProperty("game_version")]
    public string GameVersion { get; set; }

    [JsonProperty("description")]
    public string Description { get; set; }

    [JsonProperty("author")]
    public string Author { get; set; }

    [JsonProperty("downloads")]
    public int Downloads { get; set; }

    [JsonProperty("mod_loader")]
    public string ModLoader { get; set; }

    [JsonProperty("mod_count")]
    public int ModCount { get; set; }

    [JsonProperty("icon_url")]
    public string IconUrl { get; set; }

    [JsonProperty("download_path")]
    public string DownloadPath { get; set; }

    [JsonProperty("source")]
    public string Source { get; set; }

    [JsonIgnore]
    public string ConfigPath { get; set; }

    [JsonIgnore]
    public string IconPath => IconUrl;

    public ModpackDetails ToDetails()
    {
        return new ModpackDetails
        {
            ProjectId = ProjectId,
            Slug = Slug,
            Name = Name,
            Description = Description,
            Author = Author,
            Downloads = Downloads,
            IconUrl = IconUrl,
            GameVersion = GameVersion,
            ModLoader = ModLoader,
            ModCount = ModCount,
            Source = Source,
            FileUrl = "",
            FileName = "",
            Versions = new List<ModpackVersion>()
        };
    }
}