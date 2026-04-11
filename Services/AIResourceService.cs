using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using MinecraftLauncher.Models;

namespace MinecraftLauncher.Services
{
    public class AIResourceService : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly string _gameDir;
        private readonly ModService _modService;
        private bool _disposed;

        public event Action<string, int, int>? DownloadProgress;

        public AIResourceService(string gameDir)
        {
            _gameDir = gameDir;
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "NMCL/1.0");
            _modService = new ModService();
        }

        public async Task<List<ResourceSearchResult>> SearchResourcesAsync(
            string resourceType, 
            string query, 
            string? gameVersion = null, 
            int limit = 10)
        {
            try
            {
                var results = new List<ResourceSearchResult>();

                switch (resourceType.ToLower())
                {
                    case "mod":
                        results = await SearchModsAsync(query, gameVersion, limit);
                        break;
                    case "shader":
                        results = await SearchShadersAsync(query, gameVersion, limit);
                        break;
                    case "resourcepack":
                        results = await SearchResourcePacksAsync(query, gameVersion, limit);
                        break;
                    case "map":
                        results = await SearchMapsAsync(query, gameVersion, limit);
                        break;
                }

                return results;
            }
            catch (Exception ex)
            {
                App.LogError($"搜索资源失败: {resourceType} - {query}", ex);
                return new List<ResourceSearchResult>();
            }
        }

        private async Task<List<ResourceSearchResult>> SearchModsAsync(string query, string? gameVersion, int limit)
        {
            try
            {
                var searchResults = await _modService.SearchModsAsync(query, new[] { "fabric" }, gameVersion != null ? new[] { gameVersion } : null, limit);
                
                return searchResults.Select(m => new ResourceSearchResult
                {
                    Name = m.Title,
                    Description = m.Description,
                    Author = m.Author,
                    Downloads = m.Downloads,
                    Version = m.LatestVersion,
                    DownloadUrl = "",
                    ResourceType = "mod",
                    GameVersion = gameVersion ?? "",
                    IconUrl = m.IconUrl
                }).ToList();
            }
            catch (Exception ex)
            {
                App.LogError($"搜索模组失败: {query}", ex);
                return new List<ResourceSearchResult>();
            }
        }

        private async Task<List<ResourceSearchResult>> SearchShadersAsync(string query, string? gameVersion, int limit)
        {
            var results = new List<ResourceSearchResult>();
            
            try
            {
                var url = $"https://api.modrinth.com/v2/search?query={Uri.EscapeDataString(query + " shader")}&limit={limit}&facets=[[\"project_type:shader\"]]";
                
                if (!string.IsNullOrEmpty(gameVersion))
                {
                    url += $"&facets=[[\"versions:{gameVersion}\"]]";
                }

                var response = await _httpClient.GetStringAsync(url);
                var json = JsonDocument.Parse(response);
                var hits = json.RootElement.GetProperty("hits");

                foreach (var hit in hits.EnumerateArray())
                {
                    results.Add(new ResourceSearchResult
                    {
                        Name = hit.GetProperty("title").GetString() ?? "",
                        Description = hit.GetProperty("description").GetString() ?? "",
                        Author = hit.GetProperty("author").GetString() ?? "",
                        Downloads = hit.GetProperty("downloads").GetInt32(),
                        Version = "",
                        DownloadUrl = "",
                        ResourceType = "shader",
                        GameVersion = gameVersion ?? "",
                        IconUrl = hit.TryGetProperty("icon_url", out var iconUrl) ? iconUrl.GetString() ?? "" : ""
                    });
                }
            }
            catch (Exception ex)
            {
                App.LogError("搜索光影失败", ex);
            }

            return results;
        }

        private async Task<List<ResourceSearchResult>> SearchResourcePacksAsync(string query, string? gameVersion, int limit)
        {
            var results = new List<ResourceSearchResult>();
            
            try
            {
                var url = $"https://api.modrinth.com/v2/search?query={Uri.EscapeDataString(query)}&limit={limit}&facets=[[\"project_type:resourcepack\"]]";
                
                if (!string.IsNullOrEmpty(gameVersion))
                {
                    url += $"&facets=[[\"versions:{gameVersion}\"]]";
                }

                var response = await _httpClient.GetStringAsync(url);
                var json = JsonDocument.Parse(response);
                var hits = json.RootElement.GetProperty("hits");

                foreach (var hit in hits.EnumerateArray())
                {
                    results.Add(new ResourceSearchResult
                    {
                        Name = hit.GetProperty("title").GetString() ?? "",
                        Description = hit.GetProperty("description").GetString() ?? "",
                        Author = hit.GetProperty("author").GetString() ?? "",
                        Downloads = hit.GetProperty("downloads").GetInt32(),
                        Version = "",
                        DownloadUrl = "",
                        ResourceType = "resourcepack",
                        GameVersion = gameVersion ?? "",
                        IconUrl = hit.TryGetProperty("icon_url", out var iconUrl) ? iconUrl.GetString() ?? "" : ""
                    });
                }
            }
            catch (Exception ex)
            {
                App.LogError("搜索资源包失败", ex);
            }

            return results;
        }

        private async Task<List<ResourceSearchResult>> SearchMapsAsync(string query, string? gameVersion, int limit)
        {
            var results = new List<ResourceSearchResult>();
            
            try
            {
                var url = $"https://api.modrinth.com/v2/search?query={Uri.EscapeDataString(query)}&limit={limit}&facets=[[\"project_type:mod\"]]";
                
                var response = await _httpClient.GetStringAsync(url);
                var json = JsonDocument.Parse(response);
                var hits = json.RootElement.GetProperty("hits");

                foreach (var hit in hits.EnumerateArray())
                {
                    var title = hit.GetProperty("title").GetString() ?? "";
                    if (title.ToLower().Contains("map") || title.ToLower().Contains("地图"))
                    {
                        results.Add(new ResourceSearchResult
                        {
                            Name = title,
                            Description = hit.GetProperty("description").GetString() ?? "",
                            Author = hit.GetProperty("author").GetString() ?? "",
                            Downloads = hit.GetProperty("downloads").GetInt32(),
                            Version = "",
                            DownloadUrl = "",
                            ResourceType = "map",
                            GameVersion = gameVersion ?? "",
                            IconUrl = hit.TryGetProperty("icon_url", out var iconUrl) ? iconUrl.GetString() ?? "" : ""
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                App.LogError("搜索地图失败", ex);
            }

            return results;
        }

        public async Task<bool> InstallResourceAsync(ResourceSearchResult resource, string? targetVersion = null)
        {
            try
            {
                switch (resource.ResourceType.ToLower())
                {
                    case "mod":
                        return await InstallModAsync(resource, targetVersion);
                    case "shader":
                        return await InstallShaderAsync(resource);
                    case "resourcepack":
                        return await InstallResourcePackAsync(resource);
                    case "map":
                        return await InstallMapAsync(resource);
                    default:
                        return false;
                }
            }
            catch (Exception ex)
            {
                App.LogError($"安装资源失败: {resource.Name}", ex);
                return false;
            }
        }

        private async Task<bool> InstallModAsync(ResourceSearchResult resource, string? targetVersion)
        {
            try
            {
                App.LogInfo($"[AI助手] 开始安装模组: {resource.Name}");
                
                if (string.IsNullOrEmpty(targetVersion))
                {
                    App.LogError("[AI助手] 未指定目标版本，无法安装模组");
                    return false;
                }
                
                var versionsDir = Path.Combine(_gameDir, "versions");
                var versionDir = Path.Combine(versionsDir, targetVersion);
                var modsDir = Path.Combine(versionDir, "mods");
                
                if (!Directory.Exists(modsDir))
                {
                    Directory.CreateDirectory(modsDir);
                    App.LogInfo($"[AI助手] 创建模组目录: {modsDir}");
                }
                
                var modDetails = await _modService.GetModDetailsAsync(resource.Name);
                
                if (modDetails.VersionFiles == null || modDetails.VersionFiles.Count == 0)
                {
                    App.LogError($"[AI助手] 未找到模组下载链接: {resource.Name}");
                    return false;
                }
                
                var suitableFile = modDetails.VersionFiles.FirstOrDefault(f => 
                    f.GameVersions.Contains(targetVersion) && 
                    f.Filename.EndsWith(".jar"));
                
                if (suitableFile == null)
                {
                    suitableFile = modDetails.VersionFiles.FirstOrDefault(f => f.Filename.EndsWith(".jar"));
                }
                
                if (suitableFile == null)
                {
                    App.LogError($"[AI助手] 未找到适合的模组文件: {resource.Name}");
                    return false;
                }
                
                var savePath = Path.Combine(modsDir, suitableFile.Filename);
                
                App.LogInfo($"[AI助手] 开始下载模组到: {savePath}");
                
                await _modService.DownloadModFileAsync(suitableFile.Url, savePath);
                
                App.LogInfo($"[AI助手] 模组下载完成: {suitableFile.Filename}");
                
                return true;
            }
            catch (Exception ex)
            {
                App.LogError($"[AI助手] 安装模组失败: {resource.Name}", ex);
                return false;
            }
        }

        private async Task<bool> InstallShaderAsync(ResourceSearchResult resource)
        {
            try
            {
                var shaderpacksDir = Path.Combine(_gameDir, "shaderpacks");
                if (!Directory.Exists(shaderpacksDir))
                {
                    Directory.CreateDirectory(shaderpacksDir);
                }

                App.LogInfo($"[AI助手] 光影包将下载到: {shaderpacksDir}");
                return true;
            }
            catch (Exception ex)
            {
                App.LogError($"安装光影失败: {resource.Name}", ex);
                return false;
            }
        }

        private async Task<bool> InstallResourcePackAsync(ResourceSearchResult resource)
        {
            try
            {
                var resourcepacksDir = Path.Combine(_gameDir, "resourcepacks");
                if (!Directory.Exists(resourcepacksDir))
                {
                    Directory.CreateDirectory(resourcepacksDir);
                }

                App.LogInfo($"[AI助手] 资源包将下载到: {resourcepacksDir}");
                return true;
            }
            catch (Exception ex)
            {
                App.LogError($"安装资源包失败: {resource.Name}", ex);
                return false;
            }
        }

        private async Task<bool> InstallMapAsync(ResourceSearchResult resource)
        {
            try
            {
                var savesDir = Path.Combine(_gameDir, "saves");
                if (!Directory.Exists(savesDir))
                {
                    Directory.CreateDirectory(savesDir);
                }

                App.LogInfo($"[AI助手] 地图将解压到: {savesDir}");
                return true;
            }
            catch (Exception ex)
            {
                App.LogError($"安装地图失败: {resource.Name}", ex);
                return false;
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                _httpClient.Dispose();
            }
        }
    }
}
