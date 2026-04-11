using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using MinecraftLauncher.Models;
using Newtonsoft.Json;

namespace MinecraftLauncher.Services;

public class ModpackService
{
    private HttpClient? _httpClient;

    private static readonly string[] MODRINTH_MIRRORS = new[]
    {
        "https://api.modrinth.com/v2",
        "https://api.modrinth.haugnsite.xyz/v2",
        "https://mirror.haagma.se/modrinth/v2"
    };

    private static readonly string[] CURSEFORGE_MIRRORS = new[]
    {
        "https://api.curseforge.com/v1",
        "https://cfproxy.terracow.top/v1"
    };

    private const int MaxRetries = 5;
    private static readonly TimeSpan RequestDelay = TimeSpan.FromMilliseconds(500);

    private string _currentSource = "modrinth";
    private string _currentApiKey = "";

    public event EventHandler<DownloadProgressEventArgs>? DownloadProgressChanged;

    public ModpackService()
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate,
            AllowAutoRedirect = true,
            ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
        };
        _httpClient = new HttpClient(handler);
        _httpClient.Timeout = TimeSpan.FromMinutes(10);
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "NMCL/1.0");
    }

    public void SetApiKey(string key)
    {
        _currentApiKey = key;
        if (_httpClient != null && !string.IsNullOrEmpty(key))
        {
            _httpClient.DefaultRequestHeaders.Remove("x-api-key");
            _httpClient.DefaultRequestHeaders.Add("x-api-key", key);
        }
    }

    public void SetSource(string source)
    {
        _currentSource = source.ToLower();
    }

    public async Task<List<ModpackInfo>> GetFeaturedModpacksAsync(int count = 20)
    {
        if (_currentSource == "curseforge")
        {
            return await GetCurseForgeFeaturedModpacksAsync(count);
        }
        else
        {
            return await GetModrinthFeaturedModpacksAsync(count);
        }
    }

    public async Task<List<ModpackInfo>> SearchModpacksAsync(string query, int offset = 0, int limit = 20)
    {
        if (_currentSource == "curseforge")
        {
            return await SearchCurseForgeModpacksAsync(query, offset, limit);
        }
        else
        {
            return await SearchModrinthModpacksAsync(query, offset, limit);
        }
    }

    #region Modrinth API

    private async Task<List<ModpackInfo>> GetModrinthFeaturedModpacksAsync(int count)
    {
        if (_httpClient == null)
            throw new Exception("HttpClient 未初始化");

        for (int mirrorIndex = 0; mirrorIndex < MODRINTH_MIRRORS.Length; mirrorIndex++)
        {
            int retryCount = 0;
            while (retryCount < MaxRetries)
            {
                try
                {
                    var apiBase = MODRINTH_MIRRORS[mirrorIndex];

                    var searchUrl = $"{apiBase}/search?query=&limit={count}&project_type=modpack&index=downloads";
                    App.LogInfo($"获取热门整合包 (Modrinth): {searchUrl}");

                    await Task.Delay(RequestDelay);
                    var json = await _httpClient.GetStringAsync(searchUrl);
                    var result = JsonConvert.DeserializeObject<ModpackSearchResult>(json);

                    if (result?.Hits != null)
                    {
                        foreach (var hit in result.Hits)
                        {
                            hit.Source = "modrinth";
                        }
                        App.LogInfo($"获取到 {result.Hits.Count} 个热门整合包");
                        return result.Hits;
                    }
                }
                catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    retryCount++;
                    var waitTime = Math.Min(2000 * retryCount, 15000);
                    App.LogInfo($"遇到 429 限流，等待 {waitTime/1000} 秒后重试 ({retryCount}/{MaxRetries})");
                    await Task.Delay(waitTime);
                }
                catch (Exception ex)
                {
                    App.LogError($"获取热门整合包失败 (镜像 {mirrorIndex + 1}): {ex.Message}");
                    if (mirrorIndex < MODRINTH_MIRRORS.Length - 1)
                    {
                        App.LogInfo($"尝试下一个镜像...");
                        break;
                    }
                    retryCount++;
                    if (retryCount < MaxRetries)
                    {
                        await Task.Delay(1000 * retryCount);
                    }
                }
            }
        }

        App.LogInfo("所有 Modrinth 镜像都无法获取，返回空列表");
        return new List<ModpackInfo>();
    }

    private async Task<List<ModpackInfo>> SearchModrinthModpacksAsync(string query, int offset, int limit)
    {
        if (_httpClient == null)
            throw new Exception("HttpClient 未初始化");

        for (int mirrorIndex = 0; mirrorIndex < MODRINTH_MIRRORS.Length; mirrorIndex++)
        {
            int retryCount = 0;
            while (retryCount < MaxRetries)
            {
                try
                {
                    var apiBase = MODRINTH_MIRRORS[mirrorIndex];
                    var url = $"{apiBase}/search?query={Uri.EscapeDataString(query)}&limit={limit}&offset={offset}&project_type=modpack";

                    App.LogInfo($"搜索整合包 (Modrinth): {url}");

                    await Task.Delay(RequestDelay);
                    var json = await _httpClient.GetStringAsync(url);
                    var result = JsonConvert.DeserializeObject<ModpackSearchResult>(json);

                    if (result?.Hits != null)
                    {
                        foreach (var hit in result.Hits)
                        {
                            hit.Source = "modrinth";
                        }
                        App.LogInfo($"搜索到 {result.Hits.Count} 个整合包");
                        return result.Hits;
                    }
                }
                catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    retryCount++;
                    var waitTime = Math.Min(2000 * retryCount, 15000);
                    App.LogInfo($"遇到 429 限流，等待 {waitTime/1000} 秒后重试 ({retryCount}/{MaxRetries})");
                    await Task.Delay(waitTime);
                }
                catch (Exception ex)
                {
                    App.LogError($"搜索整合包失败 (镜像 {mirrorIndex + 1}): {ex.Message}");
                    if (mirrorIndex < MODRINTH_MIRRORS.Length - 1)
                    {
                        break;
                    }
                    retryCount++;
                    if (retryCount < MaxRetries)
                    {
                        await Task.Delay(1000 * retryCount);
                    }
                }
            }
        }

        return new List<ModpackInfo>();
    }

    public async Task<ModpackDetails?> GetModrinthModpackDetailsAsync(string projectId)
    {
        if (_httpClient == null)
            throw new Exception("HttpClient 未初始化");

        for (int mirrorIndex = 0; mirrorIndex < MODRINTH_MIRRORS.Length; mirrorIndex++)
        {
            int retryCount = 0;
            while (retryCount < MaxRetries)
            {
                try
                {
                    var apiBase = MODRINTH_MIRRORS[mirrorIndex];
                    var projectUrl = $"{apiBase}/project/{projectId}";

                    App.LogInfo($"获取整合包详情 (Modrinth): {projectUrl}");

                    await Task.Delay(RequestDelay);
                    var projectJson = await _httpClient.GetStringAsync(projectUrl);
                    var project = JsonConvert.DeserializeObject<ModpackProjectInfo>(projectJson);

                    if (project == null)
                        return null;

                    var versionsUrl = $"{apiBase}/project/{projectId}/version";
                    await Task.Delay(RequestDelay);
                    var versionsJson = await _httpClient.GetStringAsync(versionsUrl);
                    var versions = JsonConvert.DeserializeObject<List<ModpackVersion>>(versionsJson);

                    var details = new ModpackDetails
                    {
                        ProjectId = project.Id,
                        Slug = project.Slug,
                        Name = project.Title ?? project.Name,
                        Description = project.Body ?? project.Description,
                        Author = project.Author,
                        Downloads = project.Downloads,
                        IconUrl = project.IconUrl,
                        Source = "modrinth",
                        Versions = versions ?? new List<ModpackVersion>()
                    };

                    if (versions != null && versions.Count > 0)
                    {
                        var latest = versions[0];
                        details.GameVersion = latest.GameVersions?.FirstOrDefault() ?? "";
                        details.ModLoader = latest.Loaders?.FirstOrDefault() ?? "";
                        details.FileUrl = latest.Files?.FirstOrDefault()?.Url ?? "";
                        details.FileName = latest.Files?.FirstOrDefault()?.Filename ?? "";
                    }

                    return details;
                }
                catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    retryCount++;
                    var waitTime = Math.Min(2000 * retryCount, 15000);
                    App.LogInfo($"遇到 429 限流，等待 {waitTime/1000} 秒后重试 ({retryCount}/{MaxRetries})");
                    await Task.Delay(waitTime);
                }
                catch (Exception ex)
                {
                    App.LogError($"获取详情失败 (Modrinth 镜像 {mirrorIndex + 1}): {ex.Message}");
                    if (mirrorIndex < MODRINTH_MIRRORS.Length - 1)
                    {
                        break;
                    }
                    retryCount++;
                    if (retryCount < MaxRetries)
                    {
                        await Task.Delay(1000 * retryCount);
                    }
                }
            }
        }

        return null;
    }

    #endregion

    #region CurseForge API

    private async Task<List<ModpackInfo>> GetCurseForgeFeaturedModpacksAsync(int count)
    {
        if (_httpClient == null)
            throw new Exception("HttpClient 未初始化");

        if (string.IsNullOrEmpty(_currentApiKey))
        {
            App.LogInfo("CurseForge API Key 未设置，返回示例数据");
            return GetSampleCurseForgeModpacks();
        }

        for (int mirrorIndex = 0; mirrorIndex < CURSEFORGE_MIRRORS.Length; mirrorIndex++)
        {
            int retryCount = 0;
            while (retryCount < MaxRetries)
            {
                try
                {
                    var apiBase = CURSEFORGE_MIRRORS[mirrorIndex];

                    var url = $"{apiBase}/mods/search?gameId=432&classId=4471&sortField=6&sortOrder=desc&pageSize={count}";
                    App.LogInfo($"获取热门整合包 (CurseForge): {url}");

                    await Task.Delay(RequestDelay);
                    var json = await _httpClient.GetStringAsync(url);
                    var result = JsonConvert.DeserializeObject<CurseForgeSearchResult>(json);

                    if (result?.Data != null)
                    {
                        var modpacks = result.Data.Select(m => new ModpackInfo
                        {
                            ProjectId = m.Id.ToString(),
                            Slug = m.Slug,
                            Name = m.Name,
                            Description = m.Summary ?? m.Description,
                            Author = m.Authors?.FirstOrDefault()?.Name ?? "未知",
                            Downloads = m.DownloadCount ?? 0,
                            IconUrl = m.Logo?.Url ?? "",
                            Source = "curseforge",
                            GameVersion = string.Join(", ", m.GameVersion ?? new List<string>())
                        }).ToList();

                        App.LogInfo($"获取到 {modpacks.Count} 个热门整合包 (CurseForge)");
                        return modpacks;
                    }
                }
                catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    retryCount++;
                    var waitTime = Math.Min(2000 * retryCount, 15000);
                    App.LogInfo($"遇到 429 限流，等待 {waitTime/1000} 秒后重试 ({retryCount}/{MaxRetries})");
                    await Task.Delay(waitTime);
                }
                catch (Exception ex)
                {
                    App.LogError($"获取热门整合包失败 (CurseForge 镜像 {mirrorIndex + 1}): {ex.Message}");
                    if (mirrorIndex < CURSEFORGE_MIRRORS.Length - 1)
                    {
                        break;
                    }
                    retryCount++;
                    if (retryCount < MaxRetries)
                    {
                        await Task.Delay(1000 * retryCount);
                    }
                }
            }
        }

        App.LogInfo("所有 CurseForge 镜像都无法获取，返回示例数据");
        return GetSampleCurseForgeModpacks();
    }

    private async Task<List<ModpackInfo>> SearchCurseForgeModpacksAsync(string query, int offset, int limit)
    {
        if (_httpClient == null)
            throw new Exception("HttpClient 未初始化");

        if (string.IsNullOrEmpty(_currentApiKey))
        {
            return SearchCurseForgeModpacksOffline(query);
        }

        for (int mirrorIndex = 0; mirrorIndex < CURSEFORGE_MIRRORS.Length; mirrorIndex++)
        {
            int retryCount = 0;
            while (retryCount < MaxRetries)
            {
                try
                {
                    var apiBase = CURSEFORGE_MIRRORS[mirrorIndex];
                    var url = $"{apiBase}/mods/search?gameId=432&classId=4471&searchFilter={Uri.EscapeDataString(query)}&sortField=6&sortOrder=desc&pageSize={limit}&index={offset}";

                    App.LogInfo($"搜索整合包 (CurseForge): {url}");

                    await Task.Delay(RequestDelay);
                    var json = await _httpClient.GetStringAsync(url);
                    var result = JsonConvert.DeserializeObject<CurseForgeSearchResult>(json);

                    if (result?.Data != null)
                    {
                        var modpacks = result.Data.Select(m => new ModpackInfo
                        {
                            ProjectId = m.Id.ToString(),
                            Slug = m.Slug,
                            Name = m.Name,
                            Description = m.Summary ?? m.Description,
                            Author = m.Authors?.FirstOrDefault()?.Name ?? "未知",
                            Downloads = m.DownloadCount ?? 0,
                            IconUrl = m.Logo?.Url ?? "",
                            Source = "curseforge",
                            GameVersion = string.Join(", ", m.GameVersion ?? new List<string>())
                        }).ToList();

                        return modpacks;
                    }
                }
                catch (Exception ex)
                {
                    App.LogError($"搜索整合包失败 (CurseForge 镜像 {mirrorIndex + 1}): {ex.Message}");
                    if (mirrorIndex < CURSEFORGE_MIRRORS.Length - 1)
                    {
                        break;
                    }
                    retryCount++;
                    if (retryCount < MaxRetries)
                    {
                        await Task.Delay(1000 * retryCount);
                    }
                }
            }
        }

        return SearchCurseForgeModpacksOffline(query);
    }

    private List<ModpackInfo> SearchCurseForgeModpacksOffline(string query)
    {
        var allModpacks = GetSampleCurseForgeModpacks();
        if (string.IsNullOrWhiteSpace(query))
            return allModpacks;

        return allModpacks.Where(m =>
            m.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            m.Description.Contains(query, StringComparison.OrdinalIgnoreCase)
        ).ToList();
    }

    private List<ModpackInfo> GetSampleCurseForgeModpacks()
    {
        return new List<ModpackInfo>
        {
            new ModpackInfo
            {
                ProjectId = "ftb-presents-direwolf20",
                Slug = "ftb-presents-direwolf20",
                Name = "FTB Presents Direwolf20 1.20",
                Description = "一个专注于机器自动化和红石科技的多模组整合包，由Direwolf20制作",
                Author = "FTB Team",
                Downloads = 5000000,
                IconUrl = "https://media.forgecdn.net/avatars/126/944/1af6ec8c038a0f5c49e4f77f2f921b67.jpg",
                Source = "curseforge",
                GameVersion = "1.20.1",
                ModLoader = "Forge"
            },
            new ModpackInfo
            {
                ProjectId = "enjezli",
                Slug = "enjezli",
                Name = "Enigmatica 2 Expertskyblock",
                Description = "经典科技整合包Enigmatica系列的高难度专家模式",
                Author = "NEEPS",
                Downloads = 2000000,
                IconUrl = "https://media.forgecdn.net/avatars/189/899/5d2c38a4b0e2c4c3e88e5d6c7f8b9a0.jpg",
                Source = "curseforge",
                GameVersion = "1.12.2",
                ModLoader = "Forge"
            },
            new ModpackInfo
            {
                ProjectId = "skyfactory-4",
                Slug = "skyfactory-4",
                Name = "SkyFactory 4",
                Description = "空岛生存整合包的经典之作，从一棵树开始生存",
                Author = "ShadowNode",
                Downloads = 8000000,
                IconUrl = "https://media.forgecdn.net/avatars/76/447/8f2c3d4e5a6b7c8d9e0f1a2b3c4d5e6.jpg",
                Source = "curseforge",
                GameVersion = "1.12.2",
                ModLoader = "Forge"
            },
            new ModpackInfo
            {
                ProjectId = "modpack-serv",
                Slug = "create-electrified",
                Name = "Create: Electrified",
                Description = "将Create模组与 mekanism 等科技模组结合的现代化科技整合包",
                Author = "Flamingo Studios",
                Downloads = 1500000,
                IconUrl = "https://media.forgecdn.net/avatars/234/567/9a8b7c6d5e4f3a2b1c0d9e8f7a6b5c4.jpg",
                Source = "curseforge",
                GameVersion = "1.19.2",
                ModLoader = "Forge"
            },
            new ModpackInfo
            {
                ProjectId = "rlcraft",
                Slug = "rlcraft",
                Name = "RLCraft",
                Description = "硬核生存整合包，包含大量生物群系和超强难度",
                Author = "Swper98",
                Downloads = 12000000,
                IconUrl = "https://media.forgecdn.net/avatars/345/678/0f1e2d3c4b5a6978879a6b5c4d3e2f1.jpg",
                Source = "curseforge",
                GameVersion = "1.12.2",
                ModLoader = "Forge"
            },
            new ModpackInfo
            {
                ProjectId = "all-the-mods-7",
                Slug = "all-the-mods-7",
                Name = "All The Mods 7",
                Description = "全模组整合包，收集了几乎所有热门模组",
                Author = "ATM Team",
                Downloads = 6000000,
                IconUrl = "https://media.forgecdn.net/avatars/456/789/1a2b3c4d5e6f7a8b9c0d1e2f3a4b5c6.jpg",
                Source = "curseforge",
                GameVersion = "1.19.2",
                ModLoader = "Forge"
            }
        };
    }

    public async Task<ModpackDetails?> GetCurseForgeModpackDetailsAsync(string modId)
    {
        if (_httpClient == null || string.IsNullOrEmpty(_currentApiKey))
            return GetSampleCurseForgeModpacks().FirstOrDefault(m => m.ProjectId == modId)?.ToDetails();

        for (int mirrorIndex = 0; mirrorIndex < CURSEFORGE_MIRRORS.Length; mirrorIndex++)
        {
            try
            {
                var apiBase = CURSEFORGE_MIRRORS[mirrorIndex];
                var url = $"{apiBase}/mods/{modId}";

                App.LogInfo($"获取整合包详情 (CurseForge): {url}");

                await Task.Delay(RequestDelay);
                var json = await _httpClient.GetStringAsync(url);
                var result = JsonConvert.DeserializeObject<CurseForgeModDetailResult>(json);

                if (result?.Data != null)
                {
                    var m = result.Data;
                    var details = new ModpackDetails
                    {
                        ProjectId = m.Id.ToString(),
                        Slug = m.Slug,
                        Name = m.Name,
                        Description = StripHtmlTags(m.Description ?? m.Summary ?? ""),
                        Author = m.Authors?.FirstOrDefault()?.Name ?? "未知",
                        Downloads = m.DownloadCount ?? 0,
                        IconUrl = m.Logo?.Url ?? "",
                        Source = "curseforge",
                        GameVersion = string.Join(", ", m.GameVersion ?? new List<string>())
                    };

                    var filesUrl = $"{apiBase}/mods/{modId}/files?pageSize=1&sortField=1&sortOrder=desc";
                    await Task.Delay(RequestDelay);
                    var filesJson = await _httpClient.GetStringAsync(filesUrl);
                    var filesResult = JsonConvert.DeserializeObject<CurseForgeFilesResult>(filesJson);

                    if (filesResult?.Data != null && filesResult.Data.Count > 0)
                    {
                        var latestFile = filesResult.Data[0];
                        details.FileUrl = latestFile.DownloadUrl ?? "";
                        details.FileName = latestFile.FileName ?? "";
                    }

                    return details;
                }
            }
            catch (Exception ex)
            {
                App.LogError($"获取详情失败 (CurseForge 镜像 {mirrorIndex + 1}): {ex.Message}");
            }
        }

        return GetSampleCurseForgeModpacks().FirstOrDefault(m => m.ProjectId == modId)?.ToDetails();
    }

    private string StripHtmlTags(string html)
    {
        if (string.IsNullOrEmpty(html))
            return html;
        return Regex.Replace(html, "<[^>]+>", "").Trim();
    }

    #endregion

    public async Task<ModpackDetails?> GetModpackDetailsAsync(string projectId, string source)
    {
        if (source == "curseforge")
        {
            return await GetCurseForgeModpackDetailsAsync(projectId);
        }
        else
        {
            return await GetModrinthModpackDetailsAsync(projectId);
        }
    }

    public async Task DownloadModpackAsync(string downloadUrl, string savePath, CancellationToken cancellationToken = default)
    {
        if (_httpClient == null || string.IsNullOrEmpty(downloadUrl))
            throw new Exception("无法下载整合包");

        int retryCount = 0;
        while (retryCount < MaxRetries)
        {
            try
            {
                var directory = Path.GetDirectoryName(savePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                App.LogInfo($"正在下载整合包: {Path.GetFileName(downloadUrl)}");

                using var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    retryCount++;
                    var waitTime = Math.Min(3000 * retryCount, 20000);
                    App.LogInfo($"下载遇到 429 限流，等待 {waitTime/1000} 秒后重试 ({retryCount}/{MaxRetries})");
                    await Task.Delay(waitTime, cancellationToken);
                    continue;
                }

                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? 0;
                var downloadedBytes = 0L;

                using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var fileStream = new FileStream(savePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

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
                        FileName = Path.GetFileName(savePath),
                        Progress = totalBytes > 0 ? (double)downloadedBytes / totalBytes * 100 : 0
                    });
                }

                App.LogInfo($"整合包下载完成：{savePath}");
                return;
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                retryCount++;
                if (retryCount >= MaxRetries)
                {
                    throw new Exception($"下载整合包遇到 429 限流，已重试 {MaxRetries} 次，请稍后再试");
                }
                var waitTime = Math.Min(3000 * retryCount, 20000);
                App.LogInfo($"下载遇到 429 限流，等待 {waitTime/1000} 秒后重试 ({retryCount}/{MaxRetries})");
                await Task.Delay(waitTime, cancellationToken);
            }
            catch (Exception ex)
            {
                retryCount++;
                if (retryCount >= MaxRetries)
                {
                    App.LogError($"下载整合包失败，已重试 {MaxRetries} 次", ex);
                    throw;
                }
                App.LogInfo($"下载整合包失败，等待 {retryCount} 秒后重试 ({retryCount}/{MaxRetries}): {ex.Message}");
                await Task.Delay(1000 * retryCount, cancellationToken);
            }
        }
    }
}

