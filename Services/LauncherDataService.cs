using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using MinecraftLauncher.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MinecraftLauncher.Services;

public class LauncherDataService
{
    private readonly string _gameDir;
    private readonly string _baseMinecraftDir;
    
    public event Action<string>? LogMessage;
    
    public LauncherDataService(string gameDir)
    {
        _gameDir = gameDir;
        _baseMinecraftDir = Path.Combine(gameDir, ".minecraft");
    }
    
    #region 存档管理
    
    public List<SaveInfo> GetAllSaves()
    {
        var saves = new List<SaveInfo>();
        
        try
        {
            var versionsDir = Path.Combine(_baseMinecraftDir, "versions");
            if (Directory.Exists(versionsDir))
            {
                foreach (var versionDir in Directory.GetDirectories(versionsDir))
                {
                    var versionName = Path.GetFileName(versionDir);
                    var savesDir = Path.Combine(versionDir, "saves");
                    
                    if (Directory.Exists(savesDir))
                    {
                        foreach (var saveDir in Directory.GetDirectories(savesDir))
                        {
                            var saveInfo = GetSaveInfo(saveDir, versionName);
                            if (saveInfo != null)
                            {
                                saves.Add(saveInfo);
                            }
                        }
                    }
                }
            }
            
            var sharedSavesDir = Path.Combine(_baseMinecraftDir, "saves");
            if (Directory.Exists(sharedSavesDir))
            {
                foreach (var saveDir in Directory.GetDirectories(sharedSavesDir))
                {
                    var saveInfo = GetSaveInfo(saveDir, "共享");
                    if (saveInfo != null && !saves.Any(s => s.Path == saveInfo.Path))
                    {
                        saves.Add(saveInfo);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            LogMessage?.Invoke($"获取存档列表失败: {ex.Message}");
        }
        
        return saves.OrderByDescending(s => s.LastPlayed).ToList();
    }
    
    private SaveInfo? GetSaveInfo(string savePath, string version)
    {
        try
        {
            var levelDatPath = Path.Combine(savePath, "level.dat");
            if (!File.Exists(levelDatPath))
                return null;
            
            var saveName = Path.GetFileName(savePath);
            var directoryInfo = new DirectoryInfo(savePath);
            var sizeBytes = GetDirectorySize(savePath);
            
            var saveInfo = new SaveInfo
            {
                Name = saveName,
                Path = savePath,
                Version = version,
                LastPlayed = directoryInfo.LastWriteTime,
                SizeBytes = sizeBytes,
                SizeDisplay = FormatFileSize(sizeBytes)
            };
            
            try
            {
                var nbtData = NbtParser.ParseGzipFile(levelDatPath);
                
                if (nbtData.TryGetValue("Data", out var data) && data is Dictionary<string, object> dataDict)
                {
                    if (dataDict.TryGetValue("GameType", out var gameType))
                    {
                        saveInfo.GameMode = gameType switch
                        {
                            0 => "生存模式",
                            1 => "创造模式",
                            2 => "冒险模式",
                            3 => "旁观模式",
                            _ => "未知"
                        };
                    }
                    
                    if (dataDict.TryGetValue("Difficulty", out var difficulty))
                    {
                        saveInfo.Difficulty = difficulty switch
                        {
                            0 => "和平",
                            1 => "简单",
                            2 => "普通",
                            3 => "困难",
                            _ => "未知"
                        };
                    }
                    
                    if (dataDict.TryGetValue("RandomSeed", out var seed))
                    {
                        saveInfo.Seed = seed?.ToString() ?? "";
                    }
                    
                    if (dataDict.TryGetValue("allowCommands", out var allowCommands))
                    {
                        saveInfo.HasCheats = allowCommands is bool b && b;
                    }
                    
                    if (dataDict.TryGetValue("LevelName", out var levelName))
                    {
                        saveInfo.DisplayName = levelName?.ToString() ?? saveName;
                    }
                    
                    if (dataDict.TryGetValue("generatorName", out var generatorName))
                    {
                        saveInfo.WorldType = generatorName?.ToString() switch
                        {
                            "default" => "标准",
                            "flat" => "超平坦",
                            "large_biomes" => "巨型生物群系",
                            "amplified" => "放大化",
                            "single_biome_surface" => "单一生物群系",
                            _ => generatorName?.ToString() ?? "标准"
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                App.LogError($"解析存档 level.dat 失败：{savePath}", ex);
            }
            
            var datapacksDir = Path.Combine(savePath, "datapacks");
            if (Directory.Exists(datapacksDir))
            {
                saveInfo.DatapackCount = Directory.GetFiles(datapacksDir, "*.zip").Length + 
                                         Directory.GetDirectories(datapacksDir).Length;
            }
            
            return saveInfo;
        }
        catch (Exception ex)
        {
            return null;
        }
    }
    
    private long GetDirectorySize(string path)
    {
        try
        {
            return Directory.GetFiles(path, "*", SearchOption.AllDirectories)
                .Sum(f => new FileInfo(f).Length);
        }
        catch (Exception ex)
        {
            App.LogError($"获取目录大小失败：{path}", ex);
            return 0;
        }
    }
    
    private string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        int order = 0;
        double size = bytes;
        
        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }
        
        return $"{size:0.##} {sizes[order]}";
    }
    
    #endregion
    
    #region 模组管理
    
    public List<ModInfo> GetModsForVersion(string version)
    {
        var mods = new List<ModInfo>();
        
        try
        {
            var versionDir = Path.Combine(_baseMinecraftDir, "versions", version);
            var modsDir = Path.Combine(versionDir, "mods");
            
            if (Directory.Exists(modsDir))
            {
                foreach (var modFile in Directory.GetFiles(modsDir, "*.jar"))
                {
                    var modInfo = GetModInfo(modFile, true);
                    if (modInfo != null)
                    {
                        mods.Add(modInfo);
                    }
                }
            }
            
            var disabledModsDir = Path.Combine(versionDir, "disabled_mods");
            if (Directory.Exists(disabledModsDir))
            {
                foreach (var modFile in Directory.GetFiles(disabledModsDir, "*.jar"))
                {
                    var modInfo = GetModInfo(modFile, false);
                    if (modInfo != null)
                    {
                        mods.Add(modInfo);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            LogMessage?.Invoke($"获取模组列表失败: {ex.Message}");
        }
        
        return mods;
    }
    
    private ModInfo? GetModInfo(string modPath, bool enabled)
    {
        try
        {
            var fileInfo = new FileInfo(modPath);
            var modInfo = new ModInfo
            {
                FileName = fileInfo.Name,
                FilePath = modPath,
                Enabled = enabled,
                SizeBytes = fileInfo.Length,
                SizeDisplay = FormatFileSize(fileInfo.Length),
                LastModified = fileInfo.LastWriteTime
            };
            
            using var archive = ZipFile.OpenRead(modPath);
            
            var mcmodInfo = archive.GetEntry("mcmod.info");
            var fabricModJson = archive.GetEntry("fabric.mod.json");
            var quiltModJson = archive.GetEntry("quilt.mod.json");
            var neoforgeModsToml = archive.GetEntry("META-INF/neoforge.mods.toml");
            var forgeModsToml = archive.GetEntry("META-INF/mods.toml");
            
            if (fabricModJson != null)
            {
                modInfo.ModLoader = "Fabric";
                using var stream = fabricModJson.Open();
                using var reader = new StreamReader(stream);
                var json = reader.ReadToEnd();
                
                try
                {
                    var fabricMod = JsonConvert.DeserializeObject<dynamic>(json);
                    modInfo.Name = fabricMod?.name?.ToString() ?? fileInfo.Name;
                    modInfo.Version = fabricMod?.version?.ToString() ?? "";
                    modInfo.Description = fabricMod?.description?.ToString() ?? "";
                    modInfo.Author = fabricMod?.contact?.authors?.ToString() ?? "";
                    modInfo.ModId = fabricMod?.id?.ToString() ?? "";
                    
                    if (fabricMod?.depends != null)
                    {
                        foreach (var dep in fabricMod.depends)
                        {
                            modInfo.Dependencies.Add(new ModDependency
                            {
                                ModId = dep.Name,
                                Required = true
                            });
                        }
                    }
                }
                catch { }
            }
            else if (quiltModJson != null)
            {
                modInfo.ModLoader = "Quilt";
                using var stream = quiltModJson.Open();
                using var reader = new StreamReader(stream);
                var json = reader.ReadToEnd();
                
                try
                {
                    var quiltMod = JsonConvert.DeserializeObject<dynamic>(json);
                    modInfo.Name = quiltMod?.quilt_loader?.metadata?.name?.ToString() ?? fileInfo.Name;
                    modInfo.Version = quiltMod?.quilt_loader?.version?.ToString() ?? "";
                    modInfo.ModId = quiltMod?.quilt_loader?.id?.ToString() ?? "";
                }
                catch { }
            }
            else if (neoforgeModsToml != null)
            {
                modInfo.ModLoader = "NeoForge";
                ParseForgeModsToml(neoforgeModsToml, modInfo);
            }
            else if (forgeModsToml != null)
            {
                modInfo.ModLoader = "Forge";
                ParseForgeModsToml(forgeModsToml, modInfo);
            }
            else if (mcmodInfo != null)
            {
                modInfo.ModLoader = "Forge (Legacy)";
                using var stream = mcmodInfo.Open();
                using var reader = new StreamReader(stream);
                var content = reader.ReadToEnd();
                
                try
                {
                    var mcmod = JsonConvert.DeserializeObject(content);
                    if (mcmod is JArray arr && arr.Count > 0)
                    {
                        var firstMod = arr[0] as JObject;
                        if (firstMod != null)
                        {
                            modInfo.Name = firstMod["name"]?.ToString() ?? fileInfo.Name;
                            modInfo.Version = firstMod["version"]?.ToString() ?? "";
                            modInfo.Description = firstMod["description"]?.ToString() ?? "";
                            modInfo.ModId = firstMod["modid"]?.ToString() ?? "";
                        }
                    }
                }
                catch { }
            }
            
            if (string.IsNullOrEmpty(modInfo.Name))
            {
                modInfo.Name = Path.GetFileNameWithoutExtension(fileInfo.Name);
            }
            
            return modInfo;
        }
        catch (Exception ex)
        {
            App.LogError($"解析模组信息失败：{modPath}", ex);
            return null;
        }
    }
    
    private void ParseForgeModsToml(ZipArchiveEntry entry, ModInfo modInfo)
    {
        try
        {
            using var stream = entry.Open();
            using var reader = new StreamReader(stream);
            var content = reader.ReadToEnd();
            
            var nameMatch = Regex.Match(content, @"displayName\s*=\s*""([^""]+)""");
            if (nameMatch.Success)
            {
                modInfo.Name = nameMatch.Groups[1].Value;
            }
            
            var versionMatch = Regex.Match(content, @"version\s*=\s*""([^""]+)""");
            if (versionMatch.Success)
            {
                modInfo.Version = versionMatch.Groups[1].Value;
            }
            
            var modIdMatch = Regex.Match(content, @"modId\s*=\s*""([^""]+)""");
            if (modIdMatch.Success)
            {
                modInfo.ModId = modIdMatch.Groups[1].Value;
            }
            
            var descMatch = Regex.Match(content, @"description\s*=\s*''([^']+)''");
            if (descMatch.Success)
            {
                modInfo.Description = descMatch.Groups[1].Value;
            }
        }
        catch { }
    }
    
    public List<ModConflictInfo> DetectModConflicts(string version)
    {
        var conflicts = new List<ModConflictInfo>();
        var mods = GetModsForVersion(version);
        
        var modIds = mods.Where(m => !string.IsNullOrEmpty(m.ModId)).Select(m => m.ModId).ToList();
        var duplicateIds = modIds.GroupBy(id => id).Where(g => g.Count() > 1).Select(g => g.Key);
        
        foreach (var dupId in duplicateIds)
        {
            var conflictingMods = mods.Where(m => m.ModId == dupId).ToList();
            conflicts.Add(new ModConflictInfo
            {
                ConflictType = "重复模组",
                Description = $"检测到重复安装的模组 ID: {dupId}",
                AffectedMods = conflictingMods.Select(m => m.Name).ToList(),
                Severity = "高",
                Solution = "请删除重复的模组文件，只保留一个版本。"
            });
        }
        
        var allDependencies = new List<string>();
        foreach (var mod in mods)
        {
            allDependencies.AddRange(mod.Dependencies.Where(d => d.Required).Select(d => d.ModId));
        }
        
        var missingDeps = allDependencies.Distinct()
            .Where(dep => !modIds.Contains(dep) && !IsBuiltinMod(dep))
            .ToList();
        
        foreach (var missingDep in missingDeps)
        {
            var needingMods = mods.Where(m => m.Dependencies.Any(d => d.ModId == missingDep && d.Required)).ToList();
            conflicts.Add(new ModConflictInfo
            {
                ConflictType = "缺失依赖",
                Description = $"缺少必要的前置模组: {missingDep}",
                AffectedMods = needingMods.Select(m => m.Name).ToList(),
                Severity = "高",
                Solution = $"请安装 {missingDep} 模组，或移除需要它的模组。"
            });
        }
        
        return conflicts;
    }
    
    private bool IsBuiltinMod(string modId)
    {
        var builtinMods = new[] { "minecraft", "java", "fabric", "fabricloader", "fabric-api", 
            "forge", "neoforge", "quilt_loader", "quilted_fabric_api" };
        return builtinMods.Contains(modId.ToLower());
    }
    
    #endregion
    
    #region 版本配置
    
    public List<GameVersionInfo> GetInstalledVersions()
    {
        var versions = new List<GameVersionInfo>();
        
        try
        {
            var versionsDir = Path.Combine(_baseMinecraftDir, "versions");
            if (!Directory.Exists(versionsDir))
                return versions;
            
            foreach (var versionDir in Directory.GetDirectories(versionsDir))
            {
                var versionName = Path.GetFileName(versionDir);
                var versionJsonPath = Path.Combine(versionDir, $"{versionName}.json");
                
                if (File.Exists(versionJsonPath))
                {
                    var versionInfo = GetVersionInfo(versionDir, versionName);
                    if (versionInfo != null)
                    {
                        versions.Add(versionInfo);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            LogMessage?.Invoke($"获取版本列表失败: {ex.Message}");
        }
        
        return versions.OrderByDescending(v => v.LastModified).ToList();
    }
    
    private GameVersionInfo? GetVersionInfo(string versionDir, string versionName)
    {
        try
        {
            var versionJsonPath = Path.Combine(versionDir, $"{versionName}.json");
            var json = File.ReadAllText(versionJsonPath);
            var versionDetail = JsonConvert.DeserializeObject<VersionDetail>(json);
            
            var dirInfo = new DirectoryInfo(versionDir);
            var versionInfo = new GameVersionInfo
            {
                Id = versionName,
                Type = versionDetail?.Type ?? "unknown",
                JavaVersion = versionDetail?.JavaVersion?.MajorVersion ?? 8,
                LastModified = dirInfo.LastWriteTime,
                MainClass = versionDetail?.MainClass ?? ""
            };
            
            versionInfo.ModLoader = DetectModLoader(versionDetail, versionDir);
            
            var clientJarPath = Path.Combine(versionDir, $"{versionName}.jar");
            if (File.Exists(clientJarPath))
            {
                versionInfo.SizeBytes = new FileInfo(clientJarPath).Length;
                versionInfo.SizeDisplay = FormatFileSize(versionInfo.SizeBytes);
            }
            
            var modsDir = Path.Combine(versionDir, "mods");
            if (Directory.Exists(modsDir))
            {
                versionInfo.ModCount = Directory.GetFiles(modsDir, "*.jar").Length;
            }
            
            return versionInfo;
        }
        catch (Exception ex)
        {
            App.LogError($"获取版本信息失败：{versionName}", ex);
            return null;
        }
    }
    
    private string DetectModLoader(VersionDetail? versionDetail, string versionDir)
    {
        if (versionDetail == null || versionDetail.Libraries == null)
            return "原版";
        
        foreach (var lib in versionDetail.Libraries)
        {
            var name = lib.Name?.ToLower() ?? "";
            
            if (name.Contains("neoforge"))
                return "NeoForge";
            if (name.Contains("forge") && !name.Contains("neoforge"))
                return "Forge";
            if (name.Contains("fabricloader"))
                return "Fabric";
            if (name.Contains("quilt_loader"))
                return "Quilt";
            if (name.Contains("optifine"))
                return "OptiFine";
        }
        
        if (Directory.Exists(Path.Combine(versionDir, "mods")))
        {
            var mods = Directory.GetFiles(Path.Combine(versionDir, "mods"), "*.jar");
            foreach (var mod in mods)
            {
                try
                {
                    using var archive = ZipFile.OpenRead(mod);
                    if (archive.GetEntry("fabric.mod.json") != null)
                        return "Fabric";
                    if (archive.GetEntry("quilt.mod.json") != null)
                        return "Quilt";
                    if (archive.GetEntry("META-INF/mods.toml") != null)
                        return "Forge";
                    if (archive.GetEntry("META-INF/neoforge.mods.toml") != null)
                        return "NeoForge";
                }
                catch { }
            }
        }
        
        return "原版";
    }
    
    #endregion
    
    #region 日志分析
    
    public LauncherLogInfo GetLatestLauncherLog()
    {
        var logInfo = new LauncherLogInfo();
        
        try
        {
            var logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
            if (!Directory.Exists(logDir))
                return logInfo;
            
            var logFiles = Directory.GetFiles(logDir, "*.log")
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.LastWriteTime)
                .ToList();
            
            if (logFiles.Count == 0)
                return logInfo;
            
            var latestLog = logFiles[0];
            logInfo.LogFilePath = latestLog.FullName;
            logInfo.LogTime = latestLog.LastWriteTime;
            
            var lines = File.ReadAllLines(latestLog.FullName);
            logInfo.TotalLines = lines.Length;
            
            foreach (var line in lines)
            {
                if (line.Contains("[ERROR]"))
                {
                    logInfo.ErrorCount++;
                    logInfo.ErrorLines.Add(line);
                }
                else if (line.Contains("[WARN]"))
                {
                    logInfo.WarningCount++;
                }
                else if (line.Contains("[INFO]"))
                {
                    logInfo.InfoCount++;
                }
            }
            
            logInfo.RecentLines = lines.TakeLast(100).ToList();
        }
        catch (Exception ex)
        {
            LogMessage?.Invoke($"读取启动器日志失败: {ex.Message}");
        }
        
        return logInfo;
    }
    
    public GameCrashReport? AnalyzeGameCrash(string version)
    {
        try
        {
            var versionDir = Path.Combine(_baseMinecraftDir, "versions", version);
            var crashReportsDir = Path.Combine(versionDir, "crash-reports");
            
            if (!Directory.Exists(crashReportsDir))
                return null;
            
            var crashFiles = Directory.GetFiles(crashReportsDir, "crash-*.txt")
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.LastWriteTime)
                .ToList();
            
            if (crashFiles.Count == 0)
                return null;
            
            var latestCrash = crashFiles[0];
            var content = File.ReadAllText(latestCrash.FullName);
            
            var report = new GameCrashReport
            {
                FilePath = latestCrash.FullName,
                CrashTime = latestCrash.LastWriteTime,
                RawContent = content
            };
            
            var descMatch = Regex.Match(content, @"Description: ([^\n]+)");
            if (descMatch.Success)
            {
                report.Description = descMatch.Groups[1].Value.Trim();
            }
            
            var exceptionMatch = Regex.Match(content, @"([a-zA-Z.]+Exception|[a-zA-Z.]+Error): ([^\n]+)");
            if (exceptionMatch.Success)
            {
                report.ExceptionType = exceptionMatch.Groups[1].Value;
                report.ExceptionMessage = exceptionMatch.Groups[2].Value.Trim();
            }
            
            if (content.Contains("java.lang.OutOfMemoryError"))
            {
                report.ErrorType = CrashErrorType.OutOfMemory;
                report.SuggestedFix = "内存不足！建议增加游戏内存分配，或减少使用的模组数量。";
            }
            else if (content.Contains("NoSuchMethodError") || content.Contains("NoClassDefFoundError"))
            {
                report.ErrorType = CrashErrorType.ModIncompatibility;
                report.SuggestedFix = "检测到模组兼容性问题！可能是模组版本不匹配或缺失依赖。";
            }
            else if (content.Contains("Missing Mods"))
            {
                report.ErrorType = CrashErrorType.MissingDependency;
                report.SuggestedFix = "缺少必要的前置模组！请检查并安装缺失的依赖模组。";
            }
            else if (content.Contains("Duplicate Mod"))
            {
                report.ErrorType = CrashErrorType.DuplicateMod;
                report.SuggestedFix = "检测到重复安装的模组！请删除重复的模组文件。";
            }
            else if (content.Contains("Shader") || content.Contains("GL"))
            {
                report.ErrorType = CrashErrorType.GraphicsIssue;
                report.SuggestedFix = "检测到图形相关问题！建议更新显卡驱动或调整视频设置。";
            }
            else
            {
                report.ErrorType = CrashErrorType.Unknown;
                report.SuggestedFix = "未知错误类型，建议查看完整崩溃报告或咨询社区。";
            }
            
            return report;
        }
        catch (Exception ex)
        {
            App.LogError($"分析崩溃报告失败", ex);
            return null;
        }
    }
    
    #endregion
}

#region 数据模型

public class SaveInfo
{
    public string Name { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Path { get; set; } = "";
    public string Version { get; set; } = "";
    public DateTime LastPlayed { get; set; }
    public long SizeBytes { get; set; }
    public string SizeDisplay { get; set; } = "";
    public string GameMode { get; set; } = "未知";
    public string Difficulty { get; set; } = "未知";
    public string Seed { get; set; } = "";
    public string WorldType { get; set; } = "标准";
    public bool HasCheats { get; set; }
    public int DatapackCount { get; set; }
    
    public string DisplayLastPlayed
    {
        get
        {
            var diff = DateTime.Now - LastPlayed;
            if (diff.TotalMinutes < 1) return "刚刚";
            if (diff.TotalHours < 1) return $"{(int)diff.TotalMinutes}分钟前";
            if (diff.TotalDays < 1) return $"{(int)diff.TotalHours}小时前";
            if (diff.TotalDays < 7) return $"{(int)diff.TotalDays}天前";
            return LastPlayed.ToString("yyyy-MM-dd");
        }
    }
}

public class ModInfo
{
    public string Name { get; set; } = "";
    public string FileName { get; set; } = "";
    public string FilePath { get; set; } = "";
    public string Version { get; set; } = "";
    public string ModId { get; set; } = "";
    public string Description { get; set; } = "";
    public string Author { get; set; } = "";
    public string ModLoader { get; set; } = "Unknown";
    public bool Enabled { get; set; }
    public long SizeBytes { get; set; }
    public string SizeDisplay { get; set; } = "";
    public DateTime LastModified { get; set; }
    public List<ModDependency> Dependencies { get; set; } = new();
}

public class ModDependency
{
    public string ModId { get; set; } = "";
    public string Version { get; set; } = "";
    public bool Required { get; set; }
}

public class ModConflictInfo
{
    public string ConflictType { get; set; } = "";
    public string Description { get; set; } = "";
    public List<string> AffectedMods { get; set; } = new();
    public string Severity { get; set; } = "";
    public string Solution { get; set; } = "";
}

public class GameVersionInfo
{
    public string Id { get; set; } = "";
    public string Type { get; set; } = "";
    public string ModLoader { get; set; } = "原版";
    public int JavaVersion { get; set; }
    public DateTime LastModified { get; set; }
    public string MainClass { get; set; } = "";
    public long SizeBytes { get; set; }
    public string SizeDisplay { get; set; } = "";
    public int ModCount { get; set; }
}

public class LauncherLogInfo
{
    public string LogFilePath { get; set; } = "";
    public DateTime LogTime { get; set; }
    public int TotalLines { get; set; }
    public int ErrorCount { get; set; }
    public int WarningCount { get; set; }
    public int InfoCount { get; set; }
    public List<string> ErrorLines { get; set; } = new();
    public List<string> RecentLines { get; set; } = new();
}

public class GameCrashReport
{
    public string FilePath { get; set; } = "";
    public DateTime CrashTime { get; set; }
    public string Description { get; set; } = "";
    public string ExceptionType { get; set; } = "";
    public string ExceptionMessage { get; set; } = "";
    public CrashErrorType ErrorType { get; set; }
    public string SuggestedFix { get; set; } = "";
    public string RawContent { get; set; } = "";
}

public enum CrashErrorType
{
    Unknown,
    OutOfMemory,
    ModIncompatibility,
    MissingDependency,
    DuplicateMod,
    GraphicsIssue,
    JavaVersionMismatch,
    WorldCorruption
}

#endregion
