using System.Security.Cryptography;
using System.Text;

namespace MinecraftLauncher.Models;

public enum DownloadPriority
{
    Critical = 0,
    High = 1,
    Normal = 2,
    Low = 3
}

public enum DownloadStatus
{
    Pending,
    Downloading,
    Completed,
    Failed,
    Cancelled
}

public class DownloadTask
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Url { get; set; } = string.Empty;
    public string LocalPath { get; set; } = string.Empty;
    public string? ExpectedHash { get; set; }
    public long ExpectedSize { get; set; }
    public DownloadPriority Priority { get; set; } = DownloadPriority.Normal;
    public DownloadStatus Status { get; set; } = DownloadStatus.Pending;
    public string? ErrorMessage { get; set; }
    public int RetryCount { get; set; }
    public int MaxRetries { get; set; } = 3;
    public long DownloadedBytes { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public List<string> MirrorUrls { get; set; } = new();
    public int CurrentMirrorIndex { get; set; }
    public string? Category { get; set; }

    public double Progress => ExpectedSize > 0 ? (double)DownloadedBytes / ExpectedSize * 100 : 0;
    public TimeSpan? Duration => EndTime?.Subtract(StartTime);
    public string CurrentUrl => MirrorUrls.Count > 0 && CurrentMirrorIndex < MirrorUrls.Count 
        ? MirrorUrls[CurrentMirrorIndex] 
        : Url;
}

public class DownloadBatch
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public List<DownloadTask> Tasks { get; set; } = new();
    public int MaxConcurrent { get; set; } = 16;
    public CancellationTokenSource? CancellationTokenSource { get; set; }
    
    public int TotalTasks => Tasks.Count;
    public int CompletedTasks => Tasks.Count(t => t.Status == DownloadStatus.Completed);
    public int FailedTasks => Tasks.Count(t => t.Status == DownloadStatus.Failed);
    public int PendingTasks => Tasks.Count(t => t.Status == DownloadStatus.Pending);
    public long TotalBytes => Tasks.Sum(t => t.ExpectedSize);
    public long DownloadedBytes => Tasks.Sum(t => t.DownloadedBytes);
    public double OverallProgress => TotalBytes > 0 ? (double)DownloadedBytes / TotalBytes * 100 : 0;
}

public class DownloadProgressEventArgs : EventArgs
{
    public DownloadBatch? Batch { get; set; }
    public DownloadTask? Task { get; set; }
    public int CompletedCount { get; set; }
    public int TotalCount { get; set; }
    public long TotalBytes { get; set; }
    public long DownloadedBytes { get; set; }
    public string FileName { get; set; } = string.Empty;
    public double Progress { get; set; }
    public double Speed { get; set; }
    public TimeSpan? EstimatedTimeRemaining { get; set; }
}

public class DownloadResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public int FailedCount { get; set; }
    public List<string> FailedFiles { get; set; } = new();
}
