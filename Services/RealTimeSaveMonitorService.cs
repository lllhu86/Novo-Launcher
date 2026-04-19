using System.Diagnostics;
using System.IO;
using System.Text;
using System.Collections.Concurrent;
using System.Windows;

namespace MinecraftLauncher.Services;

public class RealTimeSaveMonitorService : IDisposable
{
    private static RealTimeSaveMonitorService? _instance;
    private static readonly object _lock = new();
    
    private readonly Dictionary<string, FileSystemWatcher> _watchers = new();
    private readonly ConcurrentDictionary<string, DateTime> _lastUpdateTimes = new();
    private readonly ConcurrentDictionary<string, SaveDataSnapshot> _currentSnapshots = new();
    private readonly SemaphoreSlim _updateLock = new(1, 1);
    
    private readonly Dictionary<string, FileSystemWatcher> _modsWatchers = new();
    private readonly ConcurrentDictionary<string, DateTime> _lastModsUpdateTimes = new();
    
    private Process? _currentGameProcess;
    private string _currentVersion = "";
    private string _currentSavePath = "";
    private string _currentModsPath = "";
    private Timer? _throttleTimer;
    private Timer? _periodicCheckTimer;
    private Timer? _modsCheckTimer;
    private bool _isMonitoring;
    private bool _disposed;
    
    private const int ThrottleIntervalMs = 2000;
    private const int PeriodicCheckIntervalMs = 5000;
    private const int MinUpdateIntervalMs = 1000;
    private const int ModsCheckIntervalMs = 3000;
    
    public event Action<SaveDataUpdateEventArgs>? SaveDataUpdated;
    public event Action<string>? MonitoringStarted;
    public event Action<string>? MonitoringStopped;
    public event Action<string>? ActiveSaveChanged;
    public event Action<ModsUpdateEventArgs>? ModsUpdated;
    
    public bool IsMonitoring => _isMonitoring;
    public string CurrentVersion => _currentVersion;
    public string CurrentSavePath => _currentSavePath;
    
