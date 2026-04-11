using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using MinecraftLauncher.Models;

namespace MinecraftLauncher.Services;

public class LaunchService
{
    private readonly string _gameDir;
    private int _javaMajorVersion = 0;

    public LaunchService(string gameDir)
    {
        _gameDir = gameDir;
    }

    public Process? LaunchGame(VersionDetail version, Account account, string? javaPath = null, string? worldName = null)
    {
        App.LogInfo($"正在准备启动版本：{version.Id}");
        
        if (version.MainClass == null)
            throw new Exception("无法找到主类");

        var java = FindJava(javaPath);
        if (java == null)
        {
            App.LogError("未找到 Java 运行时");
            throw new Exception("未找到 Java 运行时，请确保已安装 Java 并配置环境变量");
        }
        
        _javaMajorVersion = GetJavaMajorVersion(java);
        App.LogInfo($"检测到 Java 版本: {_javaMajorVersion}");
        App.LogInfo($"找到 Java: {java}");

        var baseGameDir = Path.Combine(_gameDir, ".minecraft");
        var versionDir = Path.Combine(baseGameDir, "versions", version.Id ?? "unknown");
        var clientJar = Path.Combine(versionDir, $"{version.Id}.jar");
        var librariesDir = Path.Combine(baseGameDir, "libraries");
        var nativesDir = Path.Combine(versionDir, "natives");
        
        // 版本隔离：每个版本有独立的游戏目录
        var isolatedGameDir = versionDir;

        if (!File.Exists(clientJar))
        {
            App.LogError($"客户端文件不存在：{clientJar}");
            throw new Exception($"客户端文件不存在：{clientJar}");
        }

        App.LogInfo("正在解压 natives 文件...");
        ExtractNatives(version, librariesDir, nativesDir);

        var classpath = BuildClasspath(version, librariesDir, clientJar);
        var jvmArgs = BuildJvmArgs(version, nativesDir, account, isolatedGameDir, classpath);
        var gameArgs = BuildGameArgs(version, account, isolatedGameDir, classpath, worldName);

        var fullArgs = $"{jvmArgs} {version.MainClass} {gameArgs}";
        App.LogInfo($"启动参数: {fullArgs}");

        var startInfo = new ProcessStartInfo
        {
            FileName = java,
            Arguments = fullArgs,
            WorkingDirectory = isolatedGameDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = false,
            RedirectStandardInput = true
        };

        var process = new Process { StartInfo = startInfo };
        process.OutputDataReceived += (sender, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                App.LogInfo($"[游戏输出] {e.Data}");
            }
        };
        process.ErrorDataReceived += (sender, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                App.LogInfo($"[游戏错误] {e.Data}");
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        App.LogInfo($"游戏进程已启动 (PID: {process.Id})");
        return process;
    }

