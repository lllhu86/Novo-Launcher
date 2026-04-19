using System.IO;
using System.Net.Http;
using System.Text;
using MinecraftLauncher.Models;
using Newtonsoft.Json;

namespace MinecraftLauncher.Services;

public class MinecraftService
{
    private HttpClient? _httpClient;
    private SemaphoreSlim? _downloadSemaphore;
    private static readonly TimeSpan RequestDelay = TimeSpan.FromMilliseconds(500); // 500ms 请求间隔
    private static readonly int MaxRetries = 5; // 最大重试次数
    
    // Minecraft 专用镜像源，按优先级排序
    private static readonly string[] MAVEN_MIRRORS = new[]
    {
        "https://bmclapi2.bangbang93.com/maven",               // BMCLAPI 主镜像（国内最快，专门为 Minecraft 设计）
        "https://bmclapi.bangbang93.com/maven",                // BMCLAPI 备用镜像
        "https://mirrors4.qlu.edu.cn/bmclapi",                 // 齐鲁工业大学镜像（教育网）
        "https://libraries.minecraft.net",                      // Minecraft 官方库
        "https://repo.lwjgl.org",                               // LWJGL 官方仓库
        "https://maven.aliyun.com/repository/public",           // 阿里云 Maven 镜像（通用库）
        "https://repo1.maven.org/maven2"                        // Maven 官方中央仓库
    };
    
    private const string BMCLAPI_BASE = "https://bmclapi2.bangbang93.com";
    private const string MOJANG_BASE = "https://launchermeta.mojang.com";
    
    public bool UseMirror { get; set; } = true;
    
    // 下载模式：false=单线程（稳定），true=多线程（快速）
    public bool UseMultiThreadDownload { get; set; } = false;
    
    public event EventHandler<DownloadProgressEventArgs>? DownloadProgressChanged;