#region Modrinth Models

public class ModpackSearchResult
{
    [JsonProperty("hits")]
    public List<ModpackInfo> Hits { get; set; }

    [JsonProperty("total_hits")]
    public int TotalHits { get; set; }
}

public class ModpackProjectInfo
{
    [JsonProperty("id")]
    public string Id { get; set; }

    [JsonProperty("slug")]
    public string Slug { get; set; }

    [JsonProperty("name")]
    public string Name { get; set; }

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

    [JsonProperty("categories")]
    public List<string> Categories { get; set; }
}

public class ModpackVersion
{
    [JsonProperty("id")]
    public string Id { get; set; }

    [JsonProperty("name")]
    public string Name { get; set; }

    [JsonProperty("version_number")]
    public string VersionNumber { get; set; }

    [JsonProperty("game_versions")]
    public List<string> GameVersions { get; set; }

    [JsonProperty("loaders")]
    public List<string> Loaders { get; set; }

    [JsonProperty("files")]
    public List<ModpackFile> Files { get; set; }
}

public class ModpackFile
{
    [JsonProperty("url")]
    public string Url { get; set; }

    [JsonProperty("filename")]
    public string Filename { get; set; }

    [JsonProperty("hashes")]
    public Dictionary<string, string> Hashes { get; set; }
}

