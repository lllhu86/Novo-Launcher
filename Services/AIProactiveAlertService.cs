using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MinecraftLauncher.Models;

namespace MinecraftLauncher.Services
{
    public class AIProactiveAlertService : IDisposable
    {
        private readonly string _gameDir;
        private readonly HardwareService _hardwareService;
        private readonly SaveTrackingService _saveTrackingService;
        private readonly ModService _modService;
        private System.Timers.Timer? _checkTimer;
        private bool _disposed;
        private DateTime _lastBackupTime = DateTime.MinValue;

        public event Action<ProactiveAlert>? AlertTriggered;

        public AIProactiveAlertService(string gameDir)
        {
            _gameDir = gameDir;
            _hardwareService = new HardwareService();
            _saveTrackingService = SaveTrackingService.Instance;
            _modService = new ModService();
        }

        public void StartMonitoring()
        {
            _checkTimer = new System.Timers.Timer(300000);
            _checkTimer.Elapsed += async (s, e) => await CheckForAlertsAsync();
            _checkTimer.Start();
            
            App.LogInfo("[AI助手] 主动提醒服务已启动");
        }

        public void StopMonitoring()
        {
            _checkTimer?.Stop();
            _checkTimer?.Dispose();
            
            App.LogInfo("[AI助手] 主动提醒服务已停止");
        }

        public async Task<List<ProactiveAlert>> CheckForAlertsAsync()
        {
            var alerts = new List<ProactiveAlert>();

            try
            {
                alerts.AddRange(await CheckCompatibilityAlertsAsync());
                alerts.AddRange(await CheckPerformanceAlertsAsync());
                alerts.AddRange(await CheckBackupAlertsAsync());
                
                foreach (var alert in alerts)
                {
                    AlertTriggered?.Invoke(alert);
                    App.LogInfo($"[AI助手] 触发提醒: {alert.Title}");
                }
            }
            catch (Exception ex)
            {
                App.LogError("检查主动提醒失败", ex);
            }

            return alerts;
        }

        private async Task<List<ProactiveAlert>> CheckCompatibilityAlertsAsync()
        {
            var alerts = new List<ProactiveAlert>();

            try
            {
                var versionsDir = Path.Combine(_gameDir, "versions");
                if (!Directory.Exists(versionsDir))
                {
                    return alerts;
                }

                foreach (var versionDir in Directory.GetDirectories(versionsDir))
                {
                    var versionName = Path.GetFileName(versionDir);
                    var modsDir = Path.Combine(versionDir, "mods");
                    
                    if (!Directory.Exists(modsDir))
                    {
                        continue;
                    }

                    var modFiles = Directory.GetFiles(modsDir, "*.jar");
                    var hasOptifine = modFiles.Any(f => Path.GetFileNameWithoutExtension(f).ToLower().Contains("optifine"));
                    var hasSodium = modFiles.Any(f => Path.GetFileNameWithoutExtension(f).ToLower().Contains("sodium"));

                    if (hasOptifine && hasSodium)
                    {
                        alerts.Add(new ProactiveAlert
                        {
                            AlertType = "compatibility",
                            Title = "⚠️ 模组兼容性警告",
                            Message = $"检测到「{versionName}」版本同时安装了 OptiFine 和 Sodium，这两个模组可能存在兼容性问题。",
                            Severity = "warning",
                            Actions = new List<AlertAction>
                            {
                                new AlertAction
                                {
                                    Label = "禁用 Sodium",
                                    Action = "disable_mod",
                                    Parameters = { ["mod_name"] = "Sodium", ["version"] = versionName }
                                },
                                new AlertAction
                                {
                                    Label = "禁用 OptiFine",
                                    Action = "disable_mod",
                                    Parameters = { ["mod_name"] = "OptiFine", ["version"] = versionName }
                                },
                                new AlertAction
                                {
                                    Label = "忽略",
                                    Action = "ignore",
                                    Parameters = { }
                                }
                            }
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                App.LogError("检查兼容性提醒失败", ex);
            }

            return alerts;
        }

        private async Task<List<ProactiveAlert>> CheckPerformanceAlertsAsync()
        {
            var alerts = new List<ProactiveAlert>();

            try
            {
                var hardware = _hardwareService.GetHardwareInfo();

                if (hardware.TotalMemoryGB < 8)
                {
                    alerts.Add(new ProactiveAlert
                    {
                        AlertType = "performance",
                        Title = "💡 性能优化建议",
                        Message = $"检测到您的内存容量较小（{hardware.TotalMemoryGB:F1}GB），建议关闭部分后台应用或升级内存以获得更好的游戏体验。",
                        Severity = "info",
                        Actions = new List<AlertAction>
                        {
                            new AlertAction
                            {
                                Label = "查看优化建议",
                                Action = "show_optimization",
                                Parameters = { }
                            }
                        }
                    });
                }

                if (hardware.GpuMemory != null && hardware.GpuMemory.Contains("MB"))
                {
                    var gpuMemoryMB = int.Parse(hardware.GpuMemory.Replace("MB", "").Trim());
                    if (gpuMemoryMB < 2048)
                    {
                        alerts.Add(new ProactiveAlert
                        {
                            AlertType = "performance",
                            Title = "💡 显存不足警告",
                            Message = $"检测到您的显存较小（{hardware.GpuMemory}），建议降低游戏画质设置或使用低分辨率材质包。",
                            Severity = "warning",
                            Actions = new List<AlertAction>
                            {
                                new AlertAction
                                {
                                    Label = "应用低配设置",
                                    Action = "apply_low_settings",
                                    Parameters = { }
                                }
                            }
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                App.LogError("检查性能提醒失败", ex);
            }

            return alerts;
        }

        private async Task<List<ProactiveAlert>> CheckBackupAlertsAsync()
        {
            var alerts = new List<ProactiveAlert>();

            try
            {
                var saves = _saveTrackingService.GetSaveRecords();
                
                if (saves.Count == 0)
                {
                    return alerts;
                }

                var recentModifiedSaves = saves
                    .Where(s => (DateTime.Now - s.LastPlayedTime).TotalDays <= 3)
                    .ToList();

                if (recentModifiedSaves.Count > 0)
                {
                    var backupDir = Path.Combine(_gameDir, "backups");
                    var hasRecentBackup = false;
                    
                    if (Directory.Exists(backupDir))
                    {
                        var latestBackup = Directory.GetDirectories(backupDir)
                            .Select(d => Directory.GetLastWriteTime(d))
                            .OrderByDescending(t => t)
                            .FirstOrDefault();
                        
                        if (latestBackup != default && (DateTime.Now - latestBackup).TotalDays <= 3)
                        {
                            hasRecentBackup = true;
                        }
                    }

                    if (!hasRecentBackup)
                    {
                        alerts.Add(new ProactiveAlert
                        {
                            AlertType = "backup",
                            Title = "💾 备份提醒",
                            Message = $"你最近三天修改了 {recentModifiedSaves.Count} 个存档，建议立即备份以防止数据丢失。",
                            Severity = "info",
                            Actions = new List<AlertAction>
                            {
                                new AlertAction
                                {
                                    Label = "立即备份",
                                    Action = "backup_saves",
                                    Parameters = { ["saves"] = string.Join(",", recentModifiedSaves.Select(s => s.SaveName)) }
                                },
                                new AlertAction
                                {
                                    Label = "稍后提醒",
                                    Action = "remind_later",
                                    Parameters = { }
                                }
                            }
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                App.LogError("检查备份提醒失败", ex);
            }

            return alerts;
        }

        public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            StopMonitoring();
        }
    }
    }
}
