using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using MinecraftLauncher.Models;

namespace MinecraftLauncher.Services;

public class DownloadService : IDisposable
{
    private HttpClient? _httpClient;
    private readonly int _maxConcurrent;
    private readonly long _speedLimit;
    private bool _useMirror;
    private const int _retryDelayMs = 1000;
    private const int _maxRetries = 3;
    public const long DefaultSpeedLimit = 0;

    private static readonly string[] MAVEN_MIRRORS = new[]
    {
        "https://bmclapi2.bangbang93.com/maven",
        "https://bmclapi.bangbang93.com/maven",
        "https://mirrors4.qlu.edu.cn/bmclapi",
        "https://libraries.minecraft.net",
        "https://repo.lwjgl.org",
        "https://maven.aliyun.com/repository/public",
        "https://repo1.maven.org/maven2"
    };

    private static readonly string[] ASSET_MIRRORS = new[]
    {
        "https://bmclapi2.bangbang93.com/assets",
        "https://bmclapi.bangbang93.com/assets"
    };

    private static readonly string[] VERSION_MIRRORS = new[]
    {
        "https://bmclapi2.bangbang93.com",
        "https://bmclapi.bangbang93.com"
    };

    public event EventHandler<DownloadProgressEventArgs>? BatchProgressChanged;
    public event EventHandler<DownloadTask>? TaskCompleted;
    public event EventHandler<DownloadTask>? TaskFailed;

    public bool UseMirror
    {
        get => _useMirror;
        init
        {
            _useMirror = value;
            App.LogInfo($"镜像源已{(value ? "启用" : "禁用")}");
        }
    }

    public DownloadService(int maxConcurrent = 32, long speedLimitBytesPerSecond = 0, bool useMirror = true)
    {
        _maxConcurrent = maxConcurrent;
        _speedLimit = speedLimitBytesPerSecond;
        _useMirror = useMirror;
        
        InitializeHttpClient();
        App.LogInfo($"下载服务初始化完成: 并发数={maxConcurrent}, 限速={speedLimitBytesPerSecond}/s, 镜像={(useMirror ? "启用" : "禁用")}");
    }

    private void InitializeHttpClient()
    {
        try
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                AllowAutoRedirect = true,
                ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true,
                MaxConnectionsPerServer = 500
            };
            
