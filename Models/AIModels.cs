namespace MinecraftLauncher.Models;

public class HardwareInfo
{
    public string CpuName { get; set; } = "Unknown";
    public int CpuCores { get; set; } = 4;
    public double TotalMemoryGB { get; set; } = 8;
    public string GpuName { get; set; } = "Unknown";
    public string GpuMemory { get; set; } = "Unknown";
    public string OsVersion { get; set; } = "Unknown";
}

public class GameSettingsRecommendation
{
    public int RecommendedMemoryMB { get; set; } = 4096;
    public int RenderDistance { get; set; } = 12;
    public string GraphicsMode { get; set; } = "fast";
    public string JvmArgs { get; set; } = string.Empty;
    public string Explanation { get; set; } = string.Empty;
}

public class ChatMessage
{
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public string DisplayTime => Timestamp.ToString("HH:mm");
}

public class AIConfig
{
    public string ApiKey { get; set; } = string.Empty;
    public string ApiEndpoint { get; set; } = "https://api.openai.com/v1";
    public string Model { get; set; } = "gpt-4o-mini";
    public int MaxTokens { get; set; } = 2048;
    public double Temperature { get; set; } = 0.7;
    public int MaxHistoryMessages { get; set; } = 20;
    public bool SendHardwareInfo { get; set; } = true;
    public bool EnableAutoDiagnosis { get; set; } = true;
    public bool EnableProactiveSuggestions { get; set; } = true;
}

public class AIAssistantState
{
    public bool IsMonitoring { get; set; }
    public string? CurrentGameVersion { get; set; }
    public DateTime? GameStartTime { get; set; }
    public List<GameErrorEventInfo> RecentErrors { get; set; } = new();
    public List<OptimizationSuggestionInfo> PendingSuggestions { get; set; } = new();
}

public class QuickAction
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public bool RequiresInput { get; set; }
    public string? InputPlaceholder { get; set; }
}

public class DiagnosticReport
{
    public DateTime GeneratedAt { get; set; }
    public string Version { get; set; } = string.Empty;
    public List<ModConflictInfoSimple> ModConflicts { get; set; } = new();
    public GameCrashReportInfo? LatestCrash { get; set; }
    public LauncherLogInfoSimple? LogInfo { get; set; }
    public List<OptimizationSuggestionInfo> Suggestions { get; set; } = new();
    public string OverallStatus { get; set; } = "正常";
    public int HealthScore { get; set; } = 100;
}

public class GameErrorEventInfo
{
    public DateTime Timestamp { get; set; }
    public string ErrorType { get; set; } = "";
    public string Severity { get; set; } = "中";
    public string Message { get; set; } = "";
    public string Version { get; set; } = "";
}

public class OptimizationSuggestionInfo
{
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Priority { get; set; } = "Medium";
    public string Category { get; set; } = "";
    public List<string> ManualSteps { get; set; } = new();
}

public class ModConflictInfoSimple
{
    public string ConflictType { get; set; } = "";
    public string Description { get; set; } = "";
    public string Severity { get; set; } = "";
    public string Solution { get; set; } = "";
}

public class GameCrashReportInfo
{
    public DateTime CrashTime { get; set; }
    public string Description { get; set; } = "";
    public string ExceptionType { get; set; } = "";
    public string SuggestedFix { get; set; } = "";
}

public class LauncherLogInfoSimple
{
    public int ErrorCount { get; set; }
    public int WarningCount { get; set; }
    public DateTime LogTime { get; set; }
}
