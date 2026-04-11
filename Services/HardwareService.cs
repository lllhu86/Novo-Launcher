using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;

namespace MinecraftLauncher.Services;

public class HardwareInfo
{
    public string CpuName { get; set; } = string.Empty;
    public int CpuCores { get; set; }
    public long TotalMemoryGB { get; set; }
    public string GpuName { get; set; } = string.Empty;
    public string GpuMemory { get; set; } = string.Empty;
    public string OsVersion { get; set; } = string.Empty;
}

public class GameSettingsRecommendation
{
    public int RecommendedMemoryMB { get; set; }
    public int RenderDistance { get; set; }
    public string GraphicsMode { get; set; } = "fast";
    public string JvmArgs { get; set; } = string.Empty;
    public string Explanation { get; set; } = string.Empty;
}

public class HardwareService
{
    public HardwareInfo GetHardwareInfo()
    {
        var info = new HardwareInfo();

        try
        {
            info.CpuName = GetCpuName();
            info.CpuCores = Environment.ProcessorCount;
            info.TotalMemoryGB = GetTotalMemoryGB();
            info.GpuName = GetGpuName();
            info.GpuMemory = GetGpuMemory();
            info.OsVersion = RuntimeInformation.OSDescription;
        }
        catch (Exception ex)
        {
            App.LogError("获取硬件信息失败", ex);
        }

        return info;
    }

    private string GetCpuName()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_Processor");
            foreach (var obj in searcher.Get())
            {
                return obj["Name"]?.ToString()?.Trim() ?? "Unknown CPU";
            }
        }
        catch { }

        return "Unknown CPU";
    }

    private long GetTotalMemoryGB()
    {
        try
        {
            var memStatus = new MEMORYSTATUSEX();
            if (GlobalMemoryStatusEx(memStatus))
            {
                return (long)(memStatus.ullTotalPhys / (1024 * 1024 * 1024));
            }
        }
        catch { }

        return 8;
    }

    private string GetGpuName()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController");
            foreach (var obj in searcher.Get())
            {
                var name = obj["Name"]?.ToString();
                if (!string.IsNullOrEmpty(name))
                {
                    return name;
                }
            }
        }
        catch { }

        return "Unknown GPU";
    }

    private string GetGpuMemory()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT AdapterRAM FROM Win32_VideoController");
            foreach (var obj in searcher.Get())
            {
                var ram = obj["AdapterRAM"];
                if (ram != null)
                {
                    var bytes = Convert.ToInt64(ram);
                    var gb = bytes / (1024.0 * 1024.0 * 1024.0);
                    return $"{gb:F1} GB";
                }
            }
        }
        catch { }

        return "Unknown";
    }

    public GameSettingsRecommendation GetRecommendedSettings(string preference = "balanced")
    {
        var hardware = GetHardwareInfo();
        var recommendation = new GameSettingsRecommendation();

        var totalMem = hardware.TotalMemoryGB;
        var cpuCores = hardware.CpuCores;

        recommendation.RecommendedMemoryMB = totalMem switch
        {
            <= 4 => 2048,
            <= 8 => 4096,
            <= 16 => 6144,
            <= 32 => 8192,
            _ => 12288
        };

        recommendation.RenderDistance = (totalMem, cpuCores) switch
        {
            (<= 4, _) => 8,
            (<= 8, <= 4) => 10,
            (<= 8, _) => 12,
            (<= 16, <= 4) => 12,
            (<= 16, _) => 16,
            _ => 20
        };

        recommendation.GraphicsMode = preference switch
        {
            "performance" => "fast",
            "quality" => "fancy",
            _ => "fancy"
        };

        recommendation.JvmArgs = GenerateJvmArgs(recommendation.RecommendedMemoryMB, cpuCores);

        recommendation.Explanation = $"基于您的硬件配置（{hardware.CpuName}，{hardware.TotalMemoryGB}GB 内存，{hardware.GpuName}），" +
            $"推荐分配 {recommendation.RecommendedMemoryMB / 1024.0:F1}GB 内存给游戏，" +
            $"渲染距离设置为 {recommendation.RenderDistance} 区块。";

        return recommendation;
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

    [StructLayout(LayoutKind.Sequential)]
    private class MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;

        public MEMORYSTATUSEX()
        {
            dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>();
        }
    }

    [return: MarshalAs(UnmanagedType.Bool)]
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);
}