            _httpClient = new HttpClient(handler);
            _httpClient.Timeout = TimeSpan.FromMinutes(30);
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Novo-Launcher/1.0");
        }
        catch (Exception ex)
        {
            App.LogError("HttpClient 初始化失败", ex);
            throw new InvalidOperationException("无法初始化下载服务，请检查网络配置", ex);
        }
    }

    public void SetSpeedLimit(long bytesPerSecond)
    {
        App.LogInfo($"下载限速设置为: {bytesPerSecond}/s");
    }

    public void SetMaxConcurrent(int maxConcurrent)
    {
        App.LogInfo($"最大并发数设置为: {maxConcurrent}");
    }

    public DownloadBatch CreateBatch(string name, int maxConcurrent = 16)
    {
        return new DownloadBatch
        {
            Id = Guid.NewGuid().ToString(),
            Name = name,
            MaxConcurrent = maxConcurrent,
            CancellationTokenSource = new CancellationTokenSource()
        };
    }

    public void AddLibraryDownload(DownloadBatch batch, string mavenPath, string localPath, long expectedSize = 0, string? expectedHash = null)
    {
        var urls = new List<string>();
        
        foreach (var mirror in MAVEN_MIRRORS)
        {
            urls.Add($"{mirror}/{mavenPath}");
        }

        var task = new DownloadTask
        {
            Id = Guid.NewGuid().ToString(),
            Url = urls[0],
            LocalPath = localPath,
            ExpectedSize = expectedSize,
            ExpectedHash = expectedHash,
            Priority = DownloadPriority.Normal,
            Category = "Library",
            MirrorUrls = urls,
            MaxRetries = _maxRetries
        };

        batch.Tasks.Add(task);
    }

    public void AddAssetDownload(DownloadBatch batch, string hash, string localPath, long expectedSize = 0)
    {
        var subDir = hash.Substring(0, 2);
        var urls = new List<string>();

        foreach (var mirror in ASSET_MIRRORS)
        {
            urls.Add($"{mirror}/{subDir}/{hash}");
        }
        
        urls.Add($"https://resources.download.minecraft.net/{subDir}/{hash}");

        var task = new DownloadTask
        {
            Id = hash,
            Url = urls[0],
            LocalPath = localPath,
            ExpectedSize = expectedSize,
            ExpectedHash = hash,
            Priority = DownloadPriority.Low,
            Category = "Asset",
            MirrorUrls = urls,
            MaxRetries = _maxRetries
        };

        batch.Tasks.Add(task);
    }

    public void AddClientDownload(DownloadBatch batch, string originalUrl, string localPath, long expectedSize = 0, string? expectedHash = null)
    {
        var urls = new List<string> { originalUrl };

        if (UseMirror)
        {
            foreach (var mirror in VERSION_MIRRORS)
            {
                var mirrorUrl = originalUrl
                    .Replace("https://launchermeta.mojang.com", mirror)
                    .Replace("https://launcher.mojang.com", mirror)
                    .Replace("https://piston-meta.mojang.com", mirror)
                    .Replace("https://piston-data.mojang.com", mirror);
                
                if (!urls.Contains(mirrorUrl))
                {
                    urls.Add(mirrorUrl);
                }
            }
        }

        var task = new DownloadTask
        {
            Id = Guid.NewGuid().ToString(),
            Url = urls[0],
            LocalPath = localPath,
            ExpectedSize = expectedSize,
            ExpectedHash = expectedHash,
            Priority = DownloadPriority.High,
            Category = "Client",
            MirrorUrls = urls,
            MaxRetries = _maxRetries
        };

        batch.Tasks.Add(task);
    }

    public async Task<bool> ExecuteBatchAsync(DownloadBatch batch, CancellationToken cancellationToken = default)
    {
        var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, 
            batch.CancellationTokenSource?.Token ?? CancellationToken.None);

        var semaphore = new SemaphoreSlim(batch.MaxConcurrent, batch.MaxConcurrent);
        var failedTasks = new ConcurrentBag<DownloadTask>();
        var completedCount = 0;
        var totalCount = batch.Tasks.Count;
        var totalBytes = batch.TotalBytes;
        var downloadedBytes = 0L;
        var speedLimiter = new SpeedLimiter(_speedLimit);
        var startTime = DateTime.Now;

        App.LogInfo($"开始下载批次: {batch.Name}, 共 {totalCount} 个文件");

        var tasks = batch.Tasks.Select(async downloadTask =>
        {
            await semaphore.WaitAsync(combinedCts.Token);
            try
            {
                downloadTask.Status = DownloadStatus.Downloading;
                downloadTask.StartTime = DateTime.Now;

                var success = await DownloadWithRetryAsync(downloadTask, combinedCts.Token, speedLimiter);

                if (success)
                {
                    downloadTask.Status = DownloadStatus.Completed;
                    downloadTask.EndTime = DateTime.Now;
                    TaskCompleted?.Invoke(this, downloadTask);
                }
                else
                {
                    downloadTask.Status = DownloadStatus.Failed;
                    failedTasks.Add(downloadTask);
                    TaskFailed?.Invoke(this, downloadTask);
                }

                var currentCompleted = Interlocked.Increment(ref completedCount);
                var currentDownloaded = Interlocked.Add(ref downloadedBytes, downloadTask.DownloadedBytes);
                var elapsed = DateTime.Now - startTime;
                var speed = elapsed.TotalSeconds > 0 ? currentDownloaded / elapsed.TotalSeconds : 0;
                var remaining = speed > 0 ? TimeSpan.FromSeconds((totalBytes - currentDownloaded) / speed) : TimeSpan.Zero;

                BatchProgressChanged?.Invoke(this, new DownloadProgressEventArgs
                {
                    Batch = batch,
                    Task = downloadTask,
                    CompletedCount = currentCompleted,
                    TotalCount = totalCount,
                    DownloadedBytes = currentDownloaded,
                    TotalBytes = totalBytes,
                    FileName = Path.GetFileName(downloadTask.LocalPath),
                    Progress = totalBytes > 0 ? (double)currentDownloaded / totalBytes * 100 : 0,
                    Speed = speed,
                    EstimatedTimeRemaining = remaining
                });

                return success;
            }
            finally
            {
                semaphore.Release();
            }
        }).ToList();

        var results = await Task.WhenAll(tasks);
        var successCount = results.Count(r => r);

        if (failedTasks.Count > 0)
        {
            App.LogInfo($"下载完成: 成功 {successCount}/{totalCount}, 失败 {failedTasks.Count}");
            
            foreach (var failed in failedTasks.Take(5))
            {
                App.LogInfo($"  失败: {Path.GetFileName(failed.LocalPath)} - {failed.ErrorMessage}");
            }
            
            if (failedTasks.Count > 5)
            {
                App.LogInfo($"  ... 还有 {failedTasks.Count - 5} 个失败");
            }
        }
        else
        {
            App.LogInfo($"下载完成: 全部成功 ({successCount}/{totalCount})");
        }

        return failedTasks.Count == 0;
    }

    private async Task<bool> DownloadWithRetryAsync(DownloadTask task, CancellationToken cancellationToken, SpeedLimiter speedLimiter)
    {
        var retryCount = 0;

        while (retryCount < task.MaxRetries)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                task.Status = DownloadStatus.Cancelled;
                return false;
            }

            if (File.Exists(task.LocalPath))
            {
                if (string.IsNullOrEmpty(task.ExpectedHash) || VerifyFileHash(task.LocalPath, task.ExpectedHash))
                {
                    var fileInfo = new FileInfo(task.LocalPath);
                    task.DownloadedBytes = fileInfo.Length;
                    return true;
                }
                
                try
                {
                    File.Delete(task.LocalPath);
                }
                catch { }
            }

            var mirrorIndex = Math.Min(task.CurrentMirrorIndex + retryCount, task.MirrorUrls.Count - 1);
            var url = task.MirrorUrls[mirrorIndex];

            try
            {
                var success = await DownloadFileAsync(url, task.LocalPath, task, cancellationToken, speedLimiter);
                
                if (success)
                {
                    if (!string.IsNullOrEmpty(task.ExpectedHash) && !VerifyFileHash(task.LocalPath, task.ExpectedHash))
                    {
                        App.LogInfo($"文件校验失败: {Path.GetFileName(task.LocalPath)}");
                        File.Delete(task.LocalPath);
                        throw new Exception("文件校验失败");
                    }
                    return true;
                }
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
            {
                task.ErrorMessage = "访问被拒绝 (403)";
                App.LogInfo($"下载失败 (403 Forbidden): {url}");
                return false;
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests)
            {
                App.LogInfo($"请求过多 (429), 等待 10 秒后重试: {url}");
                await Task.Delay(10000, cancellationToken);
            }
            catch (Exception ex)
            {
                task.ErrorMessage = ex.Message;
                App.LogInfo($"下载失败 ({retryCount + 1}/{task.MaxRetries}): {url} - {ex.Message}");
            }

            retryCount++;
            task.RetryCount = retryCount;

            if (retryCount < task.MaxRetries)
            {
                await Task.Delay(_retryDelayMs * retryCount, cancellationToken);
            }
        }

        return false;
    }

    private async Task<bool> DownloadFileAsync(string url, string localPath, DownloadTask task, CancellationToken cancellationToken, SpeedLimiter speedLimiter)
    {
        if (_httpClient == null) return false;

        var directory = Path.GetDirectoryName(localPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        try
        {
            using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            var contentLength = response.Content.Headers.ContentLength ?? 0;
            if (task.ExpectedSize == 0) task.ExpectedSize = contentLength;

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var fileStream = new FileStream(localPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);
            
            var buffer = new byte[8192];
            long totalRead = 0;
            int read;

            while ((read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
            {
                await speedLimiter.WaitAsync(read, cancellationToken);
                await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                totalRead += read;
                task.DownloadedBytes = totalRead;
            }

            return true;
        }
        catch (Exception ex)
        {
            task.ErrorMessage = ex.Message;
            throw;
        }
    }

    private bool VerifyFileHash(string filePath, string expectedHash)
    {
        try
        {
            using var stream = File.OpenRead(filePath);
            using var sha1 = System.Security.Cryptography.SHA1.Create();
            var hashBytes = sha1.ComputeHash(stream);
            var actualHash = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
            return string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public void CancelBatch(DownloadBatch batch)
    {
        batch.CancellationTokenSource?.Cancel();
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
    }
}

internal class SpeedLimiter
{
    private readonly long _bytesPerSecond;
    private readonly Stopwatch _stopwatch = new();
    private long _bytesThisSecond;
    private int _millisecondsWaited;

    public SpeedLimiter(long bytesPerSecond)
    {
        _bytesPerSecond = bytesPerSecond;
        _stopwatch.Start();
    }

    public async Task WaitAsync(int bytes, CancellationToken cancellationToken)
    {
        if (_bytesPerSecond <= 0) return;

        _bytesThisSecond += bytes;

        if (_bytesThisSecond >= _bytesPerSecond)
        {
            var elapsed = _stopwatch.ElapsedMilliseconds - _millisecondsWaited;
            var expectedTime = _bytesThisSecond * 1000.0 / _bytesPerSecond;
            var waitTime = (int)(expectedTime - elapsed);

            if (waitTime > 0)
            {
                await Task.Delay(waitTime, cancellationToken);
                _millisecondsWaited += waitTime;
            }

            if (_stopwatch.ElapsedMilliseconds - _millisecondsWaited >= 1000)
            {
                _bytesThisSecond = 0;
                _millisecondsWaited = (int)_stopwatch.ElapsedMilliseconds;
            }
        }
    }
}
