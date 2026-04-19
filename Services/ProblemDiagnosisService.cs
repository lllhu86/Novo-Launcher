using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using MinecraftLauncher.Models;

namespace MinecraftLauncher.Services;

public class ProblemDiagnosisService
{
    private readonly LauncherDataService _dataService;
    private readonly HardwareService _hardwareService;
    
    public event Action<DiagnosisResult>? DiagnosisCompleted;
    public event Action<FixResult>? FixCompleted;
    public event Action<string>? StatusChanged;
    
    public ProblemDiagnosisService(string gameDir)
    {
        _dataService = new LauncherDataService(gameDir);
        _hardwareService = new HardwareService();
    }
    
    #region 问题诊断
    
    public async Task<DiagnosisResult> DiagnoseProblemAsync(GameErrorEvent errorEvent)
    {
        var result = new DiagnosisResult
        {
            Timestamp = DateTime.Now,
            ErrorEvent = errorEvent,
            Status = DiagnosisStatus.Analyzing
        };
        
        StatusChanged?.Invoke($"正在诊断问题: {errorEvent.ErrorType}");
        
        await Task.Run(() =>
        {
            result.DiagnosticSteps.Add($"[检测] 错误类型: {errorEvent.ErrorType}");
            result.DiagnosticSteps.Add($"[信息] 错误严重程度: {errorEvent.Severity}");
            
            switch (errorEvent.ErrorType)
            {
                case "内存不足":
                    DiagnoseMemoryIssue(result);
                    break;
                case "缺失模组":
                case "模组依赖缺失":
                    DiagnoseMissingMod(result, errorEvent);
                    break;
                case "重复模组":
                    DiagnoseDuplicateMod(result, errorEvent);
                    break;
                case "模组加载失败":
                    DiagnoseModLoadFailure(result, errorEvent);
                    break;
                case "Java版本不兼容":
                    DiagnoseJavaVersion(result, errorEvent);
                    break;
                case "游戏异常退出":
                    DiagnoseCrash(result, errorEvent);
                    break;
                case "光影错误":
                case "OpenGL错误":
                    DiagnoseGraphicsIssue(result, errorEvent);
                    break;
                default:
                    DiagnoseGenericIssue(result, errorEvent);
                    break;
            }
        });
        
        result.Status = DiagnosisStatus.Completed;
        DiagnosisCompleted?.Invoke(result);
        
        return result;
    }
    
    private void DiagnoseMemoryIssue(DiagnosisResult result)
    {
        var hardware = _hardwareService.GetHardwareInfo();
        
        result.DiagnosticSteps.Add($"[系统] 总内存: {hardware.TotalMemoryGB:F1} GB");
        result.DiagnosticSteps.Add($"[系统] CPU核心数: {hardware.CpuCores}");
        
        var recommendedMemory = hardware.TotalMemoryGB switch
        {
            < 8 => 2048,
            < 16 => 4096,
            < 32 => 6144,
            _ => 8192
        };
        
        result.RootCause = "游戏分配的内存不足，无法满足当前模组配置的需求。";
        result.Solutions.Add(new FixSolution
        {
            Title = "自动调整内存分配",
            Description = $"将游戏内存调整为推荐值 {recommendedMemory / 1024.0:F1} GB",
            FixType = FixType.Automatic,
            Action = () => AdjustMemoryAllocation(recommendedMemory)
        });
        
        result.Solutions.Add(new FixSolution
        {
            Title = "减少模组数量",
            Description = "禁用部分不必要的模组以减少内存占用",
            FixType = FixType.Manual,
            ManualSteps = new List<string>
            {
                "打开游戏版本目录",
                "进入 mods 文件夹",
                "将不需要的模组移动到 disabled_mods 文件夹",
                "重新启动游戏"
            }
        });
        
        result.Solutions.Add(new FixSolution
        {
            Title = "关闭其他程序",
            Description = "关闭浏览器、视频软件等占用内存的程序",
            FixType = FixType.Manual,
            ManualSteps = new List<string>
            {
                "按 Ctrl+Shift+Esc 打开任务管理器",
                "查看内存使用情况",
                "关闭占用内存较高的程序",
                "重新启动游戏"
            }
        });
    }
    
