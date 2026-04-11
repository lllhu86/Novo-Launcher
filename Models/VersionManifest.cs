using Newtonsoft.Json;

namespace MinecraftLauncher.Models;

public class VersionManifest
{
    [JsonProperty("latest")]
    public LatestVersions? Latest { get; set; }

    [JsonProperty("versions")]
    public List<VersionInfo>? Versions { get; set; }
}

public class LatestVersions
{
    [JsonProperty("release")]
    public string? Release { get; set; }

    [JsonProperty("snapshot")]
    public string? Snapshot { get; set; }
}

public class VersionInfo
{
    [JsonProperty("id")]
    public string? Id { get; set; }

    [JsonProperty("type")]
    public string? Type { get; set; }

    [JsonProperty("url")]
    public string? Url { get; set; }

    [JsonProperty("time")]
    public DateTime Time { get; set; }

    [JsonProperty("releaseTime")]
    public DateTime ReleaseTime { get; set; }

    [JsonProperty("sha1")]
    public string? Sha1 { get; set; }

    [JsonProperty("complianceLevel")]
    public int ComplianceLevel { get; set; }

    public string DisplayName
    {
        get
        {
            if (string.IsNullOrEmpty(Id))
                return "Unknown";
            
            // 直接返回版本号，如 "1.20.1"、"24w14a" 等
            return Id;
        }
    }
}
