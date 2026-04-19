using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;

namespace MinecraftLauncher.Services;

public class LaunchOptimizationService : IDisposable
{
    private readonly string _gameDir;
    private readonly string _cacheDir;
    private readonly string _cacheFile;
    private LaunchCache _cache;
    private bool _disposed;

    public event Action<string>? OptimizationProgress;

    public LaunchOptimizationService(string gameDir)
    {
        _gameDir = gameDir;
        _cacheDir = Path.Combine(gameDir, ".minecraft", "launch_cache");
        _cacheFile = Path.Combine(_cacheDir, "cache.json");
        
        Directory.CreateDirectory(_cacheDir);
        _cache = LoadCache();
    }

    private LaunchCache LoadCache()
    {
        if (File.Exists(_cacheFile))
        {
            try
            {
                var json = File.ReadAllText(_cacheFile);
                return JsonSerializer.Deserialize<LaunchCache>(json) ?? new LaunchCache();
            }
            catch
            {
                return new LaunchCache();
            }
        }
        return new LaunchCache();
    }

    private void SaveCache()
    {
        try
        {
            var json = JsonSerializer.Serialize(_cache, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_cacheFile, json);
        }
        catch (Exception ex)
        {
            App.LogError("保存启动缓存失败", ex);
        }
    }

    public bool ShouldExtractNatives(string versionId, string librariesDir)
    {
        var key = $"natives_{versionId}";
        
        if (!_cache.NativesCache.TryGetValue(key, out var cachedHash))
        {
            OptimizationProgress?.Invoke("首次启动，需要解压 natives 文件");
            return true;
        }

        var currentHash = CalculateNativesHash(librariesDir, versionId);
        if (currentHash != cachedHash)
        {
            OptimizationProgress?.Invoke("检测到库文件变化，重新解压 natives");
            return true;
        }

        OptimizationProgress?.Invoke("使用缓存的 natives 文件，跳过解压");
        return false;
    }

    public void MarkNativesExtracted(string versionId, string librariesDir)
    {
        var key = $"natives_{versionId}";
        var hash = CalculateNativesHash(librariesDir, versionId);
        _cache.NativesCache[key] = hash;
        _cache.LastOptimization = DateTime.Now;
        SaveCache();
    }

    private string CalculateNativesHash(string librariesDir, string versionId)
    {
        using var sha256 = SHA256.Create();
        var hashBuilder = new System.Text.StringBuilder();
        
        var nativesPattern = Path.Combine(librariesDir, "**", "*natives*.jar");
        var files = Directory.GetFiles(librariesDir, "*natives*.jar", SearchOption.AllDirectories);
        
        Array.Sort(files);
        
        foreach (var file in files)
        {
            var fileInfo = new FileInfo(file);
            hashBuilder.Append($"{file}:{fileInfo.Length}:{fileInfo.LastWriteTimeUtc.Ticks};");
        }
        
        var bytes = System.Text.Encoding.UTF8.GetBytes(hashBuilder.ToString());
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToBase64String(hash);
    }

    public void CacheJavaVersion(string javaPath, int majorVersion)
    {
        var key = $"java_{javaPath}";
        _cache.JavaVersionCache[key] = new JavaVersionInfo
        {
            MajorVersion = majorVersion,
            CachedTime = DateTime.Now
        };
        SaveCache();
    }

    public int? GetCachedJavaVersion(string javaPath)
    {
        var key = $"java_{javaPath}";
        
        if (_cache.JavaVersionCache.TryGetValue(key, out var info))
        {
            var javaFileInfo = new FileInfo(javaPath);
            if (javaFileInfo.LastWriteTimeUtc < info.CachedTime)
            {
                OptimizationProgress?.Invoke($"使用缓存的 Java 版本: {info.MajorVersion}");
                return info.MajorVersion;
            }
        }
        
        return null;
    }

    public void CacheClasspath(string versionId, string classpath)
    {
        var key = $"classpath_{versionId}";
        _cache.ClasspathCache[key] = new ClasspathInfo
        {
            Classpath = classpath,
            CachedTime = DateTime.Now
        };
        SaveCache();
    }