    private void DiagnoseMissingMod(DiagnosisResult result, GameErrorEvent errorEvent)
    {
        var version = errorEvent.Version;
        if (string.IsNullOrEmpty(version))
        {
            result.RootCause = "无法确定游戏版本，无法诊断缺失的模组。";
            return;
        }
        
        var mods = _dataService.GetModsForVersion(version);
        var conflicts = _dataService.DetectModConflicts(version);
        
        result.DiagnosticSteps.Add($"[模组] 已安装模组数量: {mods.Count}");
        
        var missingDeps = conflicts.Where(c => c.ConflictType == "缺失依赖").ToList();
        if (missingDeps.Count > 0)
        {
            result.DiagnosticSteps.Add($"[警告] 检测到 {missingDeps.Count} 个缺失的依赖:");
            foreach (var dep in missingDeps)
            {
                result.DiagnosticSteps.Add($"   - {dep.Description}");
            }
            
            result.RootCause = "缺少必要的前置模组，导致依赖它的模组无法正常工作。";
            
            result.Solutions.Add(new FixSolution
            {
                Title = "查看缺失的依赖",
                Description = "显示需要安装的前置模组列表",
                FixType = FixType.Manual,
                ManualSteps = new List<string>
                {
                    "以下模组需要安装前置:",
                    string.Join("\n", missingDeps.Select(d => $"  - {d.Description}")),
                    "",
                    "请前往 Modrinth 或 CurseForge 搜索并安装这些模组"
                }
            });
        }
        
        if (!string.IsNullOrEmpty(errorEvent.RelatedMod))
        {
            result.DiagnosticSteps.Add($"[相关] 模组: {errorEvent.RelatedMod}");
        }
    }
    
    private void DiagnoseDuplicateMod(DiagnosisResult result, GameErrorEvent errorEvent)
    {
        var version = errorEvent.Version;
        if (string.IsNullOrEmpty(version))
        {
            result.RootCause = "无法确定游戏版本，无法诊断重复模组。";
            return;
        }
        
        var conflicts = _dataService.DetectModConflicts(version);
        var duplicates = conflicts.Where(c => c.ConflictType == "重复模组").ToList();
        
        if (duplicates.Count > 0)
        {
            result.DiagnosticSteps.Add($"[警告] 检测到 {duplicates.Count} 个重复的模组:");
            foreach (var dup in duplicates)
            {
                result.DiagnosticSteps.Add($"   - {dup.Description}");
                result.DiagnosticSteps.Add($"     受影响的文件: {string.Join(", ", dup.AffectedMods)}");
            }
            
            result.RootCause = "存在重复安装的模组，会导致游戏冲突或崩溃。";
            
            result.Solutions.Add(new FixSolution
            {
                Title = "删除重复模组",
                Description = "保留最新版本，删除其他重复文件",
                FixType = FixType.Manual,
                ManualSteps = new List<string>
                {
                    "打开游戏版本的 mods 文件夹",
                    "找到重复的模组文件",
                    "删除旧版本，保留最新版本",
                    "重新启动游戏"
                }
            });
        }
    }
    
    private void DiagnoseModLoadFailure(DiagnosisResult result, GameErrorEvent errorEvent)
    {
        var version = errorEvent.Version;
        if (!string.IsNullOrEmpty(version))
        {
            var mods = _dataService.GetModsForVersion(version);
            var versionInfo = _dataService.GetInstalledVersions()
                .FirstOrDefault(v => v.Id == version);
            
            if (versionInfo != null)
            {
                result.DiagnosticSteps.Add($"[游戏] 版本: {version}");
                result.DiagnosticSteps.Add($"[游戏] 模组加载器: {versionInfo.ModLoader}");
                result.DiagnosticSteps.Add($"[模组] 已安装模组: {mods.Count} 个");
            }
        }
        
        result.RootCause = "模组加载失败，可能是版本不兼容或文件损坏。";
        
        result.Solutions.Add(new FixSolution
        {
            Title = "检查模组兼容性",
            Description = "确保模组版本与游戏版本和模组加载器兼容",
            FixType = FixType.Manual,
            ManualSteps = new List<string>
            {
                "检查模组是否支持当前游戏版本",
                "确认模组是否与当前加载器兼容 (Forge/Fabric/Quilt)",
                "尝试重新下载模组",
                "查看模组的更新日志"
            }
        });
    }
    
