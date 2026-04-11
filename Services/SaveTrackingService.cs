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
    private System.Timers.Timer? _saveCheckTimer;
    private List<string> _knownSaves = new();
    
    public event Action? SaveRecordsChanged;
    public event Action<SaveRecord>? SaveRecordClicked;
    
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
    
    public void StartGameSession(Process gameProcess, string gameVersion)
    {
        _currentGameProcess = gameProcess;
        _gameStartTime = DateTime.Now;
        _currentGameVersion = gameVersion;
        _knownSaves = GetCurrentSaves();
        
        App.LogInfo($"开始游戏会话追踪，版本：{gameVersion}");
    }
    
    public void EndGameSession()
    {
        if (_currentGameProcess == null)
            return;
        
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
            if (File.Exists(levelDatPath))
            {
                using var fs = File.OpenRead(levelDatPath);
                using var gzipStream = new System.IO.Compression.GZipStream(fs, System.IO.Compression.CompressionMode.Decompress);
                using var reader = new BinaryReader(gzipStream, Encoding.UTF8, true);
                
                byte tagType = reader.ReadByte();
                if (tagType == 10)
                {
                    ushort nameLength = reader.ReadUInt16();
                    byte[] nameBytes = reader.ReadBytes(nameLength);
                    
                    var compound = ReadCompound(reader);
                    
                    if (compound.TryGetValue("Data", out var data) && data is Dictionary<string, object> dataDict)
                    {
                        if (dataDict.TryGetValue("GameType", out var gameType))
                        {
                            result.GameMode = gameType switch
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
                            result.Difficulty = difficulty switch
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
                            result.HasCheats = allowCommands is bool b && b;
                        }
                        
                        if (dataDict.TryGetValue("generatorName", out var generatorName))
                        {
                            result.WorldType = generatorName?.ToString() ?? "default";
                        }
                        
                        if (dataDict.TryGetValue("Time", out var time))
                        {
                            if (time is long l) result.Time = l;
                            else if (time is int i) result.Time = i;
                        }
                        
                        if (dataDict.TryGetValue("DayTime", out var dayTime))
                        {
                            if (dayTime is long l) result.DayTime = l;
                            else if (dayTime is int i) result.DayTime = i;
                        }
                        
                        if (dataDict.TryGetValue("raining", out var raining))
                        {
                            result.Raining = raining is bool b && b;
                        }
                        
                        if (dataDict.TryGetValue("thundering", out var thundering))
                        {
                            result.Thundering = thundering is bool b && b;
                        }
                    }
                }
                
                try
                {
                    var playerDataFile = Path.Combine(savePath, "level.dat_old");
                    if (!File.Exists(playerDataFile))
                    {
                        playerDataFile = levelDatPath;
                    }
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            App.LogError($"读取存档信息失败：{savePath}", ex);
        }
        
        return result;
    }
    
    private Dictionary<string, object> ParseNBT(FileStream fs)
    {
        var result = new Dictionary<string, object>();
        try
        {
            using var reader = new BinaryReader(fs, Encoding.UTF8, true);
            
            byte tagType = reader.ReadByte();
            if (tagType == 10)
            {
                ushort nameLength = reader.ReadUInt16();
                byte[] nameBytes = reader.ReadBytes(nameLength);
                
                var compound = ReadCompound(reader);
                foreach (var kvp in compound)
                {
                    result[kvp.Key] = kvp.Value;
                }
            }
        }
        catch
        {
        }
        
        return result;
    }
    
    private Dictionary<string, object> ReadCompound(BinaryReader reader)
    {
        var result = new Dictionary<string, object>();
        
        while (true)
        {
            byte tagType = reader.ReadByte();
            if (tagType == 0)
                break;
            
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