    public MinecraftService()
    {
        try
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate,
                AllowAutoRedirect = true,
                ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true,
                MaxConnectionsPerServer = 100
            };
            _httpClient = new HttpClient(handler);
            _httpClient.Timeout = TimeSpan.FromMinutes(30);
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "NMCL/1.0");
            
            _downloadSemaphore = new SemaphoreSlim(32, 64);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"HttpClient 初始化失败：{ex.Message}");
        }
    }
    
    public void SetDownloadMode(bool multiThread)
    {
        UseMultiThreadDownload = multiThread;
        var concurrency = multiThread ? 64 : 32;
        if (_downloadSemaphore != null)
        {
            _downloadSemaphore.Dispose();
        }
        _downloadSemaphore = new SemaphoreSlim(concurrency, concurrency);
    }

    private string GetBaseUrl()
    {
        return UseMirror ? BMCLAPI_BASE : MOJANG_BASE;
    }

    public async Task<VersionManifest?> GetVersionManifestAsync()
    {
        if (_httpClient == null)
            throw new Exception("HttpClient 未初始化");

        try
        {
            // 优先使用 BMCLAPI，如果失败则尝试其他镜像
            string[] urls = new[]
            {
                "https://bmclapi2.bangbang93.com/mc/game/version_manifest_v2.json",
                "https://bmclapi.bangbang93.com/mc/game/version_manifest_v2.json",
                "https://launchermeta.mojang.com/mc/game/version_manifest_v2.json"
            };

            foreach (var url in urls)
            {
                try
                {
                    App.LogInfo($"正在获取版本清单：{url}");
                    
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                    var json = await _httpClient.GetStringAsync(url, cts.Token);
                    var manifest = JsonConvert.DeserializeObject<VersionManifest>(json);
                    
                    if (manifest != null)
                    {
                        App.LogInfo($"版本清单获取成功，共 {manifest.Versions?.Count ?? 0} 个版本");
                        return manifest;
                    }
                }
                catch (Exception ex)
                {
                    App.LogError($"从 {url} 获取失败：{ex.Message}", ex);
                    // 继续尝试下一个 URL
                }
            }

            throw new Exception("所有镜像源都无法访问，请检查网络连接");
        }
        catch (HttpRequestException ex)
        {
            App.LogError($"网络请求失败：{ex.Message}", ex);
            throw new Exception($"网络请求失败：{ex.Message}\n\n请检查：\n1. 网络连接是否正常\n2. 防火墙是否阻止访问\n3. 是否需要代理");
        }
        catch (JsonException ex)
        {
            App.LogError($"JSON 解析失败：{ex.Message}", ex);
            throw new Exception($"JSON 解析失败：{ex.Message}");
        }
        catch (OperationCanceledException)
        {
            App.LogError("获取版本清单超时");
            throw new Exception("获取版本清单超时，请检查网络连接");
        }
        catch (Exception ex)
        {
            App.LogError($"获取版本清单失败：{ex.Message}", ex);
            throw new Exception($"获取版本清单失败：{ex.Message}");
        }
    }

    public async Task<VersionDetail?> GetVersionDetailAsync(VersionInfo versionInfo)
    {
        if (_httpClient == null)
            throw new Exception("HttpClient 未初始化");

        try
        {
            var url = versionInfo.Url;
            if (UseMirror && url != null)
            {
                url = url.Replace("https://launchermeta.mojang.com", BMCLAPI_BASE)
                         .Replace("https://launcher.mojang.com", BMCLAPI_BASE)
                         .Replace("https://piston-meta.mojang.com", BMCLAPI_BASE)
                         .Replace("https://piston-data.mojang.com", BMCLAPI_BASE);
            }
            App.LogInfo($"正在获取版本详情：{url}");
            
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var json = await _httpClient.GetStringAsync(url, cts.Token);
            return JsonConvert.DeserializeObject<VersionDetail>(json);
        }
        catch (OperationCanceledException)
        {
            App.LogError("获取版本详情超时");
            throw new Exception("获取版本详情超时");
        }
        catch (Exception ex)
        {
            App.LogError($"获取版本详情失败：{ex.Message}", ex);
            throw new Exception($"获取版本详情失败：{ex.Message}");
        }
    }

    public async Task PrepareVersionAsync(VersionDetail version, string gameDir, CancellationToken cancellationToken = default)
    {
        try
        {
            App.LogInfo($"准备游戏版本：{version.Id}");
            App.LogInfo($"游戏目录：{gameDir}");
            
            var dotMinecraftDir = Path.Combine(gameDir, ".minecraft");
            if (!Directory.Exists(dotMinecraftDir))
            {
                App.LogInfo($"创建目录：{dotMinecraftDir}");
                Directory.CreateDirectory(dotMinecraftDir);
            }

            var versionsDir = Path.Combine(dotMinecraftDir, "versions");
            if (!Directory.Exists(versionsDir))
            {
                App.LogInfo($"创建目录：{versionsDir}");
                Directory.CreateDirectory(versionsDir);
            }

            var versionDirPath = GetGameDirectory(gameDir, version.Id ?? "unknown");
            
            if (Directory.Exists(versionDirPath))
            {
                var existingJsonPath = Path.Combine(versionDirPath, $"{version.Id}.json");
                if (!File.Exists(existingJsonPath))
                {
                    App.LogInfo($"检测到未完成的下载，清理版本目录：{versionDirPath}");
                    DownloadProgressChanged?.Invoke(this, new DownloadProgressEventArgs
                    {
                        FileName = "正在清理未完成的下载...",
                        Progress = 0,
                        TotalBytes = 100,
                        DownloadedBytes = 0
                    });
                    
                    try
                    {
                        Directory.Delete(versionDirPath, true);
                        App.LogInfo("清理完成");
                    }
                    catch (Exception ex)
                    {
                        App.LogError($"清理版本目录失败：{ex.Message}", ex);
                    }
                }
            }
            
            if (!Directory.Exists(versionDirPath))
            {
                App.LogInfo($"创建目录：{versionDirPath}");
                Directory.CreateDirectory(versionDirPath);
            }

            var clientJarPath = Path.Combine(versionDirPath, $"{version.Id}.jar");
            if (!File.Exists(clientJarPath))
            {
                App.LogInfo("开始下载客户端 JAR...");
                DownloadProgressChanged?.Invoke(this, new DownloadProgressEventArgs
                {
                    FileName = "正在下载游戏核心...",
                    Progress = 5,
                    TotalBytes = 100,
                    DownloadedBytes = 5
                });
                
                await DownloadClientAsync(version, clientJarPath, cancellationToken);
            }
            else
            {
                App.LogInfo($"客户端 JAR 已存在：{clientJarPath}");
            }

            var librariesPath = Path.Combine(dotMinecraftDir, "libraries");
            App.LogInfo("检查库文件...");
            DownloadProgressChanged?.Invoke(this, new DownloadProgressEventArgs
            {
                FileName = "正在检查库文件...",
                Progress = 10,
                TotalBytes = 100,
                DownloadedBytes = 10
            });
            
            await DownloadLibrariesAsync(version, librariesPath, cancellationToken);

            var librariesValid = await VerifyLibrariesAsync(version, librariesPath, cancellationToken);
            if (!librariesValid)
            {
                App.LogInfo("库文件验证失败，重新下载...");
                await DownloadLibrariesAsync(version, librariesPath, cancellationToken);
                librariesValid = await VerifyLibrariesAsync(version, librariesPath, cancellationToken);
                if (!librariesValid)
                {
                    throw new Exception("库文件下载不完整，请检查网络连接后重试");
                }
            }

            if (!string.IsNullOrEmpty(version.Assets))
            {
                App.LogInfo("检查资源文件...");
                DownloadProgressChanged?.Invoke(this, new DownloadProgressEventArgs
                {
                    FileName = "正在检查资源文件...",
                    Progress = 60,
                    TotalBytes = 100,
                    DownloadedBytes = 60
                });
                
                await DownloadAssetsAsync(version, dotMinecraftDir, cancellationToken);

                var assetsValid = await VerifyAssetsAsync(dotMinecraftDir, version.Assets, cancellationToken);
                if (!assetsValid)
                {
                    App.LogInfo("资源文件验证失败，重新下载缺失文件...");
                    await DownloadAssetsAsync(version, dotMinecraftDir, cancellationToken);
                    assetsValid = await VerifyAssetsAsync(dotMinecraftDir, version.Assets, cancellationToken);
                    if (!assetsValid)
                    {
                        throw new Exception("资源文件下载不完整，请检查网络连接后重试");
                    }
                }
            }

            DownloadProgressChanged?.Invoke(this, new DownloadProgressEventArgs
            {
                FileName = "准备完成！",
                Progress = 100,
                TotalBytes = 100,
                DownloadedBytes = 100
            });

            var versionJsonPath = Path.Combine(versionDirPath, $"{version.Id}.json");
            var json = JsonConvert.SerializeObject(version, Formatting.Indented);
            await File.WriteAllTextAsync(versionJsonPath, json, cancellationToken);
            
            App.LogInfo($"游戏版本 {version.Id} 准备完成");
        }
        catch (Exception ex)
        {
            App.LogError("准备游戏版本失败", ex);
            throw;
        }
    }

    public async Task DownloadAssetsAsync(VersionDetail version, string gameDir, CancellationToken cancellationToken = default)
    {
        if (_httpClient == null || string.IsNullOrEmpty(version.Assets))
            return;

        var assetsDir = Path.Combine(gameDir, "assets");
        if (!Directory.Exists(assetsDir))
        {
            Directory.CreateDirectory(assetsDir);
        }

        var indexesDir = Path.Combine(assetsDir, "indexes");
        if (!Directory.Exists(indexesDir))
        {
            Directory.CreateDirectory(indexesDir);
        }

        var objectsDir = Path.Combine(assetsDir, "objects");
        if (!Directory.Exists(objectsDir))
        {
            Directory.CreateDirectory(objectsDir);
        }

        var assetIndexUrl = version.AssetIndex?.Url;
        var assetIndexId = version.Assets;

        if (string.IsNullOrEmpty(assetIndexUrl))
        {
            App.LogInfo($"没有找到资源索引 URL，尝试使用已知索引");
            return;
        }

        if (UseMirror)
        {
            assetIndexUrl = assetIndexUrl.Replace("https://launchermeta.mojang.com", BMCLAPI_BASE)
                                        .Replace("https://piston-meta.mojang.com", BMCLAPI_BASE)
                                        .Replace("https://piston-data.mojang.com", BMCLAPI_BASE);
        }

        var indexFilePath = Path.Combine(indexesDir, $"{assetIndexId}.json");
        App.LogInfo($"正在下载资源索引：{assetIndexUrl}");

        string indexJson = string.Empty;
        int assetIndexRetry = 0;
        bool downloadSuccess = false;
        
        while (assetIndexRetry < MaxRetries && !downloadSuccess)
        {
            try
            {
                using (var response = await _httpClient.GetAsync(assetIndexUrl, cancellationToken))
                {
                    if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                    {
                        assetIndexRetry++;
                        var waitTime = Math.Min(3000 * assetIndexRetry, 15000);
                        App.LogInfo($"下载资源索引遇到 429 限流，等待 {waitTime/1000} 秒后重试 ({assetIndexRetry}/{MaxRetries}): {assetIndexUrl}");
                        await Task.Delay(waitTime, cancellationToken);
                        continue;
                    }
                    response.EnsureSuccessStatusCode();
                    indexJson = await response.Content.ReadAsStringAsync(cancellationToken);
                    await File.WriteAllTextAsync(indexFilePath, indexJson, cancellationToken);
                    downloadSuccess = true;
                }
            }
            catch (Exception ex) when (assetIndexRetry < MaxRetries - 1)
            {
                assetIndexRetry++;
                var waitTime = 1000 * assetIndexRetry;
                App.LogInfo($"下载资源索引失败，等待 {waitTime/1000} 秒后重试 ({assetIndexRetry}/{MaxRetries}): {ex.Message}");
                await Task.Delay(waitTime, cancellationToken);
            }
        }

        if (!downloadSuccess || string.IsNullOrEmpty(indexJson))
        {
            App.LogError("下载资源索引失败，已达到最大重试次数");
            throw new Exception("下载资源索引失败，已达到最大重试次数，请稍后再试");
        }

        App.LogInfo("正在解析资源索引...");
        var assetIndex = JsonConvert.DeserializeObject<AssetIndexContent>(indexJson);
        if (assetIndex?.Objects == null)
        {
            App.LogError("资源索引解析失败");
            throw new Exception("资源索引解析失败");
        }

        App.LogInfo($"需要下载 {assetIndex.Objects.Count} 个资源文件");

        var totalAssets = assetIndex.Objects.Count;
        var downloadedAssets = 0;
        var failedAssets = new System.Collections.Concurrent.ConcurrentBag<(string Name, string Hash, string SubDir)>();
        var tasks = new List<Task>();
        var total429Errors = 0;
        var total5xxErrors = 0;

        foreach (var kvp in assetIndex.Objects)
        {
            var assetName = kvp.Key;
            var asset = kvp.Value;

            tasks.Add(Task.Run(async () =>
            {
                var hash = asset.Hash;
                var subDir = hash.Substring(0, 2);
                try
                {
                    var assetPath = Path.Combine(objectsDir, subDir, hash);

                    if (!File.Exists(assetPath))
                    {
                        var assetDir = Path.GetDirectoryName(assetPath);
                        if (!string.IsNullOrEmpty(assetDir) && !Directory.Exists(assetDir))
                        {
                            Directory.CreateDirectory(assetDir);
                        }

                        var assetUrls = GetAssetDownloadUrls(subDir, hash);
                        
                        var downloaded = false;
                        var retryCount = 0;
                        var maxRetries = assetUrls.Count * 2;
                        var currentUrlIndex = 0;
                        
                        while (!downloaded && retryCount < maxRetries)
                        {
                            try
                            {
                                var currentUrl = assetUrls[currentUrlIndex % assetUrls.Count];
                                await DownloadFileAsync(currentUrl, assetPath, asset.Size, cancellationToken, false);
                                downloaded = true;
                            }
                            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                            {
                                retryCount++;
                                currentUrlIndex++;
                                Interlocked.Increment(ref total429Errors);
                                
                                var waitTime = Math.Min(5000 * retryCount, 30000);
                                App.LogInfo($"429 限流，等待 {waitTime/1000} 秒后切换源重试 ({retryCount}/{maxRetries}): {hash}");
                                await Task.Delay(waitTime, cancellationToken);
                            }
                            catch (HttpRequestException ex) when ((int?)ex.StatusCode >= 500 && (int?)ex.StatusCode < 600)
                            {
                                retryCount++;
                                currentUrlIndex++;
                                Interlocked.Increment(ref total5xxErrors);
                                App.LogInfo($"服务器错误 ({(int?)ex.StatusCode})，切换下载源 ({retryCount}/{maxRetries}): {hash}");
                                await Task.Delay(2000, cancellationToken);
                            }
                            catch (Exception ex) when (retryCount < maxRetries - 1)
                            {
                                retryCount++;
                                currentUrlIndex++;
                                App.LogInfo($"资源下载失败，切换源重试 ({retryCount}/{maxRetries}): {hash} - {ex.Message}");
                                await Task.Delay(1000 * Math.Min(retryCount, 5), cancellationToken);
                            }
                        }
                        
                        if (!downloaded)
                        {
                            failedAssets.Add((assetName, hash, subDir));
                            App.LogError($"资源下载失败: {assetName} ({hash})");
                        }
                    }

                    var currentCount = Interlocked.Increment(ref downloadedAssets);
                    var progress = (double)currentCount / totalAssets * 100;
                    
                    if (currentCount % 50 == 0 || currentCount == totalAssets)
                    {
                        DownloadProgressChanged?.Invoke(this, new DownloadProgressEventArgs
                        {
                            TotalBytes = totalAssets,
                            DownloadedBytes = currentCount,
                            FileName = $"资源文件 ({currentCount}/{totalAssets})",
                            Progress = 60 + (progress * 0.4)
                        });
                    }
                }
                catch (Exception ex)
                {
                    failedAssets.Add((assetName, hash, subDir));
                    App.LogError($"下载资源异常: {assetName}", ex);
                    Interlocked.Increment(ref downloadedAssets);
                }
            }, cancellationToken));
        }

        await Task.WhenAll(tasks);
        
        if (total429Errors > 0)
        {
            App.LogInfo($"下载过程中遇到 {total429Errors} 次 429 限流错误");
        }
        
        if (total5xxErrors > 0)
        {
            App.LogInfo($"下载过程中遇到 {total5xxErrors} 次服务器错误");
        }
        
        if (failedAssets.Count > 0)
        {
            App.LogInfo($"检测到 {failedAssets.Count} 个资源文件下载失败，尝试使用官方源重新下载...");
            
            var retrySuccess = 0;
            var retryFailed = new List<string>();
            
            foreach (var (name, hash, subDir) in failedAssets)
            {
                try
                {
                    var assetPath = Path.Combine(objectsDir, subDir, hash);
                    var officialUrl = $"https://resources.download.minecraft.net/{subDir}/{hash}";
                    
                    await DownloadFileAsync(officialUrl, assetPath, -1, cancellationToken, false);
                    retrySuccess++;
                    
                    if (retrySuccess % 10 == 0)
                    {
                        App.LogInfo($"重试进度: {retrySuccess}/{failedAssets.Count}");
                    }
                }
                catch (Exception ex)
                {
                    retryFailed.Add(name);
                    App.LogError($"重试下载失败: {name} - {ex.Message}");
                }
            }
            
            if (retryFailed.Count > 0)
            {
                App.LogError($"重试后仍有 {retryFailed.Count} 个资源文件下载失败");
                App.LogInfo($"失败的文件: {string.Join(", ", retryFailed.Take(10))}{(retryFailed.Count > 10 ? "..." : "")}");
                throw new Exception($"有 {retryFailed.Count} 个资源文件下载失败，请检查网络连接后重试");
            }
            
            App.LogInfo($"重试成功，已下载 {retrySuccess} 个失败文件");
        }
        
        App.LogInfo($"资源下载完成，共 {totalAssets} 个文件");
    }

    private async Task DownloadFileAsync(string url, string filePath, long expectedSize, CancellationToken cancellationToken, bool reportProgress, double? fixedProgress = null)
    {
        if (_httpClient == null)
            return;

        int retryCount = 0;
        Exception? lastException = null;

        while (retryCount < MaxRetries)
        {
            try
            {
                await _downloadSemaphore.WaitAsync(cancellationToken);
                using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                
                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    _downloadSemaphore.Release();
                    retryCount++;
                    if (retryCount >= MaxRetries)
                    {
                        throw new Exception($"下载遇到 429 限流，已重试 {MaxRetries} 次");
                    }
                    var waitTime = Math.Min(2000 * retryCount, 10000);
                    App.LogInfo($"下载遇到 429 限流，等待 {waitTime/1000} 秒后重试 ({retryCount}/{MaxRetries}): {url}");
                    await Task.Delay(waitTime, cancellationToken);
                    continue;
                }
                
                response.EnsureSuccessStatusCode();

                var directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);
                
                var buffer = new byte[8192];
                long totalRead = 0;
                int read;

                while ((read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    totalRead += read;
                }

                if (reportProgress && DownloadProgressChanged != null)
                {
                    double progress;
                    if (fixedProgress.HasValue)
                    {
                        progress = fixedProgress.Value;
                    }
                    else if (expectedSize > 0)
                    {
                        progress = (double)totalRead / expectedSize * 100;
                    }
                    else if (response.Content.Headers.ContentLength > 0)
                    {
                        progress = (double)totalRead / response.Content.Headers.ContentLength.Value * 100;
                    }
                    else
                    {
                        progress = -1;
                    }
                    
                    DownloadProgressChanged?.Invoke(this, new DownloadProgressEventArgs
                    {
                        TotalBytes = expectedSize > 0 ? expectedSize : (response.Content.Headers.ContentLength ?? 0),
                        DownloadedBytes = totalRead,
                        FileName = Path.GetFileName(filePath),
                        Progress = progress
                    });
                }

                _downloadSemaphore.Release();
                return;
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                _downloadSemaphore.Release();
                retryCount++;
                if (retryCount >= MaxRetries)
                {
                    throw new Exception($"下载遇到 429 限流，已重试 {MaxRetries} 次");
                }
                var waitTime = Math.Min(2000 * retryCount, 10000);
                App.LogInfo($"下载遇到 429 限流，等待 {waitTime/1000} 秒后重试 ({retryCount}/{MaxRetries}): {url}");
                await Task.Delay(waitTime, cancellationToken);
                continue;
            }
            catch (Exception ex)
            {
                _downloadSemaphore.Release();
                retryCount++;
                lastException = ex;
                
                if (retryCount >= MaxRetries)
                {
                    break;
                }
                
                var waitTime = 1000 * retryCount;
                App.LogInfo($"下载失败，等待 {waitTime/1000} 秒后重试 ({retryCount}/{MaxRetries}): {url} - {ex.Message}");
                await Task.Delay(waitTime, cancellationToken);
                continue;
            }
        }
        
        if (lastException != null)
        {
            App.LogError($"下载文件失败，已重试 {MaxRetries} 次：{url}", lastException);
            throw new Exception($"下载失败：{Path.GetFileName(filePath)}，已重试 {MaxRetries} 次，请检查网络连接");
        }
    }

    private async Task DownloadClientAsync(VersionDetail version, string clientJarPath, CancellationToken cancellationToken)
    {
        if (_httpClient == null || version.Downloads?.Client?.Url == null)
            return;

        var url = version.Downloads.Client.Url;
        if (UseMirror)
        {
            url = url.Replace("https://launcher.mojang.com", BMCLAPI_BASE)
                    .Replace("https://piston-data.mojang.com", BMCLAPI_BASE);
        }

        var expectedSize = version.Downloads.Client.Size;
        App.LogInfo($"正在下载客户端：{url} (大小: {expectedSize / 1024.0 / 1024.0:F1} MB)");
        await DownloadFileAsync(url, clientJarPath, expectedSize, cancellationToken, true);
    }

    private async Task DownloadLibrariesAsync(VersionDetail version, string librariesDir, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(librariesDir))
        {
            Directory.CreateDirectory(librariesDir);
        }

        var libraries = version.Libraries;
        if (libraries == null)
            return;

        var libsToDownload = new List<(string Name, string GroupId, string ArtifactId, string Version, string? Classifier, string Extension, string FilePath, string MavenPath)>();
        
        foreach (var library in libraries)
        {
            if (!IsLibraryAllowed(library))
                continue;

            var name = library.Name;
            var parts = name.Split(':');
            if (parts.Length < 3)
                continue;

            var groupId = parts[0];
            var artifactId = parts[1];
            var libVersion = parts[2];
            var extension = "jar";

            var mainJarDownloaded = false;
            
            if (library.Downloads?.Artifact?.Path != null)
            {
                var artifactPath = library.Downloads.Artifact.Path;
                var normalizedPath = artifactPath.Replace('/', Path.DirectorySeparatorChar);
                var filePath = Path.Combine(librariesDir, normalizedPath);
                var directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                if (!File.Exists(filePath))
                {
                    libsToDownload.Add((name, groupId, artifactId, libVersion, null, extension, filePath, artifactPath));
                }
                mainJarDownloaded = true;
            }
            
            if (!mainJarDownloaded && !IsNativesOnlyLibrary(library))
            {
                var groupPath = groupId.Replace('.', '/');
                var mavenPath = $"{groupPath}/{artifactId}/{libVersion}/{artifactId}-{libVersion}.jar";
                var normalizedPath = mavenPath.Replace('/', Path.DirectorySeparatorChar);
                var filePath = Path.Combine(librariesDir, normalizedPath);
                var directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                if (!File.Exists(filePath))
                {
                    App.LogInfo($"库 {name} 无 Artifact.Path，使用 Maven 格式下载: {mavenPath}");
                    libsToDownload.Add((name, groupId, artifactId, libVersion, null, extension, filePath, mavenPath));
                }
            }

            // 处理 natives 文件（从 Downloads.Classifiers 获取）
            if (library.Downloads?.Classifiers != null)
            {
                var classifiers = library.Downloads.Classifiers;
                var nativesList = new[] 
                { 
                    classifiers.NativesWindows,
                    classifiers.NativesWindows64,
                    classifiers.NativesWindows32,
                    classifiers.NativesLinux,
                    classifiers.NativesMacos
                };

                foreach (var native in nativesList)
                {
                    if (native?.Path != null)
                    {
                        // 直接使用版本 JSON 中的 Path，确保与启动时一致
                        var nativePath = native.Path.Replace('/', Path.DirectorySeparatorChar);
                        var nativeFilePath = Path.Combine(librariesDir, nativePath);
                        var nativeDir = Path.GetDirectoryName(nativeFilePath);
                        if (!string.IsNullOrEmpty(nativeDir) && !Directory.Exists(nativeDir))
                        {
                            Directory.CreateDirectory(nativeDir);
                        }

                        if (!File.Exists(nativeFilePath))
                        {
                            // 使用原始的 library name 作为标识
                            libsToDownload.Add((name, groupId, artifactId, libVersion, null, extension, nativeFilePath, native.Path));
                        }
                    }
                }
            }
        }

        if (libsToDownload.Count == 0)
        {
            App.LogInfo("所有库文件都已存在，跳过下载");
            return;
        }

        var totalLibs = libsToDownload.Count;
        var downloadedLibs = 0;
        var tasks = new List<Task>();
        var failedLibs = new List<string>();

        App.LogInfo($"准备下载 {totalLibs} 个库文件，模式：{(UseMultiThreadDownload ? "多线程" : "单线程")}");
        App.LogInfo($"镜像源列表：{string.Join(", ", MAVEN_MIRRORS)}");
        App.LogInfo($"信号量并发数：{_downloadSemaphore?.CurrentCount ?? 0}");

        foreach (var lib in libsToDownload)
        {
            tasks.Add(Task.Run(async () =>
            {
                var libName = lib.Name;
                try
                {
                    // 检查文件是否已存在
                    if (File.Exists(lib.FilePath))
                    {
                        App.LogInfo($"库文件已存在，跳过：{libName}");
                        var count = Interlocked.Increment(ref downloadedLibs);
                        DownloadProgressChanged?.Invoke(this, new DownloadProgressEventArgs
                        {
                            TotalBytes = totalLibs,
                            DownloadedBytes = count,
                            FileName = $"库文件 ({count}/{totalLibs})",
                            Progress = 10 + ((double)count / totalLibs * 50)
                        });
                        return;
                    }
                    
                    App.LogInfo($"开始下载库：{libName}");
                    
                    // 直接使用传递过来的 MavenPath，确保与启动时路径一致
                    var mavenPath = lib.MavenPath;
                    var fileName = Path.GetFileName(lib.FilePath);
                    
                    // 尝试从多个镜像源下载
                    bool downloaded = false;
                    foreach (var mirror in MAVEN_MIRRORS)
                    {
                        var url = $"{mirror}/{mavenPath}";
                        
                        // libraries.minecraft.net 需要特殊的 URL 格式（与标准 Maven 格式相同）
                        // 已经在上面处理了 GroupId 的转换
                        
                        try
                        {
                            App.LogInfo($"尝试从 {mirror} 下载 {fileName}");
                            await DownloadFileAsync(url, lib.FilePath, -1, cancellationToken, false);
                            App.LogInfo($"下载库：{lib.Name} (镜像：{mirror})");
                            downloaded = true;
                            break;
                        }
                        catch (Exception ex)
                        {
                            App.LogInfo($"镜像 {mirror} 下载失败：{ex.Message}");
                            continue;
                        }
                    }
                    
                    if (!downloaded)
                    {
                        App.LogError($"所有镜像源都无法下载：{lib.Name}");
                        failedLibs.Add(lib.Name);
                    }
                    
                    var downloadCount = Interlocked.Increment(ref downloadedLibs);
                    App.LogInfo($"库文件下载完成：{libName} ({downloadCount}/{totalLibs})");
                    // 每个文件下载完成都更新进度
                    DownloadProgressChanged?.Invoke(this, new DownloadProgressEventArgs
                    {
                        TotalBytes = totalLibs,
                        DownloadedBytes = downloadCount,
                        FileName = $"库文件 ({downloadCount}/{totalLibs})",
                        Progress = 10 + ((double)downloadCount / totalLibs * 50) // 10% -> 60%
                    });
                }
                catch (Exception ex)
                {
                    App.LogError($"下载库异常：{lib.Name}", ex);
                    var errorCount = Interlocked.Increment(ref downloadedLibs);
                    DownloadProgressChanged?.Invoke(this, new DownloadProgressEventArgs
                    {
                        TotalBytes = totalLibs,
                        DownloadedBytes = errorCount,
                        FileName = $"库文件 ({errorCount}/{totalLibs})",
                        Progress = 10 + ((double)errorCount / totalLibs * 50)
                    });
                }
            }, cancellationToken));
        }

        await Task.WhenAll(tasks);
        
        if (failedLibs.Count > 0)
        {
            App.LogError($"共有 {failedLibs.Count} 个库文件下载失败：");
            foreach (var failedLib in failedLibs.Take(10))
            {
                App.LogError($"  - {failedLib}");
            }
            if (failedLibs.Count > 10)
            {
                App.LogError($"  ... 还有 {failedLibs.Count - 10} 个文件");
            }
            throw new Exception($"有 {failedLibs.Count} 个库文件下载失败，请检查网络连接或镜像源配置");
        }
        
        App.LogInfo($"库文件下载完成，共 {totalLibs} 个文件，成功 {totalLibs - failedLibs.Count} 个");
    }

    private bool IsNativesOnlyLibrary(Library library)
    {
        if (library.Natives == null || library.Natives.Count == 0)
            return false;
        
        if (library.Downloads?.Artifact != null)
            return false;
        
        if (library.Downloads?.Classifiers == null)
            return false;
        
        return true;
    }

    private bool IsLibraryAllowed(Library library)
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

    private string GetGameDirectory(string gameDir, string versionId)
    {
        return Path.Combine(gameDir, ".minecraft", "versions", versionId);
    }

    public async Task<bool> VerifyAssetsAsync(string gameDir, string assetsId, CancellationToken cancellationToken = default)
    {
        var assetsDir = Path.Combine(gameDir, "assets");
        var indexFilePath = Path.Combine(assetsDir, "indexes", $"{assetsId}.json");
        
        if (!File.Exists(indexFilePath))
        {
            App.LogError($"资源索引文件不存在: {indexFilePath}");
            return false;
        }

        try
        {
            var indexJson = await File.ReadAllTextAsync(indexFilePath, cancellationToken);
            var assetIndex = JsonConvert.DeserializeObject<AssetIndexContent>(indexJson);
            
            if (assetIndex?.Objects == null)
            {
                App.LogError("资源索引解析失败");
                return false;
            }

            var objectsDir = Path.Combine(assetsDir, "objects");
            var missingCount = 0;
            var totalCount = assetIndex.Objects.Count;

            App.LogInfo($"正在验证资源文件完整性，共 {totalCount} 个文件...");

            foreach (var kvp in assetIndex.Objects)
            {
                var asset = kvp.Value;
                var hash = asset.Hash;
                var subDir = hash.Substring(0, 2);
                var assetPath = Path.Combine(objectsDir, subDir, hash);

                if (!File.Exists(assetPath))
                {
                    missingCount++;
                    if (missingCount <= 10)
                    {
                        App.LogInfo($"缺失资源: {kvp.Key}");
                    }
                }
            }

            if (missingCount > 0)
            {
                App.LogError($"资源验证失败: 缺失 {missingCount}/{totalCount} 个文件");
                return false;
            }

            App.LogInfo($"资源验证通过: {totalCount} 个文件完整");
            return true;
        }
        catch (Exception ex)
        {
            App.LogError("验证资源文件时出错", ex);
            return false;
        }
    }

    private List<string> GetAssetDownloadUrls(string subDir, string hash)
    {
        var urls = new List<string>();
        
        if (UseMirror)
        {
            urls.Add($"https://bmclapi2.bangbang93.com/assets/{subDir}/{hash}");
            urls.Add($"https://bmclapi.bangbang93.com/assets/{subDir}/{hash}");
            urls.Add($"https://mirrors4.qlu.edu.cn/bmclapi/assets/{subDir}/{hash}");
        }
        
        urls.Add($"https://resources.download.minecraft.net/{subDir}/{hash}");
        
        return urls;
    }

    public async Task<bool> VerifyLibrariesAsync(VersionDetail version, string librariesDir, CancellationToken cancellationToken = default)
    {
        if (version.Libraries == null)
            return true;

        var missingCount = 0;
        var totalCount = 0;

        App.LogInfo("正在验证库文件完整性...");

        foreach (var library in version.Libraries)
        {
            if (!IsLibraryAllowed(library))
                continue;

            var name = library.Name;
            var parts = name.Split(':');
            if (parts.Length < 3)
                continue;

            totalCount++;

            if (library.Downloads?.Artifact?.Path != null)
            {
                var artifactPath = library.Downloads.Artifact.Path;
                var normalizedPath = artifactPath.Replace('/', Path.DirectorySeparatorChar);
                var filePath = Path.Combine(librariesDir, normalizedPath);

                if (!File.Exists(filePath))
                {
                    missingCount++;
                    App.LogInfo($"缺失库文件: {name}");
                }
            }

            if (library.Downloads?.Classifiers != null)
            {
                var classifiers = library.Downloads.Classifiers;
                var nativesList = new[] 
                { 
                    classifiers.NativesWindows,
                    classifiers.NativesWindows64,
                    classifiers.NativesWindows32,
                    classifiers.NativesLinux,
                    classifiers.NativesMacos
                };

                foreach (var native in nativesList)
                {
                    if (native?.Path != null)
                    {
                        var nativePath = native.Path.Replace('/', Path.DirectorySeparatorChar);
                        var nativeFilePath = Path.Combine(librariesDir, nativePath);

                        if (!File.Exists(nativeFilePath))
                        {
                            missingCount++;
                            App.LogInfo($"缺失 native 文件: {native.Path}");
                        }
                    }
                }
            }
        }

        if (missingCount > 0)
        {
            App.LogError($"库文件验证失败: 缺失 {missingCount} 个文件");
            return false;
        }

        App.LogInfo($"库文件验证通过: {totalCount} 个文件完整");
        return true;
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
        _downloadSemaphore?.Dispose();
    }
}