    private void DiagnoseJavaVersion(DiagnosisResult result, GameErrorEvent errorEvent)
    {
        var version = errorEvent.Version;
        int? requiredJava = null;
        
        if (!string.IsNullOrEmpty(version))
        {
            var versionInfo = _dataService.GetInstalledVersions()
                .FirstOrDefault(v => v.Id == version);
            requiredJava = versionInfo?.JavaVersion;
        }
        
        if (requiredJava.HasValue)
        {
            result.DiagnosticSteps.Add($"[Java] 需要的版本: {requiredJava.Value}");
        }
        
        var installedJava = FindInstalledJavaVersions();
        if (installedJava.Count > 0)
        {
            result.DiagnosticSteps.Add($"[Java] 已安装的版本: {string.Join(", ", installedJava)}");
        }
        
        result.RootCause = "Java 版本不兼容，游戏需要特定版本的 Java 运行时。";
        
        result.Solutions.Add(new FixSolution
        {
            Title = "安装正确的 Java 版本",
            Description = requiredJava.HasValue 
                ? $"安装 Java {requiredJava.Value} 或更高版本" 
                : "安装游戏所需的 Java 版本",
            FixType = FixType.Manual,
            ManualSteps = new List<string>
            {
                "前往 adoptium.net 或 oracle.com 下载 Java",
                requiredJava.HasValue ? $"下载 Java {requiredJava.Value} 或更高版本" : "下载游戏所需的 Java 版本",
                "安装完成后重启启动器",
                "在设置中配置 Java 路径"
            }
        });
    }
    
    private void DiagnoseCrash(DiagnosisResult result, GameErrorEvent errorEvent)
    {
        var version = errorEvent.Version;
        
        if (errorEvent.ExitCode > 0)
        {
            result.DiagnosticSteps.Add($"[退出] 退出码: {errorEvent.ExitCode}");
            
            var exitCodeMeaning = errorEvent.ExitCode switch
            {
                1 => "通用错误",
                -1 => "JVM 初始化失败",
                2 => "内存分配失败",
                3 => "找不到主类",
                _ => "未知错误"
            };
            result.DiagnosticSteps.Add($"[分析] 可能原因: {exitCodeMeaning}");
        }
        
        if (!string.IsNullOrEmpty(version))
        {
            var crashReport = _dataService.AnalyzeGameCrash(version);
            if (crashReport != null)
            {
                result.DiagnosticSteps.Add($"[崩溃] 报告: {Path.GetFileName(crashReport.FilePath)}");
                result.DiagnosticSteps.Add($"[崩溃] 时间: {crashReport.CrashTime:yyyy-MM-dd HH:mm:ss}");
                
                if (!string.IsNullOrEmpty(crashReport.Description))
                {
                    result.DiagnosticSteps.Add($"[描述] {crashReport.Description}");
                }
                
                if (!string.IsNullOrEmpty(crashReport.ExceptionType))
                {
                    result.DiagnosticSteps.Add($"[异常] 类型: {crashReport.ExceptionType}");
                }
                
                result.RootCause = crashReport.SuggestedFix;
            }
            else
            {
                result.RootCause = "游戏崩溃，未找到崩溃报告文件。";
            }
        }
        else
        {
            result.RootCause = "游戏崩溃，无法确定具体原因。";
        }
        
        result.Solutions.Add(new FixSolution
        {
            Title = "查看崩溃报告",
            Description = "打开崩溃报告文件夹查看详细信息",
            FixType = FixType.Manual,
            ManualSteps = new List<string>
            {
                "打开游戏版本目录",
                "进入 crash-reports 文件夹",
                "打开最新的崩溃报告文件",
                "将报告发送给模组作者或社区求助"
            }
        });
    }
    
    private void DiagnoseGraphicsIssue(DiagnosisResult result, GameErrorEvent errorEvent)
    {
        var hardware = _hardwareService.GetHardwareInfo();
        
        result.DiagnosticSteps.Add($"[显卡] {hardware.GpuName}");
        result.DiagnosticSteps.Add($"[显存] {hardware.GpuMemory}");
        
        result.RootCause = "图形相关问题，可能是显卡驱动过旧或光影/资源包不兼容。";
        
        result.Solutions.Add(new FixSolution
        {
            Title = "更新显卡驱动",
            Description = "下载并安装最新的显卡驱动程序",
            FixType = FixType.Manual,
            ManualSteps = new List<string>
            {
                "NVIDIA 显卡: 前往 nvidia.cn 下载驱动",
                "AMD 显卡: 前往 amd.com 下载驱动",
                "Intel 显卡: 前往 intel.cn 下载驱动",
                "安装后重启电脑"
            }
        });
        
        result.Solutions.Add(new FixSolution
        {
            Title = "禁用光影/资源包",
            Description = "暂时禁用光影和资源包以排除问题",
            FixType = FixType.Manual,
            ManualSteps = new List<string>
            {
                "打开游戏版本目录",
                "进入 shaderpacks 文件夹",
                "删除或移动光影文件",
                "重新启动游戏测试"
            }
        });
    }
    
