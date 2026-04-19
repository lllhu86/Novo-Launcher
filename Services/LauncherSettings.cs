using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MinecraftLauncher.Services;

public class LauncherSettings
{
    public string IsolationMode { get; set; } = "mod_dev";
    public bool IsAutoMemory { get; set; } = true;
    public int CustomMemoryMB { get; set; } = 4096;
    public bool OptimizeMemory { get; set; } = true;
    public string SkinType { get; set; } = "random";
    public string CustomSkinPath { get; set; } = "";
    public string JavaArgs { get; set; } = "";
    public string GameArgs { get; set; } = "";
    public string PreLaunchCommand { get; set; } = "";
    public bool DisableJavaWrapper { get; set; }
    public bool UseDedicatedGPU { get; set; }
    public bool UseMultiThreadDownload { get; set; }
    public string JavaPath { get; set; } = "";

    private static readonly string SettingsFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NovoLauncher", "launcher_settings.json");

    private static LauncherSettings? _instance;
    private static readonly object _lock = new();

    public static LauncherSettings Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    _instance ??= Load();
                }
            }
            return _instance;
        }
    }

    public static LauncherSettings Load()
    {
        try
        {
            if (File.Exists(SettingsFilePath))
            {
                var json = File.ReadAllText(SettingsFilePath);
                return JsonSerializer.Deserialize<LauncherSettings>(json) ?? new LauncherSettings();
            }
        }
        catch
        {
        }
        return new LauncherSettings();
    }

    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(SettingsFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsFilePath, json);
        }
        catch
        {
        }
    }

    public int GetEffectiveMemoryMB()
    {
        if (IsAutoMemory)
        {
            var hardware = new HardwareService();
            var recommendation = hardware.GetRecommendedSettings();
            return recommendation.RecommendedMemoryMB;
        }
        return CustomMemoryMB;
    }
}