    public static RealTimeSaveMonitorService Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    _instance ??= new RealTimeSaveMonitorService();
                }
            }
            return _instance;
        }
    }
    
    private RealTimeSaveMonitorService()
    {
        _throttleTimer = new Timer(ThrottleCallback, null, Timeout.Infinite, Timeout.Infinite);
    }
    
    public void StartMonitoring(Process gameProcess, string version, string? specificSavePath = null)
    {
        if (_isMonitoring)
        {
            StopMonitoring();
        }
        
        _currentGameProcess = gameProcess;
        _currentVersion = version;
        _isMonitoring = true;
        
        var gameDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".minecraft");
        var versionDir = Path.Combine(gameDir, "versions", version);
        var savesDir = Path.Combine(versionDir, "saves");
        
        if (!Directory.Exists(savesDir))
        {
            savesDir = Path.Combine(gameDir, "saves");
        }
        
        if (!string.IsNullOrEmpty(specificSavePath))
        {
            _currentSavePath = specificSavePath;
            MonitorSaveDirectory(specificSavePath, true);
        }
        
        if (Directory.Exists(savesDir))
        {
            SetupDirectoryWatcher(savesDir);
            
            foreach (var saveDir in Directory.GetDirectories(savesDir))
            {
                MonitorSaveDirectory(saveDir, false);
            }
        }
        
        SetupModsMonitoring(versionDir, gameDir);
        
        _periodicCheckTimer = new Timer(PeriodicCheck, null, PeriodicCheckIntervalMs, PeriodicCheckIntervalMs);
        _modsCheckTimer = new Timer(ModsPeriodicCheck, null, ModsCheckIntervalMs, ModsCheckIntervalMs);
        
        if (_currentGameProcess != null)
        {
            _currentGameProcess.EnableRaisingEvents = true;
            _currentGameProcess.Exited += OnGameExited;
        }
        
        MonitoringStarted?.Invoke(version);
        App.LogInfo($"实时存档监测已启动: {version}");
    }
    
    private void SetupModsMonitoring(string versionDir, string gameDir)
    {
        var modsPaths = new List<string>();
        
        var versionModsDir = Path.Combine(versionDir, "mods");
        if (Directory.Exists(versionModsDir))
        {
            modsPaths.Add(versionModsDir);
        }
        
        var globalModsDir = Path.Combine(gameDir, "mods");
        if (Directory.Exists(globalModsDir) && !modsPaths.Contains(globalModsDir))
        {
            modsPaths.Add(globalModsDir);
        }
        
        foreach (var modsPath in modsPaths)
        {
            SetupModsFolderWatcher(modsPath);
        }
        
        if (modsPaths.Count > 0)
        {
            _currentModsPath = modsPaths[0];
            NotifyModsUpdate(modsPaths[0], "初始加载");
        }
    }
    
    private void SetupModsFolderWatcher(string modsPath)
    {
        if (!Directory.Exists(modsPath)) return;
        
        var watcher = new FileSystemWatcher(modsPath)
        {
            IncludeSubdirectories = false,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            Filter = "*.jar",
            EnableRaisingEvents = true
        };
        
        watcher.Changed += OnModsFileChanged;
        watcher.Created += OnModsFileCreated;
        watcher.Deleted += OnModsFileDeleted;
        watcher.Renamed += OnModsFileRenamed;
        
        _modsWatchers[modsPath] = watcher;
        
        var disabledModsDir = Path.Combine(Path.GetDirectoryName(modsPath) ?? modsPath, "disabled_mods");
        if (Directory.Exists(disabledModsDir))
        {
            var disabledWatcher = new FileSystemWatcher(disabledModsDir)
            {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                Filter = "*.jar",
                EnableRaisingEvents = true
            };
            
            disabledWatcher.Changed += OnModsFileChanged;
            disabledWatcher.Created += OnModsFileCreated;
            disabledWatcher.Deleted += OnModsFileDeleted;
            disabledWatcher.Renamed += OnModsFileRenamed;
            
            _modsWatchers[disabledModsDir] = disabledWatcher;
        }
        
        App.LogInfo($"模组文件夹监测已启动: {modsPath}");
    }
    
    private void OnModsFileChanged(object sender, FileSystemEventArgs e)
    {
        if (!_isMonitoring) return;
        
        var modsPath = Path.GetDirectoryName(e.FullPath) ?? "";
        ScheduleModsUpdate(modsPath);
    }
    
    private void OnModsFileCreated(object sender, FileSystemEventArgs e)
    {
        if (!_isMonitoring) return;
        
        var modsPath = Path.GetDirectoryName(e.FullPath) ?? "";
        App.LogInfo($"检测到新模组: {Path.GetFileNameWithoutExtension(e.Name)}");
        ScheduleModsUpdate(modsPath);
    }
    
    private void OnModsFileDeleted(object sender, FileSystemEventArgs e)
    {
        if (!_isMonitoring) return;
        
        var modsPath = Path.GetDirectoryName(e.FullPath) ?? "";
        App.LogInfo($"模组已删除: {Path.GetFileNameWithoutExtension(e.Name)}");
        ScheduleModsUpdate(modsPath);
    }
    
    private void OnModsFileRenamed(object sender, RenamedEventArgs e)
    {
        if (!_isMonitoring) return;
        
        var modsPath = Path.GetDirectoryName(e.FullPath) ?? "";
        App.LogInfo($"模组重命名: {Path.GetFileNameWithoutExtension(e.OldName)} -> {Path.GetFileNameWithoutExtension(e.Name)}");
        ScheduleModsUpdate(modsPath);
    }
    
    private void ScheduleModsUpdate(string modsPath)
    {
        if (_lastModsUpdateTimes.TryGetValue(modsPath, out var lastUpdate))
        {
            if ((DateTime.Now - lastUpdate).TotalMilliseconds < MinUpdateIntervalMs)
                return;
        }
        
        _lastModsUpdateTimes[modsPath] = DateTime.Now;
        
        Task.Run(async () =>
        {
            await Task.Delay(200);
            await DispatcherInvoke(() => NotifyModsUpdate(modsPath, "文件变化"));
        });
    }
    
    private void ModsPeriodicCheck(object? state)
    {
        if (!_isMonitoring || _currentGameProcess?.HasExited != false) return;
        
        if (!string.IsNullOrEmpty(_currentModsPath) && Directory.Exists(_currentModsPath))
        {
            Task.Run(async () =>
            {
                await DispatcherInvoke(() => NotifyModsUpdate(_currentModsPath, "定期检查"));
            });
        }
    }
    
    private void NotifyModsUpdate(string modsPath, string reason)
    {
        var (enabledMods, disabledMods) = GetModsList(modsPath);
        
        var args = new ModsUpdateEventArgs
        {
            ModsPath = modsPath,
            EnabledMods = enabledMods,
            DisabledMods = disabledMods,
            Reason = reason,
            Timestamp = DateTime.Now
        };
        
        ModsUpdated?.Invoke(args);
    }
    
    private (List<string> enabledMods, List<string> disabledMods) GetModsList(string modsPath)
    {
        var enabledMods = new List<string>();
        var disabledMods = new List<string>();
        
        try
        {
            if (Directory.Exists(modsPath))
            {
                enabledMods.AddRange(
                    Directory.GetFiles(modsPath, "*.jar")
                        .Select(Path.GetFileNameWithoutExtension)
                        .Where(name => !string.IsNullOrEmpty(name))!);
            }
            
            var parentDir = Path.GetDirectoryName(modsPath);
            if (!string.IsNullOrEmpty(parentDir))
            {
                var disabledModsDir = Path.Combine(parentDir, "disabled_mods");
                if (Directory.Exists(disabledModsDir))
                {
                    disabledMods.AddRange(
                        Directory.GetFiles(disabledModsDir, "*.jar")
                            .Select(Path.GetFileNameWithoutExtension)
                            .Where(name => !string.IsNullOrEmpty(name))!);
                }
            }
        }
        catch (Exception ex)
        {
            App.LogError($"获取模组列表失败: {modsPath}", ex);
        }
        
        return (enabledMods, disabledMods);
    }
    
    private void SetupDirectoryWatcher(string savesDir)
    {
        var watcher = new FileSystemWatcher(savesDir)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
            Filter = "*.*",
            EnableRaisingEvents = true
        };
        
        watcher.Changed += OnFileChanged;
        watcher.Created += OnFileCreated;
        watcher.Deleted += OnFileDeleted;
        watcher.Renamed += OnFileRenamed;
        
        _watchers[savesDir] = watcher;
    }
    
    private void MonitorSaveDirectory(string savePath, bool isPrimary)
    {
        if (!Directory.Exists(savePath))
            return;
        
        var levelDatPath = Path.Combine(savePath, "level.dat");
        if (File.Exists(levelDatPath))
        {
            var snapshot = ReadSaveSnapshot(levelDatPath);
            if (snapshot != null)
            {
                _currentSnapshots[savePath] = snapshot;
                
                if (isPrimary || string.IsNullOrEmpty(_currentSavePath))
                {
                    _currentSavePath = savePath;
                    NotifyUpdate(savePath, snapshot, "初始加载");
                }
            }
        }
        
        var watcher = new FileSystemWatcher(savePath)
        {
            IncludeSubdirectories = false,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
            Filter = "level.dat",
            EnableRaisingEvents = true
        };
        
        watcher.Changed += OnLevelDatChanged;
        _watchers[savePath] = watcher;
    }
    
    private void OnLevelDatChanged(object sender, FileSystemEventArgs e)
    {
        if (!_isMonitoring) return;
        
        var savePath = Path.GetDirectoryName(e.FullPath) ?? "";
        
        if (_lastUpdateTimes.TryGetValue(savePath, out var lastUpdate))
        {
            if ((DateTime.Now - lastUpdate).TotalMilliseconds < MinUpdateIntervalMs)
                return;
        }
        
        _lastUpdateTimes[savePath] = DateTime.Now;
        
        Task.Run(async () =>
        {
            await Task.Delay(100);
            
            try
            {
                var snapshot = ReadSaveSnapshot(e.FullPath);
                if (snapshot != null)
                {
                    var previousSnapshot = _currentSnapshots.GetValueOrDefault(savePath);
                    _currentSnapshots[savePath] = snapshot;
                    
                    var changes = DetectChanges(previousSnapshot, snapshot);
                    if (changes.Count > 0 || previousSnapshot == null)
                    {
                        await DispatcherInvoke(() => NotifyUpdate(savePath, snapshot, "文件变化"));
                    }
                }
            }
            catch (Exception ex)
            {
                App.LogError($"读取存档快照失败: {e.FullPath}", ex);
            }
        });
    }
    
    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        if (!_isMonitoring) return;
        if (!e.Name?.Contains("level.dat") == true && !e.Name?.EndsWith(".dat") == true) return;
        
        ScheduleThrottledUpdate();
    }
    
    private void OnFileCreated(object sender, FileSystemEventArgs e)
    {
        if (!_isMonitoring) return;
        
        if (e.Name == "level.dat")
        {
            var savePath = Path.GetDirectoryName(e.FullPath) ?? "";
            MonitorSaveDirectory(savePath, false);
            App.LogInfo($"检测到新存档: {Path.GetFileName(savePath)}");
        }
    }
    
    private void OnFileDeleted(object sender, FileSystemEventArgs e)
    {
        if (!_isMonitoring) return;
        
        if (e.Name == "level.dat")
        {
            var savePath = Path.GetDirectoryName(e.FullPath) ?? "";
            _currentSnapshots.TryRemove(savePath, out _);
            _watchers.Remove(savePath);
        }
    }
    
    private void OnFileRenamed(object sender, RenamedEventArgs e)
    {
        if (!_isMonitoring) return;
    }
    
    private void ScheduleThrottledUpdate()
    {
        _throttleTimer?.Change(ThrottleIntervalMs, Timeout.Infinite);
    }
    
    private void ThrottleCallback(object? state)
    {
        if (!_isMonitoring || string.IsNullOrEmpty(_currentSavePath)) return;
        
        var levelDatPath = Path.Combine(_currentSavePath, "level.dat");
        if (File.Exists(levelDatPath))
        {
            Task.Run(async () =>
            {
                try
                {
                    var snapshot = ReadSaveSnapshot(levelDatPath);
                    if (snapshot != null)
                    {
                        var previousSnapshot = _currentSnapshots.GetValueOrDefault(_currentSavePath);
                        _currentSnapshots[_currentSavePath] = snapshot;
                        
                        var changes = DetectChanges(previousSnapshot, snapshot);
                        if (changes.Count > 0)
                        {
                            await DispatcherInvoke(() => NotifyUpdate(_currentSavePath, snapshot, "定期检查"));
                        }
                    }
                }
                catch (Exception ex)
                {
                    App.LogError("节流更新失败", ex);
                }
            });
        }
    }
    
    private void PeriodicCheck(object? state)
    {
        if (!_isMonitoring || _currentGameProcess?.HasExited != false) return;
        
        if (!string.IsNullOrEmpty(_currentSavePath))
        {
            var levelDatPath = Path.Combine(_currentSavePath, "level.dat");
            if (File.Exists(levelDatPath))
            {
                Task.Run(async () =>
                {
                    try
                    {
                        var snapshot = ReadSaveSnapshot(levelDatPath);
                        if (snapshot != null)
                        {
                            var previousSnapshot = _currentSnapshots.GetValueOrDefault(_currentSavePath);
                            _currentSnapshots[_currentSavePath] = snapshot;
                            
                            var changes = DetectChanges(previousSnapshot, snapshot);
                            if (changes.Count > 0)
                            {
                                await DispatcherInvoke(() => NotifyUpdate(_currentSavePath, snapshot, "定期检查"));
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        App.LogError("定期检查失败", ex);
                    }
                });
            }
        }
    }
    
    private SaveDataSnapshot? ReadSaveSnapshot(string levelDatPath)
    {
        try
        {
            if (!File.Exists(levelDatPath))
                return null;
            
            using var fs = File.OpenRead(levelDatPath);
            using var gzipStream = new System.IO.Compression.GZipStream(fs, System.IO.Compression.CompressionMode.Decompress);
            using var reader = new BinaryReader(gzipStream, Encoding.UTF8, true);
            
            byte tagType = reader.ReadByte();
            if (tagType != 10) return null;
            
            ushort nameLength = reader.ReadUInt16();
            reader.ReadBytes(nameLength);
            
            var compound = ReadCompound(reader);
            
            var snapshot = new SaveDataSnapshot
            {
                FilePath = levelDatPath,
                LastUpdateTime = DateTime.Now
            };
            
            if (compound.TryGetValue("Data", out var data) && data is Dictionary<string, object> dataDict)
            {
                snapshot.GameMode = dataDict.GetValueOrDefault("GameType") switch
                {
                    0 => "survival",
                    1 => "creative",
                    2 => "adventure",
                    3 => "spectator",
                    _ => "survival"
                };
                
                snapshot.Difficulty = dataDict.GetValueOrDefault("Difficulty") switch
                {
                    0 => "peaceful",
                    1 => "easy",
                    2 => "normal",
                    3 => "hard",
                    _ => "normal"
                };
                
                snapshot.Seed = dataDict.GetValueOrDefault("RandomSeed")?.ToString() ?? "";
                snapshot.WorldType = dataDict.GetValueOrDefault("generatorName")?.ToString() ?? "default";
                snapshot.HasCheats = dataDict.GetValueOrDefault("allowCommands") is bool b && b;
                
                if (dataDict.TryGetValue("Time", out var time))
                {
                    snapshot.GameTimeTicks = time switch
                    {
                        long l => l,
                        int i => i,
                        _ => 0
                    };
                }
                
                if (dataDict.TryGetValue("DayTime", out var dayTime))
                {
                    snapshot.DayTime = dayTime switch
                    {
                        long l => l,
                        int i => i,
                        _ => 0
                    };
                }
                
                snapshot.Raining = dataDict.GetValueOrDefault("raining") is bool r && r;
                snapshot.Thundering = dataDict.GetValueOrDefault("thundering") is bool t && t;
                
                if (dataDict.TryGetValue("Player", out var player) && player is Dictionary<string, object> playerDict)
                {
                    if (playerDict.TryGetValue("Pos", out var pos) && pos is List<object> posList && posList.Count >= 3)
                    {
                        snapshot.PlayerX = Convert.ToDouble(posList[0]);
                        snapshot.PlayerY = Convert.ToDouble(posList[1]);
                        snapshot.PlayerZ = Convert.ToDouble(posList[2]);
                    }
                    
                    if (playerDict.TryGetValue("Dimension", out var dim))
                    {
                        snapshot.Dimension = dim switch
                        {
                            int i => i,
                            string s when s.Contains("overworld") => 0,
                            string s when s.Contains("the_nether") => -1,
                            string s when s.Contains("the_end") => 1,
                            _ => 0
                        };
                    }
                    
                    if (playerDict.TryGetValue("XpLevel", out var xpLevel))
                    {
                        snapshot.XpLevel = Convert.ToInt32(xpLevel);
                    }
                    
                    if (playerDict.TryGetValue("Health", out var health))
                    {
                        snapshot.Health = Convert.ToSingle(health);
                    }
                    
                    if (playerDict.TryGetValue("FoodLevel", out var food))
                    {
                        snapshot.FoodLevel = Convert.ToInt32(food);
                    }
                }
                
                snapshot.LevelName = dataDict.GetValueOrDefault("LevelName")?.ToString() ?? "";
            }
            
            return snapshot;
        }
        catch (Exception ex)
        {
            App.LogError($"读取存档快照失败: {levelDatPath}", ex);
            return null;
        }
    }
    
    private Dictionary<string, object> ReadCompound(BinaryReader reader)
    {
        var result = new Dictionary<string, object>();
        
        while (true)
        {
            byte tagType = reader.ReadByte();
            if (tagType == 0) break;
            
            ushort nameLength = reader.ReadUInt16();
            byte[] nameBytes = reader.ReadBytes(nameLength);
            string name = Encoding.UTF8.GetString(nameBytes);
            
            object value = ReadTag(reader, tagType);
            result[name] = value;
        }
        
        return result;
    }
    
    private object ReadTag(BinaryReader reader, byte tagType)
    {
        return tagType switch
        {
            1 => reader.ReadByte(),
            2 => reader.ReadInt16(),
            3 => reader.ReadInt32(),
            4 => reader.ReadInt64(),
            5 => reader.ReadSingle(),
            6 => reader.ReadDouble(),
            7 => ReadByteArray(reader),
            8 => ReadString(reader),
            9 => ReadList(reader),
            10 => ReadCompound(reader),
            11 => ReadIntArray(reader),
            12 => ReadLongArray(reader),
            _ => new object()
        };
    }
    
    private byte[] ReadByteArray(BinaryReader reader)
    {
        int length = reader.ReadInt32();
        return reader.ReadBytes(length);
    }
    
    private string ReadString(BinaryReader reader)
    {
        ushort length = reader.ReadUInt16();
        byte[] bytes = reader.ReadBytes(length);
        return Encoding.UTF8.GetString(bytes);
    }
    
    private List<object> ReadList(BinaryReader reader)
    {
        var list = new List<object>();
        byte listType = reader.ReadByte();
        int length = reader.ReadInt32();
        
        for (int i = 0; i < length; i++)
        {
            list.Add(ReadTag(reader, listType));
        }
        
        return list;
    }
    
    private int[] ReadIntArray(BinaryReader reader)
    {
        int length = reader.ReadInt32();
        var result = new int[length];
        for (int i = 0; i < length; i++)
        {
            result[i] = reader.ReadInt32();
        }
        return result;
    }
    
    private long[] ReadLongArray(BinaryReader reader)
    {
        int length = reader.ReadInt32();
        var result = new long[length];
        for (int i = 0; i < length; i++)
        {
            result[i] = reader.ReadInt64();
        }
        return result;
    }
    
    private List<string> DetectChanges(SaveDataSnapshot? previous, SaveDataSnapshot current)
    {
        var changes = new List<string>();
        
        if (previous == null)
        {
            changes.Add("初始加载");
            return changes;
        }
        
        if (previous.GameMode != current.GameMode)
            changes.Add($"游戏模式: {previous.GameMode} → {current.GameMode}");
        
        if (previous.Difficulty != current.Difficulty)
            changes.Add($"难度: {previous.Difficulty} → {current.Difficulty}");
        
        if (previous.Dimension != current.Dimension)
            changes.Add($"维度变化: {previous.Dimension} → {current.Dimension}");
        
        if (Math.Abs(previous.PlayerX - current.PlayerX) > 100 ||
            Math.Abs(previous.PlayerZ - current.PlayerZ) > 100)
            changes.Add("位置显著变化");
        
        if (Math.Abs(current.GameTimeTicks - previous.GameTimeTicks) > 1200)
            changes.Add("游戏时间推进");
        
        if (previous.Raining != current.Raining || previous.Thundering != current.Thundering)
            changes.Add("天气变化");
        
        if (current.Health < previous.Health)
            changes.Add("生命值下降");
        
        if (current.XpLevel > previous.XpLevel)
            changes.Add($"等级提升: {previous.XpLevel} → {current.XpLevel}");
        
        return changes;
    }
    
    private void NotifyUpdate(string savePath, SaveDataSnapshot snapshot, string reason)
    {
        var args = new SaveDataUpdateEventArgs
        {
            SavePath = savePath,
            Snapshot = snapshot,
            Reason = reason,
            Timestamp = DateTime.Now
        };
        
        SaveDataUpdated?.Invoke(args);
    }
    
    private async Task DispatcherInvoke(Action action)
    {
        await Application.Current.Dispatcher.InvokeAsync(action);
    }
    
    private void OnGameExited(object? sender, EventArgs e)
    {
        App.LogInfo("游戏进程退出，停止实时监测");
        StopMonitoring();
    }
    
    public void StopMonitoring()
    {
        if (!_isMonitoring) return;
        
        _isMonitoring = false;
        
        _throttleTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        _periodicCheckTimer?.Dispose();
        _periodicCheckTimer = null;
        _modsCheckTimer?.Dispose();
        _modsCheckTimer = null;
        
        foreach (var watcher in _watchers.Values)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Changed -= OnLevelDatChanged;
            watcher.Changed -= OnFileChanged;
            watcher.Created -= OnFileCreated;
            watcher.Deleted -= OnFileDeleted;
            watcher.Renamed -= OnFileRenamed;
            watcher.Dispose();
        }
        _watchers.Clear();
        
        foreach (var watcher in _modsWatchers.Values)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Changed -= OnModsFileChanged;
            watcher.Created -= OnModsFileCreated;
            watcher.Deleted -= OnModsFileDeleted;
            watcher.Renamed -= OnModsFileRenamed;
            watcher.Dispose();
        }
        _modsWatchers.Clear();
        
        if (_currentGameProcess != null)
        {
            _currentGameProcess.Exited -= OnGameExited;
        }
        
        _currentGameProcess = null;
        _currentVersion = "";
        _currentSavePath = "";
        _currentModsPath = "";
        
        MonitoringStopped?.Invoke(_currentVersion);
        App.LogInfo("实时存档监测已停止");
    }
    
    public SaveDataSnapshot? GetCurrentSnapshot(string? savePath = null)
    {
        var path = savePath ?? _currentSavePath;
        if (string.IsNullOrEmpty(path)) return null;
        
        return _currentSnapshots.GetValueOrDefault(path);
    }
    
    public void SetActiveSave(string savePath)
    {
        if (!Directory.Exists(savePath)) return;
        
        _currentSavePath = savePath;
        ActiveSaveChanged?.Invoke(savePath);
        
        var levelDatPath = Path.Combine(savePath, "level.dat");
        if (File.Exists(levelDatPath))
        {
            var snapshot = ReadSaveSnapshot(levelDatPath);
            if (snapshot != null)
            {
                _currentSnapshots[savePath] = snapshot;
                NotifyUpdate(savePath, snapshot, "切换活动存档");
            }
        }
    }
    
    public void Dispose()
    {
        if (_disposed) return;
        
        _disposed = true;
        StopMonitoring();
        _throttleTimer?.Dispose();
        _updateLock?.Dispose();
        GC.SuppressFinalize(this);
    }
}

