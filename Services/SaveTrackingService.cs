using System.Diagnostics;
using System.IO;
using System.Text.Json;
using MinecraftLauncher.Models;
using System.Text;

namespace MinecraftLauncher.Services;

public class SaveTrackingService
{
    private static SaveTrackingService? _instance;
    private static readonly object _lock = new();
    private static readonly SemaphoreSlim _ioSemaphore = new(1, 1);
    
    private string _gameDir;
    private readonly string _dataFilePath;
    private SaveRecordData _saveData;
    private Process? _currentGameProcess;
    private DateTime _gameStartTime;
    private string _currentGameVersion = "";
    private string _currentSavePath = "";
    private System.Timers.Timer? _saveCheckTimer;
    private List<string> _knownSaves = new();
    
    public event Action? SaveRecordsChanged;
    public event Action<SaveRecord>? SaveRecordClicked;
    public event Action<SaveRecord, SaveDataSnapshot>? RealTimeDataUpdated;
    public event Action<string>? ActiveSaveDetected;
    
    public static SaveTrackingService Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    _instance ??= new SaveTrackingService();
                }
            }
            return _instance;
        }
    }
    
    private SaveTrackingService()
    {
        _gameDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".minecraft");
        _dataFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "save_records.json");
        _saveData = LoadSaveData();
    }
    
    public void SetGameDirectory(string gameDir)
    {
        if (!string.IsNullOrEmpty(gameDir))
        {
            _gameDir = gameDir;
        }
    }
    
    public void Initialize()
    {
        _knownSaves = GetCurrentSaves();
        StartSaveCheckTimer();
    }
    
    public List<SaveRecord> GetSaveRecords()
    {
        var removedSaves = _saveData.SaveRecords
            .Where(s => !Directory.Exists(s.SavePath))
            .Select(s => s.SaveName)
            .ToList();
        
        if (removedSaves.Count > 0)
        {
            foreach (var saveName in removedSaves)
            {
                var record = _saveData.SaveRecords.FirstOrDefault(s => s.SaveName == saveName);
                if (record != null)
                {
                    _saveData.SaveRecords.Remove(record);
                    App.LogInfo($"存档已删除，移除记录：{saveName}");
                }
            }
            _ = SaveSaveDataAsync();
        }
        
        return _saveData.SaveRecords.OrderByDescending(s => s.LastPlayedTime).ToList();
    }
    
    public SaveRecord? GetSaveRecord(string saveName)
    {
        return _saveData.SaveRecords.FirstOrDefault(s => s.SaveName == saveName);
    }
    
    public SaveRecord? GetSaveRecordByPath(string savePath)
    {
        return _saveData.SaveRecords.FirstOrDefault(s => s.SavePath == savePath);
    }
    
    public void StartGameSession(Process gameProcess, string gameVersion, string? specificSavePath = null)
    {
        _currentGameProcess = gameProcess;
        _gameStartTime = DateTime.Now;
        _currentGameVersion = gameVersion;
        _knownSaves = GetCurrentSaves();
        _currentSavePath = specificSavePath ?? "";
        
        RealTimeSaveMonitorService.Instance.SaveDataUpdated += OnRealTimeDataUpdated;
        RealTimeSaveMonitorService.Instance.ActiveSaveChanged += OnActiveSaveChanged;
        RealTimeSaveMonitorService.Instance.StartMonitoring(gameProcess, gameVersion, specificSavePath);
        
        App.LogInfo($"开始游戏会话追踪，版本：{gameVersion}");
    }
    
    private void OnRealTimeDataUpdated(SaveDataUpdateEventArgs args)
    {
        try
        {
            var record = _saveData.SaveRecords.FirstOrDefault(s => s.SavePath == args.SavePath);
            if (record != null)
            {
                UpdateRecordFromSnapshot(record, args.Snapshot);
                RealTimeDataUpdated?.Invoke(record, args.Snapshot);
                App.LogInfo($"实时数据更新: {record.SaveName} - {args.Reason}");
            }
        }
        catch (Exception ex)
        {
            App.LogError("处理实时数据更新失败", ex);
        }
    }
    
    private void OnActiveSaveChanged(string savePath)
    {
        _currentSavePath = savePath;
        ActiveSaveDetected?.Invoke(savePath);
        App.LogInfo($"活动存档切换: {Path.GetFileName(savePath)}");
    }
    
    private void UpdateRecordFromSnapshot(SaveRecord record, SaveDataSnapshot snapshot)
    {
        record.GameMode = snapshot.GameMode;
        record.Difficulty = snapshot.Difficulty;
        record.Seed = snapshot.Seed;
        record.WorldType = snapshot.WorldType;
        record.HasCheats = snapshot.HasCheats;
        record.PlayerX = snapshot.PlayerX;
        record.PlayerY = snapshot.PlayerY;
        record.PlayerZ = snapshot.PlayerZ;
        record.Dimension = snapshot.Dimension;
        record.GameTimeTicks = snapshot.GameTimeTicks;
        record.Raining = snapshot.Raining;
        record.Thundering = snapshot.Thundering;
        record.LastPlayedTime = DateTime.Now;
    }
    
    public SaveDataSnapshot? GetCurrentRealTimeSnapshot()
    {
        return RealTimeSaveMonitorService.Instance.GetCurrentSnapshot(_currentSavePath);
    }
    
    public void SetActiveSave(string savePath)
    {
        _currentSavePath = savePath;
        RealTimeSaveMonitorService.Instance.SetActiveSave(savePath);
    }
    
    public void EndGameSession()
    {
        if (_currentGameProcess == null)
            return;
        
        RealTimeSaveMonitorService.Instance.SaveDataUpdated -= OnRealTimeDataUpdated;
        RealTimeSaveMonitorService.Instance.ActiveSaveChanged -= OnActiveSaveChanged;
        RealTimeSaveMonitorService.Instance.StopMonitoring();
        
        var playTime = DateTime.Now - _gameStartTime;
        App.LogInfo($"游戏会话结束，游玩时长：{playTime.TotalMinutes:F1} 分钟");
        
        // 使用版本隔离的存档目录
        var versionSavesDir = GetVersionSavesDir(_currentGameVersion);
        var currentSaves = GetCurrentSavesForVersion(_currentGameVersion);
        var modifiedSaves = new List<string>();
        
        foreach (var saveName in currentSaves)
        {
            var savePath = Path.Combine(versionSavesDir, saveName);
            
            if (Directory.Exists(savePath))
            {
                var lastWriteTime = Directory.GetLastWriteTime(savePath);
                
                if (lastWriteTime > _gameStartTime)
                {
                    modifiedSaves.Add(saveName);
                }
            }
        }
        
        foreach (var saveName in modifiedSaves)
        {
            var savePath = Path.Combine(versionSavesDir, saveName);
            UpdateSaveRecord(saveName, savePath, playTime);
        }
        
        _ = SaveSaveDataAsync();
        SaveRecordsChanged?.Invoke();
        
        _currentGameProcess = null;
        _currentGameVersion = "";
    }
    
    private string GetVersionSavesDir(string version)
    {
        if (string.IsNullOrEmpty(version))
        {
            return Path.Combine(_gameDir, "saves");
        }
        return Path.Combine(_gameDir, "versions", version, "saves");
    }
    
    private List<string> GetCurrentSavesForVersion(string version)
    {
        var savesDir = GetVersionSavesDir(version);
        if (!Directory.Exists(savesDir))
        {
            return new List<string>();
        }
        
        return Directory.GetDirectories(savesDir)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrEmpty(name))
            .Cast<string>()
            .ToList();
    }
    
    private List<string> GetCurrentSaves()
    {
        var allSaves = new List<string>();
        
        // 扫描所有版本的存档目录
        var versionsDir = Path.Combine(_gameDir, "versions");
        if (Directory.Exists(versionsDir))
        {
            foreach (var versionDir in Directory.GetDirectories(versionsDir))
            {
                var savesDir = Path.Combine(versionDir, "saves");
                if (Directory.Exists(savesDir))
                {
                    foreach (var saveDir in Directory.GetDirectories(savesDir))
                    {
                        var saveName = Path.GetFileName(saveDir);
                        if (!string.IsNullOrEmpty(saveName) && !allSaves.Contains(saveName))
                        {
                            allSaves.Add(saveName);
                        }
                    }
                }
            }
        }
        
        // 也扫描旧的共享存档目录（向后兼容）
        var sharedSavesDir = Path.Combine(_gameDir, "saves");
        if (Directory.Exists(sharedSavesDir))
        {
            foreach (var saveDir in Directory.GetDirectories(sharedSavesDir))
            {
                var saveName = Path.GetFileName(saveDir);
                if (!string.IsNullOrEmpty(saveName) && !allSaves.Contains(saveName))
                {
                    allSaves.Add(saveName);
                }
            }
        }
        
        return allSaves;
    }
    
    private void UpdateSaveRecord(string saveName, string savePath, TimeSpan sessionPlayTime)
    {
        var existingRecord = _saveData.SaveRecords.FirstOrDefault(s => s.SavePath == savePath);
        var saveInfo = ReadSaveInfo(savePath);
        
        var gameVersion = ExtractVersionFromPath(savePath);
        
        if (existingRecord != null)
            {
                existingRecord.LastPlayedTime = DateTime.Now;
                existingRecord.TotalPlayTime += sessionPlayTime;
                existingRecord.GameVersion = gameVersion;
                existingRecord.GameMode = saveInfo.GameMode;
                existingRecord.Difficulty = saveInfo.Difficulty;
                existingRecord.Seed = saveInfo.Seed;
                existingRecord.WorldType = saveInfo.WorldType;
                existingRecord.HasCheats = saveInfo.HasCheats;
                existingRecord.IsModded = CheckIfModded(savePath);
                
                if (saveInfo.PlayerX != 0 || saveInfo.PlayerY != 0 || saveInfo.PlayerZ != 0)
                {
                    existingRecord.PlayerX = saveInfo.PlayerX;
                    existingRecord.PlayerY = saveInfo.PlayerY;
                    existingRecord.PlayerZ = saveInfo.PlayerZ;
                    existingRecord.Dimension = saveInfo.Dimension;
                }
                
                if (saveInfo.Time > 0)
                {
                    existingRecord.GameTimeTicks = saveInfo.Time;
                }
                
                existingRecord.Raining = saveInfo.Raining;
                existingRecord.Thundering = saveInfo.Thundering;
                
                App.LogInfo($"更新存档记录：{saveName} (v{gameVersion}), 总时长：{existingRecord.TotalPlayTime.TotalMinutes:F1} 分钟");
            }
        else
            {
                var newRecord = new SaveRecord
                {
                    SaveName = saveName,
                    SavePath = savePath,
                    GameVersion = gameVersion,
                    LastPlayedTime = DateTime.Now,
                    TotalPlayTime = sessionPlayTime,
                    GameMode = saveInfo.GameMode,
                    Difficulty = saveInfo.Difficulty,
                    Seed = saveInfo.Seed,
                    WorldType = saveInfo.WorldType,
                    HasCheats = saveInfo.HasCheats,
                    IsModded = CheckIfModded(savePath),
                    
                    PlayerX = saveInfo.PlayerX,
                    PlayerY = saveInfo.PlayerY,
                    PlayerZ = saveInfo.PlayerZ,
                    Dimension = saveInfo.Dimension,
                    GameTimeTicks = saveInfo.Time,
                    Raining = saveInfo.Raining,
                    Thundering = saveInfo.Thundering
                };
                _saveData.SaveRecords.Add(newRecord);
                App.LogInfo($"新增存档记录：{saveName} (v{gameVersion}), 时长：{sessionPlayTime.TotalMinutes:F1} 分钟");
            }
    }
    
    private string ExtractVersionFromPath(string savePath)
    {
        try
        {
            var parts = savePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            for (int i = 0; i < parts.Length - 1; i++)
            {
                if (parts[i].Equals("versions", StringComparison.OrdinalIgnoreCase) && i + 1 < parts.Length)
                {
                    return parts[i + 1];
                }
            }
            return _currentGameVersion;
        }
        catch
        {
            return _currentGameVersion;
        }
    }
    
    public SaveRecord RefreshSaveInfo(string saveName)
    {
        var record = _saveData.SaveRecords.FirstOrDefault(s => s.SaveName == saveName);
        
        if (record != null && !string.IsNullOrEmpty(record.SavePath) && Directory.Exists(record.SavePath))
            {
                var saveInfo = ReadSaveInfo(record.SavePath);
                record.GameMode = saveInfo.GameMode;
                record.Difficulty = saveInfo.Difficulty;
                record.Seed = saveInfo.Seed;
                record.WorldType = saveInfo.WorldType;
                record.HasCheats = saveInfo.HasCheats;
                record.IsModded = CheckIfModded(record.SavePath);
                record.EnabledMods = GetModsList(record.SavePath, true);
                record.DisabledMods = GetModsList(record.SavePath, false);
                record.GameVersion = ExtractVersionFromPath(record.SavePath);
                
                if (saveInfo.PlayerX != 0 || saveInfo.PlayerY != 0 || saveInfo.PlayerZ != 0)
                {
                    record.PlayerX = saveInfo.PlayerX;
                    record.PlayerY = saveInfo.PlayerY;
                    record.PlayerZ = saveInfo.PlayerZ;
                    record.Dimension = saveInfo.Dimension;
                }
                
                if (saveInfo.Time > 0)
                {
                    record.GameTimeTicks = saveInfo.Time;
                }
                
                record.Raining = saveInfo.Raining;
                record.Thundering = saveInfo.Thundering;
                
                _ = SaveSaveDataAsync();
            }
        
        return record ?? new SaveRecord { SaveName = saveName };
    }
    
    public SaveRecord RefreshSaveInfoByPath(string savePath)
    {
        var record = _saveData.SaveRecords.FirstOrDefault(s => s.SavePath == savePath);
        
        if (record != null && Directory.Exists(savePath))
        {
            var saveInfo = ReadSaveInfo(savePath);
            record.GameMode = saveInfo.GameMode;
            record.Difficulty = saveInfo.Difficulty;
            record.Seed = saveInfo.Seed;
            record.WorldType = saveInfo.WorldType;
            record.HasCheats = saveInfo.HasCheats;
            record.IsModded = CheckIfModded(savePath);
            record.EnabledMods = GetModsList(savePath, true);
            record.DisabledMods = GetModsList(savePath, false);
            record.GameVersion = ExtractVersionFromPath(savePath);
            
            if (saveInfo.PlayerX != 0 || saveInfo.PlayerY != 0 || saveInfo.PlayerZ != 0)
            {
                record.PlayerX = saveInfo.PlayerX;
                record.PlayerY = saveInfo.PlayerY;
                record.PlayerZ = saveInfo.PlayerZ;
                record.Dimension = saveInfo.Dimension;
            }
            
            if (saveInfo.Time > 0)
            {
                record.GameTimeTicks = saveInfo.Time;
            }
            
            record.Raining = saveInfo.Raining;
            record.Thundering = saveInfo.Thundering;
            
            _ = SaveSaveDataAsync();
        }
        
        return record ?? new SaveRecord { SaveName = Path.GetFileName(savePath), SavePath = savePath, GameVersion = ExtractVersionFromPath(savePath) };
    }
    
    private (string GameMode, string Difficulty, string Seed, string WorldType, bool HasCheats, 
             double PlayerX, double PlayerY, double PlayerZ, int Dimension, long Time, 
             long DayTime, bool Raining, bool Thundering) ReadSaveInfo(string savePath)
    {
        var result = (GameMode: "未知", Difficulty: "未知", Seed: "", WorldType: "未知", 
                     HasCheats: false, PlayerX: 0.0, PlayerY: 0.0, PlayerZ: 0.0, 
                     Dimension: 0, Time: 0L, DayTime: 0L, Raining: false, Thundering: false);
        
        try
        {
            var levelDatPath = Path.Combine(savePath, "level.dat");
            if (!File.Exists(levelDatPath))
                return result;

            var compound = NbtParser.ParseGzipFile(levelDatPath);

            if (compound.TryGetValue("Data", out var data) && data is Dictionary<string, object> dataDict)
            {
                if (dataDict.TryGetValue("GameType", out var gameType))
                {
                    result.GameMode = NbtParser.GetInt32(gameType) switch
                    {
                        0 => "survival",
                        1 => "creative",
                        2 => "adventure",
                        3 => "spectator",
                        _ => "survival"
                    };
                }
                
                if (dataDict.TryGetValue("Difficulty", out var difficulty))
                {
                    result.Difficulty = NbtParser.GetInt32(difficulty) switch
                    {
                        0 => "peaceful",
                        1 => "easy",
                        2 => "normal",
                        3 => "hard",
                        _ => "normal"
                    };
                }
                
                if (dataDict.TryGetValue("RandomSeed", out var seed))
                {
                    result.Seed = seed?.ToString() ?? "";
                }
                
                if (dataDict.TryGetValue("allowCommands", out var allowCommands))
                {
                    result.HasCheats = NbtParser.GetBoolean(allowCommands);
                }
                
                if (dataDict.TryGetValue("generatorName", out var generatorName))
                {
                    result.WorldType = generatorName?.ToString() ?? "default";
                }
                
                if (dataDict.TryGetValue("Time", out var time))
                {
                    result.Time = NbtParser.GetInt64(time);
                }
                
                if (dataDict.TryGetValue("DayTime", out var dayTime))
                {
                    result.DayTime = NbtParser.GetInt64(dayTime);
                }
                
                if (dataDict.TryGetValue("raining", out var raining))
                {
                    result.Raining = NbtParser.GetBoolean(raining);
                }
                
                if (dataDict.TryGetValue("thundering", out var thundering))
                {
                    result.Thundering = NbtParser.GetBoolean(thundering);
                }

                if (dataDict.TryGetValue("Player", out var playerObj) && playerObj is Dictionary<string, object> playerDict)
                {
                    if (playerDict.TryGetValue("Pos", out var posObj) && posObj is List<object> posList && posList.Count >= 3)
                    {
                        result.PlayerX = NbtParser.GetDouble(posList[0]);
                        result.PlayerY = NbtParser.GetDouble(posList[1]);
                        result.PlayerZ = NbtParser.GetDouble(posList[2]);
                    }

                    if (playerDict.TryGetValue("Dimension", out var dimension))
                    {
                        if (dimension is int dimInt)
                            result.Dimension = dimInt;
                        else if (dimension is string dimStr)
                            result.Dimension = dimStr switch
                            {
                                "minecraft:overworld" or "Overworld" or "" => 0,
                                "minecraft:the_nether" or "Nether" => -1,
                                "minecraft:the_end" or "End" => 1,
                                _ => 0
                            };
                    }
                }
            }
        }
        catch (Exception ex)
        {
            App.LogError($"读取存档信息失败：{savePath}", ex);
        }
        
        return result;
    }
    
    private bool CheckIfModded(string savePath)
    {
        var modsDir = Path.Combine(savePath, "mods");
        if (Directory.Exists(modsDir) && Directory.GetFiles(modsDir, "*.jar").Length > 0)
        {
            return true;
        }
        
        var parentModsDir = Path.Combine(Directory.GetParent(savePath)?.Parent?.FullName ?? "", "mods");
        if (Directory.Exists(parentModsDir) && Directory.GetFiles(parentModsDir, "*.jar").Length > 0)
        {
            return true;
        }
        
        return false;
    }
    
    private List<string> GetModsList(string savePath, bool enabled)
    {
        var mods = new List<string>();
        
        var modsDir = Path.Combine(savePath, "mods");
        if (Directory.Exists(modsDir))
        {
            if (enabled)
            {
                mods.AddRange(Directory.GetFiles(modsDir, "*.jar").Select(Path.GetFileNameWithoutExtension));
            }
        }
        
        var disabledModsDir = Path.Combine(savePath, "disabled_mods");
        if (Directory.Exists(disabledModsDir))
        {
            if (!enabled)
            {
                mods.AddRange(Directory.GetFiles(disabledModsDir, "*.jar").Select(Path.GetFileNameWithoutExtension));
            }
        }
        
        return mods;
    }
    
    private void StartSaveCheckTimer()
    {
        _saveCheckTimer = new System.Timers.Timer(30000);
        _saveCheckTimer.Elapsed += async (s, e) => await CheckForNewSavesAsync();
        _saveCheckTimer.Start();
    }
    
    private async Task CheckForNewSavesAsync()
    {
        try
        {
            var currentSaves = GetCurrentSaves();
            var newSaves = currentSaves.Except(_knownSaves).ToList();
            
            if (newSaves.Count > 0)
            {
                App.LogInfo($"检测到新存档：{string.Join(", ", newSaves)}");
                _knownSaves = currentSaves;
                
                if (_currentGameProcess != null && !_currentGameProcess.HasExited)
                {
                    var playTime = DateTime.Now - _gameStartTime;
                    var versionSavesDir = GetVersionSavesDir(_currentGameVersion);
                    foreach (var saveName in newSaves)
                    {
                        var savePath = Path.Combine(versionSavesDir, saveName);
                        if (Directory.Exists(savePath))
                        {
                            UpdateSaveRecord(saveName, savePath, playTime);
                        }
                    }
                    await SaveSaveDataAsync();
                    SaveRecordsChanged?.Invoke();
                }
            }
        }
        catch (Exception ex)
        {
            App.LogError("检查新存档失败", ex);
        }
    }
    
    private SaveRecordData LoadSaveData()
    {
        try
        {
            if (File.Exists(_dataFilePath))
            {
                var json = File.ReadAllText(_dataFilePath);
                var data = JsonSerializer.Deserialize<SaveRecordData>(json);
                if (data != null)
                {
                    App.LogInfo($"加载存档记录：{data.SaveRecords.Count} 条");
                    return data;
                }
            }
        }
        catch (Exception ex)
        {
            App.LogError("加载存档记录失败", ex);
        }
        
        return new SaveRecordData();
    }
    
    private async Task SaveSaveDataAsync()
    {
        try
        {
            await _ioSemaphore.WaitAsync();
            try
            {
                var json = JsonSerializer.Serialize(_saveData, new JsonSerializerOptions 
                { 
                    WriteIndented = true 
                });
                await File.WriteAllTextAsync(_dataFilePath, json);
                App.LogInfo("存档记录已保存");
            }
            finally
            {
                _ioSemaphore.Release();
            }
        }
        catch (Exception ex)
        {
            App.LogError("保存存档记录失败", ex);
        }
    }
    
    public void RefreshRecords()
    {
        SaveRecordsChanged?.Invoke();
    }
    
    public void ClearOldRecords(int keepLast = 10)
    {
        var sortedRecords = _saveData.SaveRecords
            .OrderByDescending(s => s.LastPlayedTime)
            .ToList();
        
        if (sortedRecords.Count > keepLast)
        {
            _saveData.SaveRecords = sortedRecords.Take(keepLast).ToList();
            _ = SaveSaveDataAsync();
            SaveRecordsChanged?.Invoke();
            App.LogInfo($"清理旧存档记录，保留最近 {keepLast} 条");
        }
    }
    
    public void OnSaveRecordClicked(SaveRecord record)
    {
        SaveRecordClicked?.Invoke(record);
    }
    
    public void Dispose()
    {
        _saveCheckTimer?.Stop();
        _saveCheckTimer?.Dispose();
        _ioSemaphore?.Dispose();
    }
}