    private void DiagnoseGenericIssue(DiagnosisResult result, GameErrorEvent errorEvent)
    {
        result.DiagnosticSteps.Add($"[原始错误] {errorEvent.RawLine}");
        
        if (!string.IsNullOrEmpty(errorEvent.Message))
        {
            result.DiagnosticSteps.Add($"[错误信息] {errorEvent.Message}");
        }
        
        result.RootCause = "检测到未知类型的错误，建议查看详细日志。";
        
        result.Solutions.Add(new FixSolution
        {
            Title = "查看游戏日志",
            Description = "打开日志文件查看详细错误信息",
            FixType = FixType.Manual,
            ManualSteps = new List<string>
            {
                "打开游戏版本目录",
                "进入 logs 文件夹",
                "打开 latest.log 文件",
                "搜索 ERROR 或 FATAL 关键字"
            }
        });
        
        result.Solutions.Add(new FixSolution
        {
            Title = "发送日志给开发者",
            Description = "打包日志文件发送给模组作者或社区",
            FixType = FixType.Manual,
            ManualSteps = new List<string>
            {
                "压缩 logs 文件夹和 crash-reports 文件夹",
                "前往相关模组的 Issue 页面",
                "创建新 Issue 并附上日志文件",
                "描述问题发生时的操作"
            }
        });
    }
    
    #endregion
    
    #region 自动修复
    
    public async Task<FixResult> ExecuteFixAsync(FixSolution solution)
    {
        var result = new FixResult
        {
            Solution = solution,
            Timestamp = DateTime.Now,
            Status = FixStatus.Executing
        };
        
        StatusChanged?.Invoke($"正在执行修复: {solution.Title}");
        
        try
        {
            if (solution.FixType == FixType.Automatic && solution.Action != null)
            {
                var success = await Task.Run(() => solution.Action());
                result.Success = success;
                result.Message = success 
                    ? "[成功] 修复成功！请重新启动游戏验证。" 
                    : "[失败] 自动修复失败，请尝试手动修复。";
                result.Status = success ? FixStatus.Success : FixStatus.Failed;
            }
            else
            {
                result.Success = false;
                result.Message = "此修复需要手动操作，请按照步骤执行。";
                result.Status = FixStatus.ManualRequired;
            }
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Message = $"修复过程中出错: {ex.Message}";
            result.Status = FixStatus.Failed;
            App.LogError($"执行修复失败: {ex.Message}", ex);
        }
        
        FixCompleted?.Invoke(result);
        return result;
    }
    
    private bool AdjustMemoryAllocation(int memoryMB)
    {
        try
        {
            var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "game_config.json");
            var config = new GameConfig
            {
                MemoryMB = memoryMB,
                LastModified = DateTime.Now
            };
            
            var json = System.Text.Json.JsonSerializer.Serialize(config, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            });
            
            File.WriteAllText(configPath, json);
            
