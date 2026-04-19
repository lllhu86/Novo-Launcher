using System.Net.Http;
using System.Text;
using Newtonsoft.Json;
using MinecraftLauncher.Models;
using System.IO;

namespace MinecraftLauncher.Services;

public class AIService : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly AIConfig _config;
    private readonly List<ChatMessage> _conversationHistory;
    private readonly HardwareService _hardwareService;
    private readonly LauncherDataService _launcherDataService;
    private readonly GameLogMonitorService _logMonitorService;
    private readonly ProblemDiagnosisService _diagnosisService;
    private readonly AICommandParser _commandParser;
    private readonly AIResourceService _resourceService;
    private readonly AIProactiveAlertService _proactiveAlertService;
    private bool _disposed;
    private string _gameDir;

    public event Action? ConversationCleared;
    public event Action<GameErrorEvent>? ErrorDetected;
    public event Action<DiagnosisResult>? DiagnosisCompleted;
    public event Action<OptimizationSuggestion>? OptimizationSuggested;

    public bool IsConfigured => !string.IsNullOrEmpty(_config.ApiKey);
    public GameLogMonitorService LogMonitor => _logMonitorService;
    public LauncherDataService DataService => _launcherDataService;

    public AIService() : this(AppDomain.CurrentDomain.BaseDirectory)
    {
    }

    private readonly string _conversationFilePath;
    private readonly int _maxPersistedMessages = 50;

    public AIService(string gameDir)
    {
        _gameDir = gameDir;
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        _config = LoadConfig();
        _conversationHistory = LoadConversationHistory();
        _conversationFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ai_conversation.json");
        _hardwareService = new HardwareService();
        _launcherDataService = new LauncherDataService(gameDir);
        _logMonitorService = new GameLogMonitorService();
        _diagnosisService = new ProblemDiagnosisService(gameDir);
        _commandParser = new AICommandParser();
        _resourceService = new AIResourceService(gameDir);
        _proactiveAlertService = new AIProactiveAlertService(gameDir);
        
        SetupEventHandlers();
    }

    private void SetupEventHandlers()
    {
        _logMonitorService.ErrorDetected += OnGameErrorDetected;
        _logMonitorService.WarningDetected += OnGameWarningDetected;
        _diagnosisService.DiagnosisCompleted += OnDiagnosisCompleted;
    }

    private void OnGameErrorDetected(GameErrorEvent errorEvent)
    {
        ErrorDetected?.Invoke(errorEvent);
        App.LogError($"[AI助手] 检测到游戏错误: {errorEvent.ErrorType}");
    }

    private void OnGameWarningDetected(GameWarningEvent warningEvent)
    {
        App.LogInfo($"[AI助手] 检测到游戏警告: {warningEvent.WarningType}");
    }

    private void OnDiagnosisCompleted(DiagnosisResult result)
    {
        DiagnosisCompleted?.Invoke(result);
    }

    private AIConfig LoadConfig()
    {
        var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ai_config.json");
        if (File.Exists(configPath))
        {
            try
            {
                var json = File.ReadAllText(configPath);
                var config = JsonConvert.DeserializeObject<AIConfig>(json) ?? new AIConfig();
                if (!string.IsNullOrEmpty(config.ApiKey))
                {
                    config.ApiKey = DataProtectionHelper.Unprotect(config.ApiKey);
                }
                return config;
            }
            catch
            {
                return new AIConfig();
            }
        }
        return new AIConfig();
    }

    public void SaveConfig(AIConfig config)
    {
        var configToSave = new AIConfig
        {
            ApiKey = !string.IsNullOrEmpty(config.ApiKey) ? DataProtectionHelper.Protect(config.ApiKey) : "",
            ApiEndpoint = config.ApiEndpoint,
            Model = config.Model,
            MaxTokens = config.MaxTokens,
            Temperature = config.Temperature,
            MaxHistoryMessages = config.MaxHistoryMessages,
            SendHardwareInfo = config.SendHardwareInfo,
            EnableAutoDiagnosis = config.EnableAutoDiagnosis,
            EnableProactiveSuggestions = config.EnableProactiveSuggestions
        };

        var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ai_config.json");
        var json = JsonConvert.SerializeObject(configToSave, Formatting.Indented);
        File.WriteAllText(configPath, json);
        
        _config.ApiKey = config.ApiKey;
        _config.ApiEndpoint = config.ApiEndpoint;
        _config.Model = config.Model;
        _config.MaxTokens = config.MaxTokens;
        _config.Temperature = config.Temperature;
        _config.MaxHistoryMessages = config.MaxHistoryMessages;
        _config.SendHardwareInfo = config.SendHardwareInfo;
        _config.EnableAutoDiagnosis = config.EnableAutoDiagnosis;
        _config.EnableProactiveSuggestions = config.EnableProactiveSuggestions;
    }

    public AIConfig GetConfig() => new()
    {
        ApiKey = _config.ApiKey,
        ApiEndpoint = _config.ApiEndpoint,
        Model = _config.Model,
        MaxTokens = _config.MaxTokens,
        Temperature = _config.Temperature,
        MaxHistoryMessages = _config.MaxHistoryMessages,
        SendHardwareInfo = _config.SendHardwareInfo,
        EnableAutoDiagnosis = _config.EnableAutoDiagnosis,
        EnableProactiveSuggestions = _config.EnableProactiveSuggestions
    };

    public async Task<string> SendMessageAsync(string userMessage, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return "[提示] 请先配置 API Key。点击右上角「设置」按钮，输入您的 API Key。";
        }

        _conversationHistory.Add(new ChatMessage
        {
            Role = "user",
            Content = userMessage,
            Timestamp = DateTime.Now
        });

        TrimHistory();

        try
        {
            var systemPrompt = BuildEnhancedSystemPrompt();
            var messages = new List<object>
            {
                new { role = "system", content = systemPrompt }
            };

            foreach (var msg in _conversationHistory)
            {
                messages.Add(new { role = msg.Role, content = msg.Content });
            }

            var requestBody = new
            {
                model = _config.Model,
                messages = messages,
                max_tokens = _config.MaxTokens,
                temperature = _config.Temperature
            };

            var json = JsonConvert.SerializeObject(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_config.ApiKey}");

            var response = await _httpClient.PostAsync($"{_config.ApiEndpoint}/chat/completions", content, cancellationToken);
            var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorObj = JsonConvert.DeserializeObject<dynamic>(responseJson);
                var errorMessage = errorObj?.error?.message?.ToString() ?? "未知错误";
                _conversationHistory.RemoveAt(_conversationHistory.Count - 1);
                return $"[API错误] {errorMessage}";
            }

            var completion = JsonConvert.DeserializeObject<dynamic>(responseJson);
            var assistantMessage = completion?.choices?[0]?.message?.content?.ToString() ?? "无响应";

            _conversationHistory.Add(new ChatMessage
            {
                Role = "assistant",
                Content = assistantMessage,
                Timestamp = DateTime.Now
            });

            SaveConversationHistory();

            return assistantMessage;
        }
        catch (HttpRequestException ex)
        {
            _conversationHistory.RemoveAt(_conversationHistory.Count - 1);
            return $"[网络错误] {ex.Message}";
        }
        catch (TaskCanceledException)
        {
            _conversationHistory.RemoveAt(_conversationHistory.Count - 1);
            return "[超时] 请求超时，请重试。";
        }
        catch (Exception ex)
        {
            _conversationHistory.RemoveAt(_conversationHistory.Count - 1);
            return $"[错误] {ex.Message}";
        }
    }

    private string BuildEnhancedSystemPrompt()
    {
        var hardware = _hardwareService.GetHardwareInfo();
        var versions = _launcherDataService.GetInstalledVersions();
        var saves = _launcherDataService.GetAllSaves();
        
        var sb = new StringBuilder();
        
        sb.AppendLine(@"你是《我的世界》启动器的专属智能助手，名叫「小矿工」。你的职责是辅助玩家管理启动器、存档、模组，并实时监控、排查、修复游戏启动与运行中的问题。

【交互风格】
- 语气友好、充满游戏感，适度使用MC梗（如「别让苦力怕炸了你的存档！」）
- 回答必须具体、操作导向，拒绝笼统描述
- 若需执行修复，先说明将做什么，再执行
- 遇到未知错误时，主动询问是否要打包日志发送给开发者

【资源搜索与安装】
当用户请求搜索或安装资源时，你可以输出结构化的 JSON 指令，格式如下：
{
  ""action"": ""search_resource"",
  ""parameters"": {
    ""resource_type"": ""mod|shader|resourcepack|map"",
    ""query"": ""搜索关键词"",
    ""game_version"": ""1.19.2""
  }
}

常见操作指令：
- search_resource: 搜索资源（模组、光影、资源包、地图）
- install_resource: 安装资源
- backup_saves: 备份存档
- optimize_settings: 优化游戏设置

【主动提醒】
你可以在以下情况主动提醒用户：
- 兼容性预警：安装模组时，主动提示潜在的兼容性问题
- 性能提示：检测到硬件配置较低时，建议优化设置
- 备份提醒：检测到用户长时间未备份存档时，提醒备份

【安全边界】
- 绝不擅自删除玩家存档或模组，任何删除操作必须二次确认
- 修改Java启动参数前，告知风险并备份原配置
- 若检测到作弊模组，仅提醒「可能违反服务器规则」，不主动举报");

        if (_config.SendHardwareInfo)
        {
            sb.AppendLine(@$"
【当前用户硬件信息】
- CPU: {hardware.CpuName} ({hardware.CpuCores} 核心)
- 内存: {hardware.TotalMemoryGB:F1} GB
- GPU: {hardware.GpuName}
- GPU 显存: {hardware.GpuMemory}
- 操作系统: {hardware.OsVersion}");
        }

        if (versions.Count > 0)
        {
            sb.AppendLine(@$"
【已安装的游戏版本】({versions.Count} 个)");
            foreach (var v in versions.Take(5))
            {
                sb.AppendLine($"- {v.Id} ({v.ModLoader}, {v.ModCount} 个模组)");
            }
            if (versions.Count > 5)
            {
                sb.AppendLine($"- ... 还有 {versions.Count - 5} 个版本");
            }
        }

        if (saves.Count > 0)
        {
            sb.AppendLine(@$"
【最近游玩的存档】({saves.Count} 个)");
            foreach (var s in saves.Take(5))
            {
                sb.AppendLine($"- {s.DisplayName} ({s.Version}, {s.DisplayLastPlayed})");
            }
        }

        sb.AppendLine(@$"
【问题排查流程】
当玩家遇到问题时，请遵循以下流程：
1. 诊断 -> 提取错误关键词（如 Exit Code 1、Missing Mod Dependency、Java Heap Space）
2. 定位 -> 指出具体原因（例如：「模组『机械动力』需要Flywheel渲染库，但未安装或版本过低。」）
3. 给出方案 -> 提供自动修复或手动引导选项
4. 验证 -> 修复后建议玩家重试启动，并持续观察是否出现新问题

请用简洁、友好的中文回答问题。");

        return sb.ToString();
    }

    private void TrimHistory()
    {
        while (_conversationHistory.Count > _config.MaxHistoryMessages)
        {
            _conversationHistory.RemoveAt(0);
        }
    }

    public void ClearConversation()
    {
        _conversationHistory.Clear();
        SaveConversationHistory();
        ConversationCleared?.Invoke();
    }

    public List<ChatMessage> GetConversationHistory() => _conversationHistory.ToList();

    private List<ChatMessage> LoadConversationHistory()
    {
        var filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ai_conversation.json");
        try
        {
            if (File.Exists(filePath))
            {
                var json = File.ReadAllText(filePath);
                var messages = JsonConvert.DeserializeObject<List<ChatMessage>>(json);
                if (messages != null)
                {
                    return messages.TakeLast(_maxPersistedMessages).ToList();
                }
            }
        }
        catch
        {
        }
        return new List<ChatMessage>();
    }

    private void SaveConversationHistory()
    {
        try
        {
            var messagesToSave = _conversationHistory.TakeLast(_maxPersistedMessages).ToList();
            var json = JsonConvert.SerializeObject(messagesToSave, Formatting.Indented);
            File.WriteAllText(_conversationFilePath, json);
        }
        catch
        {
        }
    }

    #region 游戏监控

    public void StartGameMonitoring(System.Diagnostics.Process gameProcess, string version)
    {
        _logMonitorService.StartMonitoring(gameProcess, version);
        App.LogInfo($"[AI助手] 开始监控游戏: {version}");
    }

    public void StopGameMonitoring()
    {
        _logMonitorService.StopMonitoring();
        App.LogInfo("[AI助手] 停止监控游戏");
    }

    #endregion

    #region 问题诊断与修复

    public async Task<DiagnosisResult> DiagnoseErrorAsync(GameErrorEvent errorEvent)
    {
        return await _diagnosisService.DiagnoseProblemAsync(errorEvent);
    }

    public async Task<FixResult> ExecuteFixAsync(FixSolution solution)
    {
        return await _diagnosisService.ExecuteFixAsync(solution);
    }

    public async Task<string> AnalyzeErrorLogAsync(string logContent)
    {
        if (!IsConfigured)
        {
            return "[提示] 请先配置 API Key 才能使用日志分析功能。";
        }

        var prompt = $@"请分析以下 Minecraft 游戏日志，找出错误原因并给出解决方案。

【日志内容】
{logContent}

请按以下格式回答：
1. [错误诊断] 简要说明发生了什么错误
2. [根本原因] 解释为什么会出现这个错误
3. [解决方案] 给出具体的修复步骤
4. [注意事项] 如果有需要特别注意的地方请说明";

        return await SendMessageAsync(prompt);
    }

    #endregion

    #region 数据访问

    public List<SaveInfo> GetAllSaves() => _launcherDataService.GetAllSaves();

    public List<ModInfo> GetModsForVersion(string version) => _launcherDataService.GetModsForVersion(version);

    public List<GameVersionInfo> GetInstalledVersions() => _launcherDataService.GetInstalledVersions();

    public List<ModConflictInfo> DetectModConflicts(string version) => _launcherDataService.DetectModConflicts(version);

    public GameCrashReport? GetLatestCrashReport(string version) => _launcherDataService.AnalyzeGameCrash(version);

    public LauncherLogInfo GetLauncherLog() => _launcherDataService.GetLatestLauncherLog();

    #endregion

    #region 优化建议

    public async Task<GameSettingsRecommendation> GetGameSettingsRecommendationAsync(string preference = "balanced")
    {
        var hardware = _hardwareService.GetHardwareInfo();
        var recommendation = new GameSettingsRecommendation();

        var totalMemoryMB = (long)(hardware.TotalMemoryGB * 1024);
        
        recommendation.RecommendedMemoryMB = totalMemoryMB switch
        {
            < 4096 => 2048,
            < 8192 => 4096,
            < 16384 => 6144,
            < 32768 => 8192,
            _ => 12288
        };

        recommendation.RenderDistance = (hardware.TotalMemoryGB, preference) switch
        {
            (< 8, _) => 8,
            (< 16, "performance") => 10,
            (< 16, _) => 12,
            (_, "performance") => 14,
            _ => 16
        };

        recommendation.GraphicsMode = preference switch
        {
            "performance" => "fast",
            "quality" => "fancy",
            _ => "fast"
        };

        recommendation.JvmArgs = GenerateJvmArgs(recommendation.RecommendedMemoryMB, hardware.CpuCores);
        
        recommendation.Explanation = $"[推荐] 基于您的硬件配置（{hardware.CpuName}，{hardware.TotalMemoryGB:F1}GB 内存，{hardware.GpuName}），" +
            $"推荐分配 {recommendation.RecommendedMemoryMB / 1024.0:F1}GB 内存给游戏，" +
            $"渲染距离设置为 {recommendation.RenderDistance} 区块。";

        return recommendation;
    }

    public async Task<List<OptimizationSuggestion>> GetOptimizationSuggestionsAsync()
    {
        return await _diagnosisService.GetOptimizationSuggestionsAsync();
    }

    private string GenerateJvmArgs(int memoryMB, int cpuCores)
    {
        var args = new List<string>
        {
            $"-Xmx{memoryMB}M",
            $"-Xms{memoryMB / 2}M",
            "-XX:+UseG1GC",
            "-XX:+ParallelRefProcEnabled",
            "-XX:MaxGCPauseMillis=200",
            "-XX:+UnlockExperimentalVMOptions",
            "-XX:+DisableExplicitGC",
            "-XX:+AlwaysPreTouch",
            "-XX:G1NewSizePercent=30",
            "-XX:G1MaxNewSizePercent=40",
            "-XX:G1HeapRegionSize=8M",
            "-XX:G1ReservePercent=20",
            "-XX:G1HeapWastePercent=5",
            "-XX:G1MixedGCCountTarget=4",
            "-XX:G1MixedGCLiveThresholdPercent=90",
            "-XX:G1RSetUpdatingPauseTimePercent=5",
            "-XX:SurvivorRatio=32",
            "-XX:+PerfDisableSharedMem",
            "-XX:MaxTenuringThreshold=1",
            "-Dusing.aikars.flag=https://mcflags.emc.gs",
            "-Daikars.new.flags=true"
        };

        if (cpuCores >= 4)
        {
            args.Add($"-XX:ConcGCThreads={Math.Max(1, cpuCores / 4)}");
            args.Add($"-XX:ParallelGCThreads={Math.Max(2, cpuCores / 2)}");
        }

        return string.Join(" ", args);
    }

    #endregion

    #region 快捷功能

    public async Task<string> GetQuickDiagnosisAsync(string version)
    {
        var sb = new StringBuilder();
        sb.AppendLine("[诊断] 正在进行快速诊断...\n");

        var conflicts = _launcherDataService.DetectModConflicts(version);
        if (conflicts.Count > 0)
        {
            sb.AppendLine("[警告] 检测到模组问题：");
            foreach (var conflict in conflicts)
            {
                sb.AppendLine($"  • {conflict.ConflictType}: {conflict.Description}");
                sb.AppendLine($"    解决方案: {conflict.Solution}");
            }
        }
        else
        {
            sb.AppendLine("[正常] 未检测到模组冲突");
        }

        var crashReport = _launcherDataService.AnalyzeGameCrash(version);
        if (crashReport != null)
        {
            sb.AppendLine($"\n💥 检测到崩溃报告 ({crashReport.CrashTime:yyyy-MM-dd HH:mm})：");
            sb.AppendLine($"  类型: {crashReport.ErrorType}");
            sb.AppendLine($"  建议: {crashReport.SuggestedFix}");
        }

        var logInfo = _launcherDataService.GetLatestLauncherLog();
        if (logInfo.ErrorCount > 0)
        {
            sb.AppendLine($"\n[错误] 启动器日志中有 {logInfo.ErrorCount} 个错误");
        }

        return sb.ToString();
    }

    public async Task<string> GetSaveAnalysisAsync(string saveName)
    {
        var saves = _launcherDataService.GetAllSaves();
        var save = saves.FirstOrDefault(s => s.Name == saveName || s.DisplayName == saveName);
        
        if (save == null)
        {
            return $"[错误] 未找到存档: {saveName}";
        }

        var sb = new StringBuilder();
        sb.AppendLine($"[存档分析] {save.DisplayName}\n");
        sb.AppendLine($"[路径] {save.Path}");
        sb.AppendLine($"[版本] {save.Version}");
        sb.AppendLine($"[大小] {save.SizeDisplay}");
        sb.AppendLine($"[最后游玩] {save.DisplayLastPlayed}");
        sb.AppendLine($"[游戏模式] {save.GameMode}");
        sb.AppendLine($"[难度] {save.Difficulty}");
        sb.AppendLine($"[世界类型] {save.WorldType}");
        
        if (!string.IsNullOrEmpty(save.Seed))
        {
            sb.AppendLine($"[种子] {save.Seed}");
        }
        
        if (save.HasCheats)
        {
            sb.AppendLine($"[作弊] 已开启");
        }
        
        if (save.DatapackCount > 0)
        {
            sb.AppendLine($"[数据包] {save.DatapackCount} 个");
        }

        if (save.SizeBytes > 500 * 1024 * 1024)
        {
            sb.AppendLine($"\n[警告] 存档体积较大，建议备份并使用 MCEdit 修剪未加载区块。");
        }

        return sb.ToString();
    }

    public async Task<string> GetModAnalysisAsync(string version)
    {
        var mods = _launcherDataService.GetModsForVersion(version);
        var conflicts = _launcherDataService.DetectModConflicts(version);

        var sb = new StringBuilder();
        sb.AppendLine($"[模组分析] {version}\n");
        sb.AppendLine($"总数: {mods.Count} 个模组\n");

        var enabledMods = mods.Where(m => m.Enabled).ToList();
        var disabledMods = mods.Where(m => !m.Enabled).ToList();

        sb.AppendLine($"[已启用] {enabledMods.Count} 个");
        sb.AppendLine($"[已禁用] {disabledMods.Count} 个\n");

        var modLoaders = mods.Select(m => m.ModLoader).Distinct().ToList();
        if (modLoaders.Count > 0)
        {
            sb.AppendLine($"[加载器] {string.Join(", ", modLoaders)}\n");
        }

        if (conflicts.Count > 0)
        {
            sb.AppendLine("[警告] 检测到问题：");
            foreach (var conflict in conflicts)
            {
                sb.AppendLine($"  • [{conflict.Severity}] {conflict.Description}");
            }
        }
        else
        {
            sb.AppendLine("[正常] 未检测到模组冲突");
        }

        return sb.ToString();
    }

    #endregion

    #region 资源搜索与安装

    public async Task<string> SearchAndInstallResourceAsync(string userMessage)
    {
        try
        {
            var (resourceType, query, gameVersion) = _commandParser.ParseResourceRequest(userMessage);
            
            if (string.IsNullOrEmpty(query))
            {
                return "[提示] 请告诉我你想搜索什么资源。例如：「找一个 1.19 可用的光影」";
            }

            var results = await _resourceService.SearchResourcesAsync(resourceType, query, gameVersion, 5);
            
            if (results.Count == 0)
            {
                return $"[结果] 未找到匹配的{GetResourceTypeName(resourceType)}。";
            }

            var response = $"[搜索] 找到 {results.Count} 个匹配的{GetResourceTypeName(resourceType)}：\n\n";
            
            for (int i = 0; i < results.Count; i++)
            {
                var r = results[i];
                response += $"{i + 1}. **{r.Name}**\n";
                response += $"   [描述] {r.Description}\n";
                response += $"   [作者] {r.Author}\n";
                response += $"   [下载量] {r.Downloads:N0}\n";
                if (!string.IsNullOrEmpty(r.GameVersion))
                {
                    response += $"   [版本] {r.GameVersion}\n";
                }
                response += "\n";
            }

            response += "[提示] 请回复编号（1-5）来安装对应的资源，或告诉我其他需求。";
            
            return response;
        }
        catch (Exception ex)
        {
            App.LogError("搜索资源失败", ex);
            return $"[错误] 搜索失败: {ex.Message}";
        }
    }

    private string GetResourceTypeName(string resourceType)
    {
        return resourceType.ToLower() switch
        {
            "mod" => "模组",
            "shader" => "光影",
            "resourcepack" => "资源包",
            "map" => "地图",
            _ => "资源"
        };
    }

    public async Task<string> InstallResourceByIndexAsync(int index, string resourceType, string query, string? gameVersion)
    {
        try
        {
            var results = await _resourceService.SearchResourcesAsync(resourceType, query, gameVersion, 5);
            
            if (index < 1 || index > results.Count)
            {
                return "[错误] 无效的编号，请重新选择。";
            }

            var resource = results[index - 1];
            var success = await _resourceService.InstallResourceAsync(resource, gameVersion);
            
            if (success)
            {
                return $"[成功] 已安装「{resource.Name}」！\n\n资源已下载并放置到正确的目录，重启游戏即可生效。";
            }
            else
            {
                return $"[失败] 安装失败，请稍后重试。";
            }
        }
        catch (Exception ex)
        {
            App.LogError($"安装资源失败: {index}", ex);
            return $"[错误] 安装失败: {ex.Message}";
        }
    }

    #endregion

    #region 主动提醒

    public void StartProactiveAlerts()
    {
        _proactiveAlertService.AlertTriggered += OnProactiveAlertTriggered;
        _proactiveAlertService.StartMonitoring();
    }

    public void StopProactiveAlerts()
    {
        _proactiveAlertService.AlertTriggered -= OnProactiveAlertTriggered;
        _proactiveAlertService.StopMonitoring();
    }

    private void OnProactiveAlertTriggered(ProactiveAlert alert)
    {
        ProactiveAlertTriggered?.Invoke(alert);
    }

    public event Action<ProactiveAlert>? ProactiveAlertTriggered;

    #endregion

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _httpClient.Dispose();
            _logMonitorService.Dispose();
            _resourceService.Dispose();
            _proactiveAlertService.Dispose();
        }
    }
}
