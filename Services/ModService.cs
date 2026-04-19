using System.IO;
using System.Net.Http;
using MinecraftLauncher.Models;
using Newtonsoft.Json;

namespace MinecraftLauncher.Services;

public class ModService : IDisposable
{
    public class ModDep
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public string ProjectId { get; set; }
        public string Version { get; set; }
    }
    
    private HttpClient? _httpClient;
    private const string MODRINTH_API = "https://api.modrinth.com/v2";
    private bool _disposed;

    public event EventHandler<DownloadProgressEventArgs>? DownloadProgressChanged;

    public ModService()
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
        };
        _httpClient = new HttpClient(handler);
        _httpClient.Timeout = TimeSpan.FromMinutes(10);
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "NMCL/1.0");
    }

    /// <summary>
    /// 搜索 Modrinth 上的 MOD
    /// </summary>
    public async Task<List<ModrinthMod>> SearchModsAsync(string query, string[] loaders = null, string[] versions = null, int offset = 0, int limit = 20)
    {
        if (_httpClient == null)
            throw new Exception("HttpClient 未初始化");

        try
        {
            var url = $"{MODRINTH_API}/search?query={Uri.EscapeDataString(query)}&limit={limit}&offset={offset}";
            
            if (loaders != null && loaders.Length > 0)
            {
                var loadersJson = JsonConvert.SerializeObject(loaders);
                url += $"&loaders={Uri.EscapeDataString(loadersJson)}";
            }
            
            if (versions != null && versions.Length > 0)
            {
                var versionsJson = JsonConvert.SerializeObject(versions);
                url += $"&versions={Uri.EscapeDataString(versionsJson)}";
            }

            App.LogInfo($"搜索 MOD (Modrinth): {url}");
            
            var json = await _httpClient.GetStringAsync(url);
            var result = JsonConvert.DeserializeObject<ModrinthSearchResult>(json);
            
            return result?.Hits ?? new List<ModrinthMod>();
        }
        catch (Exception ex)
        {
            App.LogError($"搜索 MOD 失败：{ex.Message}", ex);
            return new List<ModrinthMod>();
        }
    }

    /// <summary>
    /// 获取热门 MOD
    /// </summary>
    public async Task<List<ModrinthMod>> GetPopularModsAsync(int count = 20)
    {
        if (_httpClient == null)
            throw new Exception("HttpClient 未初始化");

        try
        {
            var url = $"{MODRINTH_API}/search?query=&limit={count}&index=downloads";
            App.LogInfo($"获取热门 MOD (Modrinth): {url}");
            
            var json = await _httpClient.GetStringAsync(url);
            var result = JsonConvert.DeserializeObject<ModrinthSearchResult>(json);
            
            return result?.Hits ?? new List<ModrinthMod>();
        }
        catch (Exception ex)
        {
            App.LogError($"获取热门 MOD 失败：{ex.Message}", ex);
            return new List<ModrinthMod>();
        }
    }

    /// <summary>
    /// 获取 MOD 的详细信息（包括版本文件列表）
    /// </summary>
    public async Task<ModrinthMod> GetModDetailsAsync(string projectId)
    {
        if (_httpClient == null)
            throw new Exception("HttpClient 未初始化");

        try
        {
            var url = $"{MODRINTH_API}/project/{projectId}/version";
            App.LogInfo($"获取 MOD 版本信息：{url}");
            
            var json = await _httpClient.GetStringAsync(url);
            var versionObjects = JsonConvert.DeserializeObject<List<VersionObject>>(json);
            
            var versionFiles = new List<VersionFile>();
            if (versionObjects != null)
            {
                foreach (var version in versionObjects)
                {
                    if (version.Files != null)
                    {
                        foreach (var file in version.Files)
                        {
                            versionFiles.Add(new VersionFile
                            {
                                Version = version.Version,
                                VersionType = version.VersionType,
                                Loaders = version.Loaders,
                                GameVersions = version.GameVersions,
                                Url = file.Url,
                                Filename = file.Filename,
                                Size = file.Size
                            });
                        }
                    }
                }
            }
            
            var modDetails = new ModrinthMod
            {
                ProjectId = projectId,
                VersionFiles = versionFiles
            };
            
            App.LogInfo($"获取到 {versionFiles.Count} 个版本文件");
            return modDetails;
        }
        catch (Exception ex)
        {
            App.LogError($"获取 MOD 详细信息失败：{ex.Message}", ex);
            return new ModrinthMod { ProjectId = projectId, VersionFiles = new List<VersionFile>() };
        }
    }

    public async Task<string> GetModDescriptionAsync(string projectId)
    {
        if (_httpClient == null)
            throw new Exception("HttpClient 未初始化");

        try
        {
            var url = $"{MODRINTH_API}/project/{projectId}";
            App.LogInfo($"获取 MOD 描述：{url}");
            
            var json = await _httpClient.GetStringAsync(url);
            var projectInfo = JsonConvert.DeserializeObject<ModrinthProjectInfo>(json);
            
            return projectInfo?.Body ?? string.Empty;
        }
        catch (Exception ex)
        {
            App.LogError($"获取 MOD 描述失败：{ex.Message}", ex);
            return string.Empty;
        }
    }

    public async Task<List<ModDep>> GetModDependenciesAsync(string projectId)
    {
        if (_httpClient == null)
            throw new Exception("HttpClient 未初始化");

        var dependencies = new List<ModDep>();

        try
        {
            var url = $"{MODRINTH_API}/project/{projectId}/version";
            App.LogInfo($"获取 MOD 依赖信息：{url}");
            
            var json = await _httpClient.GetStringAsync(url);
            var versionObjects = JsonConvert.DeserializeObject<List<VersionObject>>(json);
            
            if (versionObjects != null && versionObjects.Count > 0)
            {
                var latestVersion = versionObjects[0];
                if (latestVersion.Dependencies != null)
                {
                    foreach (var dep in latestVersion.Dependencies)
                    {
                        if (dep is Dictionary<string, object> depDict)
                        {
                            var depType = depDict.ContainsKey("dependency_type") ? depDict["dependency_type"].ToString() : "required";
                            if (depType == "required" || depType == "embedded" || depType == "incompatible")
                            {
                                var depProjectId = depDict.ContainsKey("project_id") ? depDict["project_id"]?.ToString() : null;
                                var depVersion = depDict.ContainsKey("version_id") ? depDict["version_id"]?.ToString() : null;
                                
                                if (!string.IsNullOrEmpty(depProjectId))
                                {
                                    var depInfo = await GetModInfoByIdAsync(depProjectId);
                                    if (depInfo != null)
                                    {
                                        dependencies.Add(new ModDep
                                        {
                                            ProjectId = depProjectId,
                                            Name = depInfo.Title ?? depInfo.Slug ?? "未知 MOD",
                                            Description = $"需要 {depInfo.Title} 才能正常运行",
                                            Version = depVersion ?? ""
                                        });
                                    }
                                }
                            }
                        }
                    }
                }
            }
            
            App.LogInfo($"获取到 {dependencies.Count} 个依赖");
        }
        catch (Exception ex)
        {
            App.LogError($"获取 MOD 依赖失败：{ex.Message}", ex);
        }

        return dependencies;
    }

    private async Task<ModrinthProjectInfo> GetModInfoByIdAsync(string projectId)
    {
        if (_httpClient == null)
            return null;

        try
        {
            var url = $"{MODRINTH_API}/project/{projectId}";
            var json = await _httpClient.GetStringAsync(url);
            return JsonConvert.DeserializeObject<ModrinthProjectInfo>(json);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 下载 MOD 文件
    /// </summary>
    public async Task DownloadModFileAsync(string downloadUrl, string savePath, CancellationToken cancellationToken = default)
    {
        if (_httpClient == null || string.IsNullOrEmpty(downloadUrl))
            throw new Exception("无法下载 MOD");

        try
        {
            var directory = Path.GetDirectoryName(savePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            App.LogInfo($"正在下载 MOD: {Path.GetFileName(downloadUrl)}");

            using var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? 0;
            var downloadedBytes = 0L;

            using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var fileStream = new FileStream(savePath, FileMode.Create, FileAccess.Write, FileShare.None);

            var buffer = new byte[8192];
            int bytesRead;

            while ((bytesRead = await contentStream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                downloadedBytes += bytesRead;

                DownloadProgressChanged?.Invoke(this, new DownloadProgressEventArgs
                {
                    TotalBytes = totalBytes,
                    DownloadedBytes = downloadedBytes,
                    FileName = Path.GetFileName(savePath)
                });
            }

            App.LogInfo($"MOD 下载完成：{savePath}");
        }
        catch (Exception ex)
        {
            App.LogError($"下载 MOD 失败：{ex.Message}", ex);
            throw;
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _httpClient?.Dispose();
        }
    }
}

/// <summary>
/// Modrinth MOD 模型
/// </summary>
public class ModrinthMod
{
    [JsonProperty("project_id")]
    public string ProjectId { get; set; }

    [JsonProperty("project_type")]
    public string ProjectType { get; set; }

    [JsonProperty("slug")]
    public string Slug { get; set; }

    [JsonProperty("author")]
    public string Author { get; set; }

    [JsonProperty("title")]
    public string Title { get; set; }

    [JsonProperty("description")]
    public string Description { get; set; }

    [JsonProperty("display_name")]
    public string DisplayName { get; set; }

    [JsonProperty("downloads")]
    public int Downloads { get; set; }

    [JsonProperty("icon_url")]
    public string IconUrl { get; set; }

    [JsonProperty("latest_version")]
    public string LatestVersion { get; set; }

    [JsonProperty("version_files")]
    public List<VersionFile> VersionFiles { get; set; }
}

public class VersionObject
{
    [JsonProperty("id")]
    public string Id { get; set; }

    [JsonProperty("project_id")]
    public string ProjectId { get; set; }

    [JsonProperty("author_id")]
    public string AuthorId { get; set; }

    [JsonProperty("name")]
    public string Name { get; set; }

    [JsonProperty("version_number")]
    public string Version { get; set; }

    [JsonProperty("changelog")]
    public string Changelog { get; set; }

    [JsonProperty("changelog_url")]
    public string ChangelogUrl { get; set; }

    [JsonProperty("version_type")]
    public string VersionType { get; set; }

    [JsonProperty("loaders")]
    public List<string> Loaders { get; set; }

    [JsonProperty("game_versions")]
    public List<string> GameVersions { get; set; }

    [JsonProperty("files")]
    public List<VersionFileInfo> Files { get; set; }

    [JsonProperty("dependencies")]
    public List<object> Dependencies { get; set; }
}

public class VersionFileInfo
{
    [JsonProperty("hashes")]
    public FileHashes Hashes { get; set; }

    [JsonProperty("url")]
    public string Url { get; set; }

    [JsonProperty("filename")]
    public string Filename { get; set; }

    [JsonProperty("primary")]
    public bool Primary { get; set; }

    [JsonProperty("size")]
    public long Size { get; set; }

    [JsonProperty("file_type")]
    public string FileType { get; set; }
}

public class FileHashes
{
    [JsonProperty("sha512")]
    public string Sha512 { get; set; }

    [JsonProperty("sha1")]
    public string Sha1 { get; set; }
}

public class VersionFile
{
    [JsonProperty("version")]
    public string Version { get; set; }

    [JsonProperty("version_type")]
    public string VersionType { get; set; }

    [JsonProperty("loaders")]
    public List<string> Loaders { get; set; }

    [JsonProperty("game_versions")]
    public List<string> GameVersions { get; set; }

    [JsonProperty("url")]
    public string Url { get; set; }

    [JsonProperty("filename")]
    public string Filename { get; set; }

    [JsonProperty("size")]
    public long Size { get; set; }
}

public class ModrinthProjectInfo
{
    [JsonProperty("id")]
    public string Id { get; set; }

    [JsonProperty("slug")]
    public string Slug { get; set; }

    [JsonProperty("title")]
    public string Title { get; set; }

    [JsonProperty("description")]
    public string Description { get; set; }

    [JsonProperty("body")]
    public string Body { get; set; }

    [JsonProperty("author")]
    public string Author { get; set; }

    [JsonProperty("downloads")]
    public int Downloads { get; set; }

    [JsonProperty("icon_url")]
    public string IconUrl { get; set; }

    [JsonProperty("project_type")]
    public string ProjectType { get; set; }
}

public class ModrinthSearchResult
{
    [JsonProperty("hits")]
    public List<ModrinthMod> Hits { get; set; }

    [JsonProperty("total_hits")]
    public int TotalHits { get; set; }
}
