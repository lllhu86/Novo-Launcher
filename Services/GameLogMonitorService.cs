using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace MinecraftLauncher.Services;

public class GameLogMonitorService : IDisposable
{
    private Process? _gameProcess;
    private string? _currentVersion;
    private string? _logFilePath;
    private FileSystemWatcher? _logWatcher;
    private long _lastLogPosition;
    private Timer? _logCheckTimer;
    private bool _isMonitoring;
    private bool _disposed;
    
    public event Action<LogEntry>? LogReceived;
    public event Action<GameErrorEvent>? ErrorDetected;
    public event Action<GameWarningEvent>? WarningDetected;
    public event Action<string>? GameStarted;
    public event Action<int>? GameExited;
    
    public bool IsMonitoring => _isMonitoring;
    public string? CurrentVersion => _currentVersion;
    
    private static readonly Dictionary<string, string> KnownErrorPatterns = new()
    {
        { @"Exit Code[:\s]+(-?\d+)", "游戏异常退出" },
        { @"java\.lang\.OutOfMemoryError", "内存不足" },
        { @"java\.lang\.NoSuchMethodError", "方法不存在（版本不兼容）" },
        { @"java\.lang\.NoClassDefFoundError", "类定义未找到（缺失依赖）" },
        { @"java\.io\.FileNotFoundException", "文件未找到" },
        { @"Missing Mods?", "缺失模组" },
        { @"Duplicate Mod", "重复模组" },
        { @"Failed to load mod", "模组加载失败" },
        { @"Mod dependency missing", "模组依赖缺失" },
        { @"Shader.*error", "光影错误" },
        { @"GL_[A-Z_]+", "OpenGL错误" },
        { @"java\.net\.SocketException", "网络连接错误" },
        { @"Connection refused", "连接被拒绝" },
        { @"Timed out", "连接超时" },
        { @"World corrupted", "存档损坏" },
        { @"Unable to locate signable asset", "资源文件缺失" },
        { @"Failed to download", "下载失败" },
        { @"UnsupportedClassVersionError", "Java版本不兼容" },
        { @"SecurityException", "安全异常" },
        { @"ConcurrentModificationException", "并发修改异常" },
        { @"NullPointerException", "空指针异常" },
        { @"IllegalArgumentException", "非法参数" },
        { @"IndexOutOfBoundsException", "索引越界" },
        { @"ArrayIndexOutOfBoundsException", "数组索引越界" },
        { @"ClassCastException", "类型转换异常" },
        { @"StackOverflowError", "栈溢出" }
    };
    
    private static readonly Dictionary<string, string> WarningPatterns = new()
    {
        { @"Using deprecated code", "使用了已弃用的代码" },
        { @"Performance warning", "性能警告" },
        { @"Memory usage high", "内存使用过高" },
        { @"Low disk space", "磁盘空间不足" },
        { @"Mod.*may cause issues", "模组可能引起问题" },
        { @"Incompatible mod", "不兼容的模组" },
        { @"Outdated mod", "过时的模组" },
        { @"Missing translation", "缺失翻译" }
    };
    
    public void StartMonitoring(Process gameProcess, string version)
    {
        if (_isMonitoring)
        {
            StopMonitoring();
        }
        
        _gameProcess = gameProcess;
        _currentVersion = version;
        _isMonitoring = true;
        
        var versionDir = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            ".minecraft", "versions", version
        );
        
        _logFilePath = Path.Combine(versionDir, "logs", "latest.log");
        
        _lastLogPosition = 0;
        if (File.Exists(_logFilePath))
        {
            _lastLogPosition = new FileInfo(_logFilePath).Length;
        }
        
        SetupLogWatcher(versionDir);
        
        _logCheckTimer = new Timer(CheckLogFile, null, TimeSpan.Zero, TimeSpan.FromSeconds(1));
        
        _gameProcess.EnableRaisingEvents = true;
        _gameProcess.Exited += OnGameExited;
        _gameProcess.OutputDataReceived += OnGameOutput;
        _gameProcess.ErrorDataReceived += OnGameError;
        
        GameStarted?.Invoke(version);
        