#endregion

#region CurseForge Models

public class CurseForgeSearchResult
{
    [JsonProperty("data")]
    public List<CurseForgeMod> Data { get; set; }

    [JsonProperty("pagination")]
    public CurseForgePagination Pagination { get; set; }
}

public class CurseForgeModDetailResult
{
    [JsonProperty("data")]
    public CurseForgeMod Data { get; set; }
}

public class CurseForgeFilesResult
{
    [JsonProperty("data")]
    public List<CurseForgeFile> Data { get; set; }
}

public class CurseForgeMod
{
    [JsonProperty("id")]
    public int Id { get; set; }

    [JsonProperty("name")]
    public string Name { get; set; }

    [JsonProperty("slug")]
    public string Slug { get; set; }

    [JsonProperty("summary")]
    public string Summary { get; set; }

    [JsonProperty("description")]
    public string Description { get; set; }

    [JsonProperty("downloadCount")]
    public int? DownloadCount { get; set; }

    [JsonProperty("logo")]
    public CurseForgeLogo Logo { get; set; }

    [JsonProperty("authors")]
    public List<CurseForgeAuthor> Authors { get; set; }

    [JsonProperty("gameVersion")]
    public List<string> GameVersion { get; set; }

    [JsonProperty("classId")]
    public int ClassId { get; set; }
}