public class SaveDataSnapshot
{
    public string FilePath { get; set; } = "";
    public DateTime LastUpdateTime { get; set; }
    
    public string GameMode { get; set; } = "survival";
    public string Difficulty { get; set; } = "normal";
    public string Seed { get; set; } = "";
    public string WorldType { get; set; } = "default";
    public bool HasCheats { get; set; }
    
    public double PlayerX { get; set; }
    public double PlayerY { get; set; }
    public double PlayerZ { get; set; }
    public int Dimension { get; set; }
    
    public long GameTimeTicks { get; set; }
    public long DayTime { get; set; }
    public bool Raining { get; set; }
    public bool Thundering { get; set; }
    
    public int XpLevel { get; set; }
    public float Health { get; set; }
    public int FoodLevel { get; set; }
    
    public string LevelName { get; set; } = "";
    
    public string DisplayGameMode => GameMode switch
    {
        "survival" => "生存模式",
        "creative" => "创造模式",
        "adventure" => "冒险模式",
        "spectator" => "旁观模式",
        _ => GameMode
    };
    
    public string DisplayDifficulty => Difficulty switch
    {
        "peaceful" => "和平",
        "easy" => "简单",
        "normal" => "普通",
        "hard" => "困难",
        _ => Difficulty
    };
    
