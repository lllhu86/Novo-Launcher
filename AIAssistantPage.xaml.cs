using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MinecraftLauncher.Services;
using MinecraftLauncher.Models;
using System.Diagnostics;

namespace MinecraftLauncher;

public partial class AIAssistantPage : Page
{
    private readonly AIService _aiService;
    private readonly HardwareService _hardwareService;
    private bool _isLoading;
    private string? _currentGameVersion;

    public event Action<GameErrorEvent>? ErrorDetected;
    public event Action<DiagnosisResult>? DiagnosisCompleted;

    public AIAssistantPage()
    {
        InitializeComponent();
        _aiService = new AIService();
        _hardwareService = new HardwareService();
        
        LoadHardwareInfo();
        SetupEventHandlers();
        
        if (!_aiService.IsConfigured)
        {
            ShowConfigReminder();
        }
        else
        {
            ShowWelcomeMessage();
        }
    }

    private void SetupEventHandlers()
    {
        _aiService.ErrorDetected += OnErrorDetected;
        _aiService.DiagnosisCompleted += OnDiagnosisCompleted;
    }

    private void OnErrorDetected(GameErrorEvent errorEvent)
    {
        Dispatcher.Invoke(() =>
        {
            AddMessage("assistant", $"⚠️ 检测到游戏错误！\n\n🔍 错误类型: {errorEvent.ErrorType}\n📋 严重程度: {errorEvent.Severity}\n\n💡 建议: {errorEvent.SuggestedAction}");
        });
        ErrorDetected?.Invoke(errorEvent);
    }

    private void OnDiagnosisCompleted(DiagnosisResult result)
    {
        Dispatcher.Invoke(() =>
        {
            var message = $"🔍 诊断完成\n\n";
            message += string.Join("\n", result.DiagnosticSteps);
            message += $"\n\n🎯 根本原因: {result.RootCause}";
            
            if (result.Solutions.Count > 0)
            {
                message += "\n\n💡 解决方案:";
                for (int i = 0; i < result.Solutions.Count; i++)
                {
                    var solution = result.Solutions[i];
                    message += $"\n  {i + 1}. {solution.Title}: {solution.Description}";
                }
            }
            
            AddMessage("assistant", message);
        });
        DiagnosisCompleted?.Invoke(result);
    }

    private void LoadHardwareInfo()
    {
        var hardware = _hardwareService.GetHardwareInfo();
        CpuInfoText.Text = $"CPU: {hardware.CpuName} ({hardware.CpuCores} 核心)";
        MemoryInfoText.Text = $"内存: {hardware.TotalMemoryGB:F1} GB";
        GpuInfoText.Text = $"GPU: {hardware.GpuName}";
        
        var versions = _aiService.GetInstalledVersions();
        VersionCountText.Text = $"已安装版本: {versions.Count} 个";
    }

    private void ShowConfigReminder()
    {
        AddMessage("assistant", "⚠️ 请先点击「设置」按钮配置 API Key，以便使用 AI 功能。\n\n支持 OpenAI 兼容 API（如 OpenAI、Azure、本地部署的模型等）。");
    }

    private void ShowWelcomeMessage()
    {
        var versions = _aiService.GetInstalledVersions();
        var saves = _aiService.GetAllSaves();
        
        var welcomeMsg = "👋 你好！我是你的 Minecraft 启动器智能助手「小矿工」！\n\n";
        welcomeMsg += "🎮 我可以帮你：\n";
        welcomeMsg += "  • 📦 管理和诊断模组问题\n";
        welcomeMsg += "  • 💾 分析存档状态\n";
        welcomeMsg += "  • 🔧 优化游戏配置\n";
        welcomeMsg += "  • 🐛 排查游戏崩溃问题\n";
        welcomeMsg += "  • 📝 实时监控游戏日志\n\n";
        
        if (versions.Count > 0)
        {
            welcomeMsg += $"📊 检测到 {versions.Count} 个游戏版本";
            if (saves.Count > 0)
            {
                welcomeMsg += $"，{saves.Count} 个存档";
            }
            welcomeMsg += "\n\n";
        }
        
        welcomeMsg += "💡 试试点击下方的快捷按钮，或直接输入你的问题！";
        
        AddMessage("assistant", welcomeMsg);
    }