public class CurseForgeLogo
{
    [JsonProperty("id")]
    public int Id { get; set; }

    [JsonProperty("url")]
    public string Url { get; set; }

    [JsonProperty("thumbnailUrl")]
    public string ThumbnailUrl { get; set; }
}

public class CurseForgeAuthor
{
    [JsonProperty("id")]
    public int Id { get; set; }

    [JsonProperty("name")]
    public string Name { get; set; }

    [JsonProperty("url")]
    public string Url { get; set; }
}

public class CurseForgeFile
{
    [JsonProperty("id")]
    public int Id { get; set; }

    [JsonProperty("fileName")]
    public string FileName { get; set; }

    [JsonProperty("downloadUrl")]
    public string DownloadUrl { get; set; }

    [JsonProperty("fileDate")]
    public string FileDate { get; set; }
}

public class CurseForgePagination
{
    [JsonProperty("pageIndex")]
    public int PageIndex { get; set; }

    [JsonProperty("pageSize")]
    public int PageSize { get; set; }

    [JsonProperty("totalCount")]
    public int TotalCount { get; set; }

    [JsonProperty("pageCount")]
    public int PageCount { get; set; }
}

#endregion

public class ModpackDetails
{
    public string ProjectId { get; set; }
    public string Slug { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public string Author { get; set; }
    public int Downloads { get; set; }
    public string IconUrl { get; set; }
    public string GameVersion { get; set; }
    public string ModLoader { get; set; }
    public int ModCount { get; set; }
    public List<ModpackVersion> Versions { get; set; }
    public string FileUrl { get; set; }
    public string FileName { get; set; }
    public string Source { get; set; }
}