    public string? GetCachedClasspath(string versionId, string librariesDir, string clientJar)
    {
        var key = $"classpath_{versionId}";
        
        if (!_cache.ClasspathCache.TryGetValue(key, out var info))
            return null;

        if (!ValidateClasspath(info.Classpath, librariesDir, clientJar))
        {
            OptimizationProgress?.Invoke("检测到库文件变化，重新构建 classpath");
            return null;
        }

        OptimizationProgress?.Invoke("使用缓存的 classpath");
        return info.Classpath;
    }

    private bool ValidateClasspath(string classpath, string librariesDir, string clientJar)
    {
        var paths = classpath.Split(Path.PathSeparator);
        
        foreach (var path in paths)
        {
            if (!File.Exists(path))
                return false;
        }
        
        return true;
    }

    public async Task PreloadCriticalFilesAsync(string versionId, string librariesDir, string clientJar)
    {
        OptimizationProgress?.Invoke("开始预加载关键文件...");
        
        var criticalFiles = new ConcurrentBag<string>();
        criticalFiles.Add(clientJar);
        
        var tasks = new List<Task>();
        
        var coreLibraries = new[]
        {
            "lwjgl",
            "minecraft",
            "log4j",
            "gson",
            "guava",
            "commons"
        };
        
        foreach (var lib in coreLibraries)
        {
            var files = Directory.GetFiles(librariesDir, $"*{lib}*.jar", SearchOption.AllDirectories);
            foreach (var file in files)
            {
                criticalFiles.Add(file);
            }
        }
        
        var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = 4 };
        
        await Task.Run(() =>
        {
            Parallel.ForEach(criticalFiles, parallelOptions, file =>
            {
                try
                {
                    using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, FileOptions.SequentialScan);
                    var buffer = new byte[8192];
                    var totalRead = 0L;
                    var fileSize = stream.Length;
                    
                    while (totalRead < fileSize)
                    {
                        var toRead = Math.Min(buffer.Length, (int)(fileSize - totalRead));
                        stream.Read(buffer, 0, toRead);
                        totalRead += toRead;
                    }
                }
                catch { }
            });
        });
        
        OptimizationProgress?.Invoke($"预加载完成，已缓存 {criticalFiles.Count} 个关键文件");
    }

    public LaunchStatistics GetStatistics()
    {
        return new LaunchStatistics
        {
            TotalLaunches = _cache.TotalLaunches,
            AverageLaunchTime = _cache.AverageLaunchTime,
            LastOptimization = _cache.LastOptimization,
            CacheHitRate = CalculateCacheHitRate()
        };
    }

    public void RecordLaunchTime(double seconds)
    {
        _cache.TotalLaunches++;
        
        if (_cache.TotalLaunches == 1)
        {
            _cache.AverageLaunchTime = seconds;
        }
        else
        {
            _cache.AverageLaunchTime = (_cache.AverageLaunchTime * (_cache.TotalLaunches - 1) + seconds) / _cache.TotalLaunches;
        }
        
        SaveCache();
    }

    private double CalculateCacheHitRate()
    {
        if (_cache.TotalLaunches == 0)
            return 0;
        
        var hits = _cache.NativesCache.Count + _cache.JavaVersionCache.Count + _cache.ClasspathCache.Count;
        return (double)hits / _cache.TotalLaunches;
    }

    public void ClearCache()
    {
        _cache = new LaunchCache();
        SaveCache();
        OptimizationProgress?.Invoke("启动缓存已清除");
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            SaveCache();
        }
    }
}

public class LaunchCache
{
    public Dictionary<string, string> NativesCache { get; set; } = new();
    public Dictionary<string, JavaVersionInfo> JavaVersionCache { get; set; } = new();
    public Dictionary<string, ClasspathInfo> ClasspathCache { get; set; } = new();
    public int TotalLaunches { get; set; }
    public double AverageLaunchTime { get; set; }
    public DateTime LastOptimization { get; set; }
}

public class JavaVersionInfo
{
    public int MajorVersion { get; set; }
    public DateTime CachedTime { get; set; }
}

public class ClasspathInfo
{
    public string Classpath { get; set; } = "";
    public DateTime CachedTime { get; set; }
}

public class LaunchStatistics
{
    public int TotalLaunches { get; set; }
    public double AverageLaunchTime { get; set; }
    public DateTime LastOptimization { get; set; }
    public double CacheHitRate { get; set; }
}