    private void AddMessage(string role, string content)
    {
        var panel = new StackPanel
        {
            Margin = new Thickness(0, 0, 0, 10),
            HorizontalAlignment = role == "user" ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            MaxWidth = 550
        };

        var namePanel = new StackPanel { Orientation = Orientation.Horizontal };
        var nameText = new TextBlock
        {
            Text = role == "user" ? "你" : "🤖 小矿工",
            FontWeight = FontWeights.Bold,
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 5),
            Foreground = (Brush)Application.Current.FindResource("AccentBrush")
        };
        namePanel.Children.Add(nameText);

        var timeText = new TextBlock
        {
            Text = DateTime.Now.ToString("HH:mm"),
            FontSize = 10,
            Margin = new Thickness(10, 0, 0, 5),
            Foreground = (Brush)Application.Current.FindResource("SecondaryForegroundBrush"),
            VerticalAlignment = VerticalAlignment.Center
        };
        namePanel.Children.Add(timeText);

        var border = new Border
        {
            Background = (Brush)Application.Current.FindResource("TertiaryBackgroundBrush"),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12)
        };

        var contentText = new TextBlock
        {
            Text = content,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Application.Current.FindResource("PrimaryForegroundBrush")
        };

        border.Child = contentText;
        panel.Children.Add(namePanel);
        panel.Children.Add(border);

        ChatMessagesPanel.Children.Add(panel);
        
        Dispatcher.BeginInvoke(() =>
        {
            ChatScrollViewer.ScrollToEnd();
        });
    }

    private async void SendButton_Click(object sender, RoutedEventArgs e)
    {
        await SendMessage();
    }

    private async void InputTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            e.Handled = true;
            await SendMessage();
        }
    }

    private async Task SendMessage()
    {
        var message = InputTextBox.Text.Trim();
        if (string.IsNullOrEmpty(message)) return;

        InputTextBox.Text = string.Empty;
        AddMessage("user", message);
        
        _isLoading = true;
        LoadingPanel.Visibility = Visibility.Visible;
        SendButton.IsEnabled = false;

        try
        {
            var response = await _aiService.SendMessageAsync(message);
            AddMessage("assistant", response);
        }
        catch (Exception ex)
        {
            AddMessage("assistant", $"❌ 错误: {ex.Message}");
        }
        finally
        {
            _isLoading = false;
            LoadingPanel.Visibility = Visibility.Collapsed;
            SendButton.IsEnabled = true;
        }
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        _aiService.ClearConversation();
        ChatMessagesPanel.Children.Clear();
        ShowWelcomeMessage();
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AIConfigDialog(_aiService.GetConfig());
        dialog.Owner = Window.GetWindow(this);
        if (dialog.ShowDialog() == true)
        {
            _aiService.SaveConfig(dialog.Config);
            if (_aiService.IsConfigured)
            {
                MessageBox.Show("✅ API 配置已保存！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
    }

    private async void OptimizeButton_Click(object sender, RoutedEventArgs e)
    {
        AddMessage("user", "帮我优化游戏配置");
        
        _isLoading = true;
        LoadingPanel.Visibility = Visibility.Visible;
        
        try
        {
            var recommendation = await _aiService.GetGameSettingsRecommendationAsync("balanced");
            var response = $"💎 根据您的硬件配置，我推荐以下设置：\n\n" +
                          $"• 内存分配: {recommendation.RecommendedMemoryMB / 1024.0:F1} GB\n" +
                          $"• 渲染距离: {recommendation.RenderDistance} 区块\n" +
                          $"• 图像品质: {recommendation.GraphicsMode}\n\n" +
                          $"📝 JVM 参数:\n{recommendation.JvmArgs}\n\n" +
                          $"{recommendation.Explanation}";
            
            AddMessage("assistant", response);
        }
        catch (Exception ex)
        {
            AddMessage("assistant", $"❌ 获取推荐失败: {ex.Message}");
        }
        finally
        {
            _isLoading = false;
            LoadingPanel.Visibility = Visibility.Collapsed;
        }
    }

    private async void DiagnoseButton_Click(object sender, RoutedEventArgs e)
    {
        var versions = _aiService.GetInstalledVersions();
        if (versions.Count == 0)
        {
            AddMessage("assistant", "❌ 未检测到已安装的游戏版本。请先下载一个游戏版本。");
            return;
        }
        
        AddMessage("user", "帮我诊断游戏问题");
        _isLoading = true;
        LoadingPanel.Visibility = Visibility.Visible;
        
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("🔍 正在进行系统诊断...\n");
            
            sb.AppendLine("📦 已安装版本:");
            foreach (var v in versions.Take(5))
            {
                sb.AppendLine($"  • {v.Id} ({v.ModLoader}, {v.ModCount} 模组)");
            }
            
            var allConflicts = new List<ModConflictInfo>();
            foreach (var v in versions.Where(v => v.ModCount > 0))
            {
                var conflicts = _aiService.DetectModConflicts(v.Id);
                if (conflicts.Count > 0)
                {
                    allConflicts.AddRange(conflicts);
                }
            }
            
            if (allConflicts.Count > 0)
            {
                sb.AppendLine($"\n⚠️ 检测到 {allConflicts.Count} 个模组问题:");
                foreach (var conflict in allConflicts.Take(5))
                {
                    sb.AppendLine($"  • [{conflict.Severity}] {conflict.Description}");
                }
            }
            else
            {
                sb.AppendLine("\n✅ 未检测到模组冲突");
            }
            
            var logInfo = _aiService.GetLauncherLog();
            if (logInfo.ErrorCount > 0)
            {
                sb.AppendLine($"\n❌ 启动器日志中有 {logInfo.ErrorCount} 个错误");
                if (logInfo.ErrorLines.Count > 0)
                {
                    sb.AppendLine("最近的错误:");
                    foreach (var line in logInfo.ErrorLines.Take(3))
                    {
                        sb.AppendLine($"  {line}");
                    }
                }
            }
            
            var suggestions = await _aiService.GetOptimizationSuggestionsAsync();
            if (suggestions.Count > 0)
            {
                sb.AppendLine($"\n💡 优化建议:");
                foreach (var s in suggestions.Take(3))
                {
                    sb.AppendLine($"  • {s.Title}: {s.Description}");
                }
            }
            
            AddMessage("assistant", sb.ToString());
        }
        catch (Exception ex)
        {
            AddMessage("assistant", $"❌ 诊断失败: {ex.Message}");
        }
        finally
        {
            _isLoading = false;
            LoadingPanel.Visibility = Visibility.Collapsed;
        }
    }

    private async void AnalyzeModsButton_Click(object sender, RoutedEventArgs e)
    {
        var versions = _aiService.GetInstalledVersions().Where(v => v.ModCount > 0).ToList();
        if (versions.Count == 0)
        {
            AddMessage("assistant", "❌ 未检测到安装了模组的游戏版本。");
            return;
        }
        
        var version = versions[0];
        AddMessage("user", $"分析 {version.Id} 的模组");
        _isLoading = true;
        LoadingPanel.Visibility = Visibility.Visible;
        
        try
        {
            var analysis = await _aiService.GetModAnalysisAsync(version.Id);
            AddMessage("assistant", analysis);
        }
        catch (Exception ex)
        {
            AddMessage("assistant", $"❌ 分析失败: {ex.Message}");
        }
        finally
        {
            _isLoading = false;
            LoadingPanel.Visibility = Visibility.Collapsed;
        }
    }

    private async void AnalyzeSavesButton_Click(object sender, RoutedEventArgs e)
    {
        var saves = _aiService.GetAllSaves();
        if (saves.Count == 0)
        {
            AddMessage("assistant", "❌ 未检测到存档。请先创建一个存档。");
            return;
        }
        
        AddMessage("user", "分析我的存档");
        _isLoading = true;
        LoadingPanel.Visibility = Visibility.Visible;
        
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine($"📊 存档概览 ({saves.Count} 个存档)\n");
            
            var largeSaves = saves.Where(s => s.SizeBytes > 500 * 1024 * 1024).ToList();
            var recentSaves = saves.Take(5).ToList();
            
            sb.AppendLine("🕐 最近游玩:");
            foreach (var save in recentSaves)
            {
                sb.AppendLine($"  • {save.DisplayName} ({save.Version}) - {save.SizeDisplay}");
            }
            
            if (largeSaves.Count > 0)
            {
                sb.AppendLine($"\n⚠️ 大型存档 (>500MB):");
                foreach (var save in largeSaves)
                {
                    sb.AppendLine($"  • {save.DisplayName} - {save.SizeDisplay}");
                }
                sb.AppendLine("\n💡 建议使用 MCEdit 修剪未加载区块以减小存档体积。");
            }
            
            AddMessage("assistant", sb.ToString());
        }
        catch (Exception ex)
        {
            AddMessage("assistant", $"❌ 分析失败: {ex.Message}");
        }
        finally
        {
            _isLoading = false;
            LoadingPanel.Visibility = Visibility.Collapsed;
        }
    }

    private async void ViewLogsButton_Click(object sender, RoutedEventArgs e)
    {
        AddMessage("user", "分析最新的错误日志");
        LoadingPanel.Visibility = Visibility.Visible;
        _isLoading = true;

        try
        {
            var logInfo = _aiService.GetLauncherLog();
            
            if (logInfo.ErrorCount == 0)
            {
                AddMessage("assistant", "✅ 启动器日志中没有检测到错误。\n\n如果你遇到了游戏问题，请确保游戏已经运行过并产生了日志文件。");
                return;
            }

            var errorContent = string.Join("\n", logInfo.ErrorLines.Take(50));
            
            if (_aiService.IsConfigured)
            {
                var response = await _aiService.AnalyzeErrorLogAsync(errorContent);
                AddMessage("assistant", response);
            }
            else
            {
                var response = $"📋 检测到 {logInfo.ErrorCount} 个错误:\n\n{errorContent}\n\n💡 请配置 API Key 以获得更详细的错误分析。";
                AddMessage("assistant", response);
            }
        }
        catch (Exception ex)
        {
            AddMessage("assistant", $"❌ 分析日志失败: {ex.Message}");
        }
        finally
        {
            _isLoading = false;
            LoadingPanel.Visibility = Visibility.Collapsed;
        }
    }

    private async void RecipeButton_Click(object sender, RoutedEventArgs e)
    {
        AddMessage("user", "告诉我钻石装备的合成方法");
        
        _isLoading = true;
        LoadingPanel.Visibility = Visibility.Visible;
        
        try
        {
            var response = await _aiService.SendMessageAsync(
                "请告诉我钻石装备的合成配方，包括钻石剑、钻石镐、钻石盔甲等。使用中文回答，用简洁的格式列出合成表。");
            AddMessage("assistant", response);
        }
        catch (Exception ex)
        {
            AddMessage("assistant", $"❌ 获取信息失败: {ex.Message}");
        }
        finally
        {
            _isLoading = false;
            LoadingPanel.Visibility = Visibility.Collapsed;
        }
    }

    private async void ModsButton_Click(object sender, RoutedEventArgs e)
    {
        AddMessage("user", "推荐一些好玩的模组");

        _isLoading = true;
        LoadingPanel.Visibility = Visibility.Visible;

        try
        {
            var response = await _aiService.SendMessageAsync(
                "请推荐一些热门且好玩的 Minecraft 模组，按类型分类（生存、冒险、科技、魔法等），用中文回答。");
            AddMessage("assistant", response);
        }
        catch (Exception ex)
        {
            AddMessage("assistant", $"❌ 获取推荐失败: {ex.Message}");
        }
        finally
        {
            _isLoading = false;
            LoadingPanel.Visibility = Visibility.Collapsed;
        }
    }

    public void StartGameMonitoring(Process gameProcess, string version)
    {
        _currentGameVersion = version;
        _aiService.StartGameMonitoring(gameProcess, version);
        AddMessage("assistant", $"🎮 开始监控游戏: {version}\n\n我将实时监控游戏日志，一旦发现问题会立即通知你！");
    }

    public void StopGameMonitoring()
    {
        _aiService.StopGameMonitoring();
        _currentGameVersion = null;
    }

    public AIService GetAIService() => _aiService;
}