        App.LogInfo($"开始监控游戏日志: {_logFilePath}");
    }
    
    private void SetupLogWatcher(string versionDir)
    {
        var logsDir = Path.Combine(versionDir, "logs");
        
        if (!Directory.Exists(logsDir))
        {
            Directory.CreateDirectory(logsDir);
        }
        
        _logWatcher = new FileSystemWatcher(logsDir)
        {
            Filter = "latest.log",
            NotifyFilter = NotifyFilters.Size | NotifyFilters.LastWrite,
            EnableRaisingEvents = true
        };
        
        _logWatcher.Changed += OnLogFileChanged;
    }
    
    private void OnLogFileChanged(object sender, FileSystemEventArgs e)
    {
        if (!_isMonitoring || string.IsNullOrEmpty(_logFilePath))
            return;
        
        try
        {
            ReadNewLogEntries();
        }
        catch (Exception ex)
        {
            App.LogError($"读取日志文件失败: {ex.Message}", ex);
        }
    }
    
    private void CheckLogFile(object? state)
    {
        if (!_isMonitoring || string.IsNullOrEmpty(_logFilePath))
            return;
        
        try
        {
            if (File.Exists(_logFilePath))
            {
                ReadNewLogEntries();
            }
        }
        catch (Exception ex)
        {
            App.LogError($"检查日志文件失败: {ex.Message}", ex);
        }
    }
    
    private void ReadNewLogEntries()
    {
        if (string.IsNullOrEmpty(_logFilePath) || !File.Exists(_logFilePath))
            return;
        
        try
        {
            using var fs = new FileStream(_logFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            
            if (fs.Length <= _lastLogPosition)
                return;
            
            fs.Seek(_lastLogPosition, SeekOrigin.Begin);
            
            using var reader = new StreamReader(fs);
            var newContent = reader.ReadToEnd();
            _lastLogPosition = fs.Position;
            
            if (string.IsNullOrEmpty(newContent))
                return;
            
            var lines = newContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            
            foreach (var line in lines)
            {
                ProcessLogLine(line);
            }
        }
        catch (Exception ex)
        {
            App.LogError($"读取日志条目失败: {ex.Message}", ex);
        }
    }
    
    private void ProcessLogLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return;
        
        var logEntry = ParseLogEntry(line);
        LogReceived?.Invoke(logEntry);
        
        foreach (var pattern in KnownErrorPatterns)
        {
            if (Regex.IsMatch(line, pattern.Key, RegexOptions.IgnoreCase))
            {
                var errorEvent = new GameErrorEvent
                {
                    Timestamp = DateTime.Now,
                    RawLine = line,
                    ErrorType = pattern.Value,
                    Pattern = pattern.Key,
                    Version = _currentVersion ?? "",
                    Severity = DetermineSeverity(line, pattern.Value)
                };
                
                ExtractErrorDetails(line, errorEvent);
                
                ErrorDetected?.Invoke(errorEvent);
                
                App.LogError($"检测到游戏错误: {errorEvent.ErrorType} - {errorEvent.Message}");
                break;
            }
        }
        
        foreach (var pattern in WarningPatterns)
        {
            if (Regex.IsMatch(line, pattern.Key, RegexOptions.IgnoreCase))
            {
                var warningEvent = new GameWarningEvent
                {
                    Timestamp = DateTime.Now,
                    RawLine = line,
                    WarningType = pattern.Value,
                    Pattern = pattern.Key,
                    Version = _currentVersion ?? ""
                };
                
                WarningDetected?.Invoke(warningEvent);
                break;
            }
        }
    }
    
    private LogEntry ParseLogEntry(string line)
    {
        var entry = new LogEntry
        {
            RawContent = line,
            Timestamp = DateTime.Now
        };
        
        var timeMatch = Regex.Match(line, @"\[(\d{2}:\d{2}:\d{2})\]");
        if (timeMatch.Success)
        {
            entry.TimeString = timeMatch.Groups[1].Value;
        }
        
        var threadMatch = Regex.Match(line, @"\[([A-Za-z0-9_-]+(?:\s*\/\s*[A-Za-z0-9_-]+)?)\]");
        if (threadMatch.Success)
        {
            entry.Thread = threadMatch.Groups[1].Value;
        }
        
        var levelMatch = Regex.Match(line, @"\[(TRACE|DEBUG|INFO|WARN|ERROR|FATAL)\]", RegexOptions.IgnoreCase);
        if (levelMatch.Success)
        {
            entry.Level = levelMatch.Groups[1].Value.ToUpper();
        }
        
        if (line.Contains("[ERROR]") || line.Contains("[FATAL]"))
        {
            entry.Level = "ERROR";
        }
        else if (line.Contains("[WARN]"))
        {
            entry.Level = "WARN";
        }
        else if (line.Contains("[INFO]"))
        {
            entry.Level = "INFO";
        }
        
        return entry;
    }
    
    private string DetermineSeverity(string line, string errorType)
    {
        if (line.Contains("FATAL") || errorType.Contains("内存不足") || errorType.Contains("异常退出"))
        {
            return "严重";
        }
        
        if (errorType.Contains("缺失") || errorType.Contains("不兼容") || errorType.Contains("错误"))
        {
            return "高";
        }
        
        return "中";
    }
    
    private void ExtractErrorDetails(string line, GameErrorEvent errorEvent)
    {
        var exceptionMatch = Regex.Match(line, @"([a-zA-Z.]+(?:Exception|Error)):\s*(.+?)(?:$|at\s)");
        if (exceptionMatch.Success)
        {
            errorEvent.ExceptionType = exceptionMatch.Groups[1].Value;
            errorEvent.Message = exceptionMatch.Groups[2].Value.Trim();
        }
        
        var exitCodeMatch = Regex.Match(line, @"Exit Code[:\s]+(-?\d+)");
        if (exitCodeMatch.Success)
        {
            errorEvent.ExitCode = int.Parse(exitCodeMatch.Groups[1].Value);
            errorEvent.Message = $"游戏以退出码 {errorEvent.ExitCode} 退出";
        }
        
        var modMatch = Regex.Match(line, @"mod[s]?\s*[:\s]+['""]?([a-zA-Z0-9_-]+)['""]?", RegexOptions.IgnoreCase);
        if (modMatch.Success)
        {
            errorEvent.RelatedMod = modMatch.Groups[1].Value;
        }
    }
    
    private void OnGameOutput(object sender, DataReceivedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Data))
            return;
        
        ProcessLogLine($"[STDOUT] {e.Data}");
    }
    
    private void OnGameError(object sender, DataReceivedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Data))
            return;
        
        ProcessLogLine($"[STDERR] {e.Data}");
    }
    
    private void OnGameExited(object? sender, EventArgs e)
    {
        var exitCode = _gameProcess?.ExitCode ?? -1;
        
        App.LogInfo($"游戏进程退出，退出码: {exitCode}");
        
        GameExited?.Invoke(exitCode);
        
        StopMonitoring();
    }
    
    public void StopMonitoring()
    {
        if (!_isMonitoring)
            return;
        
        _isMonitoring = false;
        
        _logCheckTimer?.Dispose();
        _logCheckTimer = null;
        
        if (_logWatcher != null)
        {
            _logWatcher.EnableRaisingEvents = false;
            _logWatcher.Changed -= OnLogFileChanged;
            _logWatcher.Dispose();
            _logWatcher = null;
        }
        
        if (_gameProcess != null)
        {
            _gameProcess.Exited -= OnGameExited;
            _gameProcess.OutputDataReceived -= OnGameOutput;
            _gameProcess.ErrorDataReceived -= OnGameError;
        }
        
        _gameProcess = null;
        _currentVersion = null;
        _logFilePath = null;
        
        App.LogInfo("停止监控游戏日志");
    }
    
    public List<string> GetRecentLogs(int count = 100)
    {
        var logs = new List<string>();
        
        if (string.IsNullOrEmpty(_logFilePath) || !File.Exists(_logFilePath))
            return logs;
        
        try
        {
            var lines = File.ReadAllLines(_logFilePath);
            return lines.TakeLast(count).ToList();
        }
        catch
        {
            return logs;
        }
    }
    
    public void Dispose()
    {
        if (_disposed)
            return;
        
        _disposed = true;
        StopMonitoring();
        GC.SuppressFinalize(this);
    }
}

