using Newtonsoft.Json;
using System.Collections.Generic;

namespace MinecraftLauncher.Models;

public class VersionDetail
{
    [JsonProperty("id")]
    public string? Id { get; set; }

    [JsonProperty("type")]
    public string? Type { get; set; }

    [JsonProperty("mainClass")]
    public string? MainClass { get; set; }

    [JsonProperty("minecraftArguments")]
    public string? MinecraftArguments { get; set; }

    [JsonProperty("arguments")]
    public Arguments? Arguments { get; set; }

    [JsonProperty("libraries")]
    public List<Library>? Libraries { get; set; }

    [JsonProperty("assetIndex")]
    public AssetIndex? AssetIndex { get; set; }

    [JsonProperty("assets")]
    public string? Assets { get; set; }

    [JsonProperty("downloads")]
    public Downloads? Downloads { get; set; }

    [JsonProperty("javaVersion")]
    public JavaVersion? JavaVersion { get; set; }
}

public class Arguments
{
    [JsonProperty("game")]
    public List<object>? Game { get; set; }

    [JsonProperty("jvm")]
    public List<object>? Jvm { get; set; }
}

public class Library
{
    [JsonProperty("name")]
    public string? Name { get; set; }

    [JsonProperty("downloads")]
    public LibraryDownloads? Downloads { get; set; }

    [JsonProperty("rules")]
    public List<Rule>? Rules { get; set; }

    [JsonProperty("natives")]
    public Dictionary<string, string>? Natives { get; set; }

    [JsonProperty("extract")]
    public Extract? Extract { get; set; }
}

public class Extract
{
    [JsonProperty("exclude")]
    public List<string>? Exclude { get; set; }
}

public class LibraryDownloads
{
    [JsonProperty("artifact")]
    public Artifact? Artifact { get; set; }

    [JsonProperty("classifiers")]
    public Classifiers? Classifiers { get; set; }
}

public class Classifiers
{
    [JsonProperty("natives-windows")]
    public Artifact? NativesWindows { get; set; }

    [JsonProperty("natives-windows-64")]
    public Artifact? NativesWindows64 { get; set; }

    [JsonProperty("natives-windows-32")]
    public Artifact? NativesWindows32 { get; set; }

    [JsonProperty("natives-linux")]
    public Artifact? NativesLinux { get; set; }

    [JsonProperty("natives-macos")]
    public Artifact? NativesMacos { get; set; }
}

public class Artifact
{
    [JsonProperty("url")]
    public string? Url { get; set; }

    [JsonProperty("sha1")]
    public string? Sha1 { get; set; }

    [JsonProperty("size")]
    public long Size { get; set; }

    [JsonProperty("path")]
    public string? Path { get; set; }
}

public class Rule
{
    [JsonProperty("action")]
    public string? Action { get; set; }

    [JsonProperty("os")]
    public OsRule? Os { get; set; }
}

public class OsRule
{
    [JsonProperty("name")]
    public string? Name { get; set; }

    [JsonProperty("arch")]
    public string? Arch { get; set; }
}

public class AssetIndex
{
    [JsonProperty("id")]
    public string? Id { get; set; }

    [JsonProperty("sha1")]
    public string? Sha1 { get; set; }

    [JsonProperty("size")]
    public long Size { get; set; }

    [JsonProperty("url")]
    public string? Url { get; set; }

    [JsonProperty("totalSize")]
    public long TotalSize { get; set; }
}

public class AssetIndexContent
{
    [JsonProperty("objects")]
    public Dictionary<string, AssetObject>? Objects { get; set; }
}

public class AssetObject
{
    [JsonProperty("hash")]
    public string Hash { get; set; } = "";

    [JsonProperty("size")]
    public long Size { get; set; }
}

public class Downloads
{
    [JsonProperty("client")]
    public DownloadInfo? Client { get; set; }

    [JsonProperty("server")]
    public DownloadInfo? Server { get; set; }
}

public class DownloadInfo
{
    [JsonProperty("sha1")]
    public string? Sha1 { get; set; }

    [JsonProperty("size")]
    public long Size { get; set; }

    [JsonProperty("url")]
    public string? Url { get; set; }
}

public class JavaVersion
{
    [JsonProperty("component")]
    public string? Component { get; set; }

    [JsonProperty("majorVersion")]
    public int MajorVersion { get; set; }
}
