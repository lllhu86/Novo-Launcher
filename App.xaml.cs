﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿using System.IO;
using System.Net;
using System.Windows;
using System.Windows.Threading;

namespace MinecraftLauncher;

public partial class App : Application
{
    private static readonly string LogFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
    
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        
        // 设置 TLS 1.2 和 TLS 1.3 支持
        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls13;
        ServicePointManager.ServerCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) => true;
        
        InitializeLogger();
        ClearCache();
        
        DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
        
        LogInfo("应用程序启动");
    }

    private void InitializeLogger()
    {
        if (!Directory.Exists(LogFolder))
        {
            Directory.CreateDirectory(LogFolder);
        }
    }

    private void ClearCache()
    {
        try
        {
            var cacheFolders = new[]
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "obj")
            };

            foreach (var folder in cacheFolders)
            {
                if (Directory.Exists(folder))
                {
                    LogInfo($"清理缓存文件夹: {folder}");
                    try
                    {
                        Directory.Delete(folder, true);
                    }
                    catch (Exception ex)
                    {
                        LogError($"清理失败: {ex.Message}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            LogError($"清理缓存时出错: {ex.Message}");
        }
    }

    private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogError("UI 线程异常", e.Exception);
        MessageBox.Show($"发生错误：{e.Exception.Message}\n\n详细信息已记录到日志文件。", 
            "NMCL 错误", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var exception = e.ExceptionObject as Exception;
        LogError("应用程序域异常", exception);
        MessageBox.Show($"发生严重错误：{exception?.Message}\n\n详细信息已记录到日志文件。", 
            "NMCL 严重错误", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        LogError("异步任务异常", e.Exception);
        MessageBox.Show($"异步任务错误：{e.Exception.Message}\n\n详细信息已记录到日志文件。", 
            "NMCL 错误", MessageBoxButton.OK, MessageBoxImage.Error);
        e.SetObserved();
    }

    public static void LogInfo(string message)
    {
        WriteLog("INFO", message);
    }

    public static void LogError(string message, Exception? exception = null)
    {
        var logMessage = message;
        if (exception != null)
        {
            logMessage += $"\n{exception.GetType().Name}: {exception.Message}\n{exception.StackTrace}";
        }
        WriteLog("ERROR", logMessage);
    }

    private static void WriteLog(string level, string message)
    {
        try
        {
            var logFile = Path.Combine(LogFolder, $"NovoLauncher_{DateTime.Now:yyyyMMdd}.log");
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            var logEntry = $"[{timestamp}] [{level}] {message}\r\n";
            
            File.AppendAllText(logFile, logEntry);
        }
        catch
        {
        }
    }
}