#region 数据模型

public class LogEntry
{
    public DateTime Timestamp { get; set; }
    public string TimeString { get; set; } = "";
    public string Thread { get; set; } = "";
    public string Level { get; set; } = "INFO";
    public string RawContent { get; set; } = "";
    
    public string DisplayTime => Timestamp.ToString("HH:mm:ss");
}

public class GameErrorEvent
{
    public DateTime Timestamp { get; set; }
    public string RawLine { get; set; } = "";
    public string ErrorType { get; set; } = "";
    public string Pattern { get; set; } = "";
    public string Version { get; set; } = "";
    public string Severity { get; set; } = "中";
    public string Message { get; set; } = "";
    public string ExceptionType { get; set; } = "";
    public string RelatedMod { get; set; } = "";
    public int ExitCode { get; set; } = -1;
    
    public string DisplayTime => Timestamp.ToString("HH:mm:ss");
    
    public string SuggestedAction => ErrorType switch
    {
        "内存不足" => "建议增加游戏内存分配，或减少使用的模组数量。",
        "缺失模组" => "请安装缺失的模组，或移除需要它的模组。",
        "重复模组" => "请删除重复的模组文件，只保留一个版本。",
        "模组加载失败" => "请检查模组版本是否与游戏版本兼容。",
        "模组依赖缺失" => "请安装所需的前置模组。",
        "Java版本不兼容" => "请安装正确版本的Java运行时。",
        "游戏异常退出" => "请查看详细日志以确定具体原因。",
        _ => "请查看详细日志以确定具体原因。"
    };
}

public class GameWarningEvent
{
    public DateTime Timestamp { get; set; }
    public string RawLine { get; set; } = "";
    public string WarningType { get; set; } = "";
    public string Pattern { get; set; } = "";
    public string Version { get; set; } = "";
    
    public string DisplayTime => Timestamp.ToString("HH:mm:ss");
}

#endregion