    private void ExtractNatives(VersionDetail version, string librariesDir, string nativesDir)
    {
        if (Directory.Exists(nativesDir))
        {
            try
            {
                Directory.Delete(nativesDir, true);
            }
            catch { }
        }
        Directory.CreateDirectory(nativesDir);

        if (version.Libraries == null) return;

        foreach (var library in version.Libraries)
        {
            if (!IsNativesLibrary(library)) continue;

            // 处理所有 natives 变体
            var classifiers = library.Downloads?.Classifiers;
            if (classifiers == null) continue;

            var nativesList = new[] 
            { 
                classifiers.NativesWindows,
                classifiers.NativesWindows64,
                classifiers.NativesWindows32,
                classifiers.NativesLinux,
                classifiers.NativesMacos
            };

            foreach (var natives in nativesList)
            {
                if (natives?.Path == null) continue;

                var nativePath = Path.Combine(librariesDir, natives.Path.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(nativePath))
                {
                    App.LogInfo($"库文件缺失: {nativePath}");
                    continue;
                }

                try
                {
                    using var archive = ZipFile.OpenRead(nativePath);
                    foreach (var entry in archive.Entries)
                    {
                        if (entry.FullName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
                            entry.FullName.EndsWith(".so", StringComparison.OrdinalIgnoreCase) ||
                            entry.FullName.EndsWith(".dylib", StringComparison.OrdinalIgnoreCase) ||
                            entry.FullName.EndsWith(".jnilib", StringComparison.OrdinalIgnoreCase))
                        {
                            var destPath = Path.Combine(nativesDir, entry.Name);
                            entry.ExtractToFile(destPath, true);
                            App.LogInfo($"解压: {entry.Name}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    App.LogError($"解压 natives 失败: {nativePath}", ex);
                }
            }
        }
    }

    private bool IsNativesLibrary(Library library)
    {
        if (library.Natives != null && library.Natives.Count > 0)
            return true;
        
        if (library.Downloads?.Classifiers != null)
            return true;

        return false;
    }

    private string? FindJava(string? customPath)
    {
        if (!string.IsNullOrEmpty(customPath) && File.Exists(customPath))
            return customPath;

        var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (!string.IsNullOrEmpty(javaHome))
        {
            var javaExe = Path.Combine(javaHome, "bin", "java.exe");
            if (File.Exists(javaExe))
                return javaExe;
        }

        var pathVar = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(pathVar))
        {
            var paths = pathVar.Split(Path.PathSeparator);
            foreach (var path in paths)
            {
                var javaExe = Path.Combine(path, "java.exe");
                if (File.Exists(javaExe))
                    return javaExe;
            }
        }

        var commonPaths = new[]
        {
            @"C:\Program Files\Java\jdk-17\bin\java.exe",
            @"C:\Program Files\Java\jdk-21\bin\java.exe",
            @"C:\Program Files\Java\jdk-25\bin\java.exe",
            @"C:\Program Files\Java\jre-17\bin\java.exe",
            @"C:\Program Files\Java\jre-21\bin\java.exe",
            @"C:\Program Files (x86)\Java\jre-17\bin\java.exe",
            @"C:\Program Files (x86)\Java\jre-1.8\bin\java.exe"
        };

        foreach (var path in commonPaths)
        {
            if (File.Exists(path))
                return path;
        }

        return null;
    }

    private string BuildClasspath(VersionDetail version, string librariesDir, string clientJar)
    {
        var classpath = new List<string>();

        if (version.Libraries != null)
        {
            foreach (var library in version.Libraries)
            {
                if (!IsAllowed(library)) continue;

                var added = false;
                
                if (library.Downloads?.Artifact?.Path != null)
                {
                    var normalizedPath = library.Downloads.Artifact.Path.Replace('/', Path.DirectorySeparatorChar);
                    var libraryPath = Path.Combine(librariesDir, normalizedPath);
                    if (File.Exists(libraryPath))
                    {
                        classpath.Add(libraryPath);
                        added = true;
                    }
                    else
                    {
                        App.LogInfo($"库文件缺失: {libraryPath}");
                    }
                }
                
                if (!added)
                {
                    var name = library.Name;
                    if (!string.IsNullOrEmpty(name))
                    {
                        var parts = name.Split(':');
                        if (parts.Length >= 3)
                        {
                            var groupPath = parts[0].Replace('.', Path.DirectorySeparatorChar);
                            var artifactId = parts[1];
                            var libVersion = parts[2];
                            var expectedPath = Path.Combine(librariesDir, groupPath, artifactId, libVersion, $"{artifactId}-{libVersion}.jar");
                            if (File.Exists(expectedPath))
                            {
                                classpath.Add(expectedPath);
                            }
                        }
                    }
                }
            }
        }

        classpath.Add(clientJar);
        return string.Join(Path.PathSeparator, classpath);
    }

    private bool IsAllowed(Library library)
    {
        if (library.Rules == null || library.Rules.Count == 0)
            return true;

        foreach (var rule in library.Rules)
        {
            var allowed = rule.Action == "allow";
            var applies = rule.Os == null || 
                         (rule.Os.Name == "windows" && OperatingSystem.IsWindows()) ||
                         (rule.Os.Name == "osx" && OperatingSystem.IsMacOS()) ||
                         (rule.Os.Name == "linux" && OperatingSystem.IsLinux());

            if (applies)
                return allowed;
        }

        return false;
    }

    private string BuildJvmArgs(VersionDetail version, string nativesDir, Account account, string isolatedGameDir, string classpath)
    {
        var args = new List<string>
        {
            "-Xmx2G",
            "-Xms512M",
            $"-Djava.library.path=\"{nativesDir}\"",
            "-Dminecraft.launcher.brand=Novo-Launcher",
            "-Dminecraft.launcher.version=1.0",
            $"-Dos.name=\"Windows 10\"",
            "-Dos.version=10.0"
        };
        
        args.Add("-XX:+UseG1GC");
        args.Add("-XX:+UnlockExperimentalVMOptions");
        args.Add("-XX:G1NewSizePercent=20");
        args.Add("-XX:G1ReservePercent=20");
        args.Add("-XX:MaxGCPauseMillis=50");
        args.Add("-XX:G1HeapRegionSize=32M");

        if (version.Arguments?.Jvm != null)
        {
            foreach (var jvmArg in version.Arguments.Jvm)
            {
                if (jvmArg is string str)
                {
                    var processed = ProcessArg(str, version, account, isolatedGameDir, classpath);
                    
                    if (!IsJvmArgCompatible(processed))
                    {
                        App.LogInfo($"跳过不兼容的 JVM 参数: {processed}");
                        continue;
                    }
                    
                    if (!args.Contains(processed))
                    {
                        args.Add(processed);
                    }
                }
            }
        }

        return string.Join(" ", args);
    }

    private bool IsJvmArgCompatible(string arg)
    {
        if (string.IsNullOrWhiteSpace(arg)) return false;
        
        if (arg.Contains("--sun-misc-unsafe-memory-access") && _javaMajorVersion < 21)
            return false;
        
        if (arg.Contains("--enable-native-access") && _javaMajorVersion < 17)
            return false;
        
        if (arg.StartsWith("-XX:+UseZGC") && _javaMajorVersion < 15)
            return false;
        
        if (arg.StartsWith("-XX:+UseShenandoahGC") && _javaMajorVersion < 12)
            return false;
        
        return true;
    }

    private int GetJavaMajorVersion(string javaPath)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = javaPath,
                Arguments = "-version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null) return 8;

            var output = process.StandardError.ReadToEnd();
            process.WaitForExit();

            var match = Regex.Match(output, @"version\s+""(?:(\d+)\.)?(?:(\d+)\.)?(\d+)");
            if (match.Success)
            {
                var major = match.Groups[1].Value;
                if (!string.IsNullOrEmpty(major) && int.TryParse(major, out var majorVersion))
                {
                    if (majorVersion == 1)
                    {
                        var minor = match.Groups[2].Value;
                        if (int.TryParse(minor, out var minorVersion))
                            return minorVersion;
                    }
                    return majorVersion;
                }
            }

            var legacyMatch = Regex.Match(output, @"version\s+""1\.(\d+)");
            if (legacyMatch.Success && int.TryParse(legacyMatch.Groups[1].Value, out var legacyVersion))
            {
                return legacyVersion;
            }

            return 8;
        }
        catch
        {
            return 8;
        }
    }