    public string DisplayWorldType => WorldType switch
    {
        "default" => "标准",
        "flat" => "超平坦",
        "large_biomes" => "巨型生物群系",
        "amplified" => "放大化",
        "single_biome_surface" => "单一生物群系",
        _ => WorldType
    };
    
    public string DisplayDimension => Dimension switch
    {
        -1 => "下界",
        0 => "主世界",
        1 => "末地",
        _ => $"维度 {Dimension}"
    };
    
    public string DisplayWeather
    {
        get
        {
            if (Thundering) return "⛈ 雷暴";
            if (Raining) return "🌧 雨天";
            return "☀ 晴朗";
        }
    }
    
    public string DisplayGameTime
    {
        get
        {
            if (GameTimeTicks == 0) return "未知";
            
            var totalMinutes = GameTimeTicks / (20 * 60);
            var days = (int)(totalMinutes / 1440);
            var hours = (int)((totalMinutes % 1440) / 60);
            var minutes = (int)(totalMinutes % 60);
            
            if (days > 0) return $"{days}天{hours}小时";
            if (hours > 0) return $"{hours}小时{minutes}分";
            return $"{minutes}分钟";
        }
    }
    
    public string DisplayDayTime
    {
        get
        {
            var dayTicks = DayTime % 24000;
            var hours = (int)(dayTicks / 1000);
            var minutes = (int)((dayTicks % 1000) * 60 / 1000);
            return $"{hours:D2}:{minutes:D2}";
        }
    }
    
    public string DisplayCoordinates => $"X: {PlayerX:F0}, Y: {PlayerY:F0}, Z: {PlayerZ:F0}";
    
    public string DisplayHealth => Health > 0 ? $"{Health:F0}/20" : "20/20";
    
    public string DisplayFood => FoodLevel > 0 ? $"{FoodLevel}/20" : "20/20";
}

public class SaveDataUpdateEventArgs : EventArgs
{
    public string SavePath { get; set; } = "";
    public SaveDataSnapshot Snapshot { get; set; } = new();
    public string Reason { get; set; } = "";
    public DateTime Timestamp { get; set; }
    public List<string> Changes { get; set; } = new();
}

public class ModsUpdateEventArgs : EventArgs
{
    public string ModsPath { get; set; } = "";
    public List<string> EnabledMods { get; set; } = new();
    public List<string> DisabledMods { get; set; } = new();
    public string Reason { get; set; } = "";
    public DateTime Timestamp { get; set; }
    
    public int TotalModsCount => EnabledMods.Count + DisabledMods.Count;
}