            App.LogInfo($"已调整内存分配为 {memoryMB} MB");
            return true;
        }
        catch (Exception ex)
        {
            App.LogError($"调整内存分配失败: {ex.Message}", ex);
            return false;
        }
    }
    
    private List<int> FindInstalledJavaVersions()
    {
        var versions = new List<int>();
        
        try
        {
            var javaHomes = new List<string>();
            
            var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
            if (!string.IsNullOrEmpty(javaHome))
            {
                javaHomes.Add(javaHome);
            }
            
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var javaDir = Path.Combine(programFiles, "Java");
            if (Directory.Exists(javaDir))
            {
                javaHomes.AddRange(Directory.GetDirectories(javaDir));
            }
            
            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            var javaDirX86 = Path.Combine(programFilesX86, "Java");
            if (Directory.Exists(javaDirX86))
            {
                javaHomes.AddRange(Directory.GetDirectories(javaDirX86));
            }
            
            foreach (var home in javaHomes)
            {
                var releaseFile = Path.Combine(home, "release");
                if (File.Exists(releaseFile))
                {
                    var content = File.ReadAllText(releaseFile);
                    var match = Regex.Match(content, @"JAVA_VERSION=""(\d+)");
                    if (match.Success)
                    {
                        var version = int.Parse(match.Groups[1].Value);
                        if (!versions.Contains(version))
                        {
                            versions.Add(version);
                        }
                    }
                }
            }
        }
        catch { }
        
        return versions.OrderBy(v => v).ToList();
    }
    
    #endregion
    
    #region 主动优化建议
    
    public async Task<List<OptimizationSuggestion>> GetOptimizationSuggestionsAsync()
    {
        var suggestions = new List<OptimizationSuggestion>();
        
        await Task.Run(() =>
        {
            var hardware = _hardwareService.GetHardwareInfo();
            var versions = _dataService.GetInstalledVersions();
            var saves = _dataService.GetAllSaves();
            
            var recommendedMemory = hardware.TotalMemoryGB switch
            {
                < 8 => 2048,
                < 16 => 4096,
                < 32 => 6144,
                _ => 8192
            };
            
            suggestions.Add(new OptimizationSuggestion
            {
                Title = "内存分配优化",
                Description = $"根据您的系统内存 ({hardware.TotalMemoryGB:F1} GB)，建议分配 {recommendedMemory / 1024.0:F1} GB 给游戏",
                Priority = SuggestionPriority.High,
                Category = "性能",
                Action = () => AdjustMemoryAllocation(recommendedMemory)
            });
            
            var largeSaves = saves.Where(s => s.SizeBytes > 500 * 1024 * 1024).ToList();
            if (largeSaves.Count > 0)
            {
                suggestions.Add(new OptimizationSuggestion
                {
                    Title = "存档体积过大",
                    Description = $"检测到 {largeSaves.Count} 个存档超过 500MB，建议备份并清理",
                    Priority = SuggestionPriority.Medium,
                    Category = "存储",
                    ManualSteps = new List<string>
                    {
                        "使用 MCEdit 或类似工具修剪未加载的区块",
                        "删除不需要的实体和方块",
                        "定期备份存档到云存储"
                    }
                });
            }
            
            foreach (var version in versions)
            {
                var conflicts = _dataService.DetectModConflicts(version.Id);
                if (conflicts.Count > 0)
                {
                    suggestions.Add(new OptimizationSuggestion
                    {
                        Title = $"模组冲突警告 ({version.Id})",
                        Description = $"检测到 {conflicts.Count} 个模组问题，可能导致游戏不稳定",
                        Priority = SuggestionPriority.High,
                        Category = "模组",
                        ManualSteps = new List<string>
                        {
                            "打开 AI 助手查看详细信息",
                            "解决缺失依赖或重复模组问题",
                            "重新启动游戏验证"
                        }
                    });
                }
            }
            
            if (hardware.GpuName.Contains("Intel", StringComparison.OrdinalIgnoreCase))
            {
                suggestions.Add(new OptimizationSuggestion
                {
                    Title = "集成显卡优化建议",
                    Description = "检测到您使用的是 Intel 集成显卡，建议降低视频设置以获得更流畅的体验",
                    Priority = SuggestionPriority.Medium,
                    Category = "图形",
                    ManualSteps = new List<string>
                    {
                        "降低渲染距离至 8-10 区块",
                        "关闭光影和高清资源包",
                        "使用 Fast 模式而非 Fancy 模式",
                        "关闭 VSync 和光影"
                    }
                });
            }
        });
        
        return suggestions.OrderByDescending(s => s.Priority).ToList();
    }
    
    #endregion
}

#region 数据模型

public class DiagnosisResult
{
    public DateTime Timestamp { get; set; }
    public DiagnosisStatus Status { get; set; }
    public GameErrorEvent? ErrorEvent { get; set; }
    public List<string> DiagnosticSteps { get; set; } = new();
    public string RootCause { get; set; } = "";
    public List<FixSolution> Solutions { get; set; } = new();
}

public enum DiagnosisStatus
{
    Analyzing,
    Completed,
    Failed
}

public class FixSolution
{
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public FixType FixType { get; set; }
    public List<string> ManualSteps { get; set; } = new();
    public Func<bool>? Action { get; set; }
}

public enum FixType
{
    Automatic,
    Manual,
    SemiAutomatic
}

public class FixResult
{
    public DateTime Timestamp { get; set; }
    public FixSolution Solution { get; set; } = new();
    public FixStatus Status { get; set; }
    public bool Success { get; set; }
    public string Message { get; set; } = "";
}

public enum FixStatus
{
    Executing,
    Success,
    Failed,
    ManualRequired
}

public class OptimizationSuggestion
{
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public SuggestionPriority Priority { get; set; }
    public string Category { get; set; } = "";
    public List<string> ManualSteps { get; set; } = new();
    public Func<bool>? Action { get; set; }
}

public enum SuggestionPriority
{
    Low,
    Medium,
    High,
    Critical
}

public class GameConfig
{
    public int MemoryMB { get; set; } = 4096;
    public DateTime LastModified { get; set; }
}

#endregion