    private int GetMinecraftMajorVersion(string? versionId)
    {
        if (string.IsNullOrEmpty(versionId))
            return 0;

        var match = System.Text.RegularExpressions.Regex.Match(versionId, @"^(\d+)\.(\d+)");
        if (match.Success)
        {
            var major = int.Parse(match.Groups[1].Value);
            var minor = int.Parse(match.Groups[2].Value);
            
            if (major == 1)
            {
                return minor;
            }
            return major * 10 + minor;
        }

        return 0;
    }

    private string BuildGameArgs(VersionDetail version, Account account, string isolatedGameDir, string classpath, string? worldName = null)
    {
        var args = new List<string>();

        if (version.Arguments?.Game != null)
        {
            foreach (var gameArg in version.Arguments.Game)
            {
                if (gameArg is string str)
                {
                    var processed = ProcessArg(str, version, account, isolatedGameDir, classpath);
                    args.Add(processed);
                }
            }
        }
        else if (!string.IsNullOrEmpty(version.MinecraftArguments))
        {
            var processed = ProcessArg(version.MinecraftArguments, version, account, isolatedGameDir, classpath);
            args.AddRange(processed.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }

        args.Add("--language");
        args.Add("zh_cn");
        
        if (!string.IsNullOrEmpty(worldName))
        {
            var mcVersion = GetMinecraftMajorVersion(version.Id);
            if (mcVersion >= 20)
            {
                args.Add("--quickPlaySingleplayer");
                args.Add($"\"{worldName}\"");
                App.LogInfo($"将直接加载存档 (QuickPlay): {worldName}");
            }
            else
            {
                App.LogInfo($"Minecraft {version.Id} 不支持 QuickPlay，游戏将启动到主菜单");
            }
        }

        return string.Join(" ", args);
    }

    private string ProcessArg(string arg, VersionDetail version, Account account, string isolatedGameDir, string classpath)
    {
        var baseGameDir = Path.Combine(_gameDir, ".minecraft");
        var assetsDir = Path.Combine(baseGameDir, "assets");
        var versionDir = isolatedGameDir;

        return arg
            .Replace("${classpath}", $"\"{classpath}\"")
            .Replace("${auth_player_name}", account.Name)
            .Replace("${version_name}", version.Id ?? "")
            .Replace("${game_directory}", $"\"{isolatedGameDir}\"")
            .Replace("${assets_root}", $"\"{assetsDir}\"")
            .Replace("${assets_index_name}", version.Assets ?? "")
            .Replace("${auth_uuid}", account.Uuid)
            .Replace("${auth_access_token}", account.AccessToken)
            .Replace("${user_type}", account.PlayerType)
            .Replace("${version_type}", version.Type ?? "release")
            .Replace("${natives_directory}", $"\"{Path.Combine(versionDir, "natives")}\"")
            .Replace("${launcher_name}", "Novo-Launcher")
            .Replace("${launcher_version}", "1.0")
            .Replace("${classpath_separator}", Path.PathSeparator.ToString())
            .Replace("${clientid}", "")
            .Replace("${auth_xuid}", "");
    }
}
