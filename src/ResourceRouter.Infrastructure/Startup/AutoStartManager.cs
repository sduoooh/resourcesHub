using System;
using System.Runtime.Versioning;
using Microsoft.Win32;
using ResourceRouter.Core.Abstractions;

namespace ResourceRouter.Infrastructure.Startup;

[SupportedOSPlatform("windows")]
public sealed class AutoStartManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "ResourceRouter.App";

    private readonly IAppLogger? _logger;

    public AutoStartManager(IAppLogger? logger = null)
    {
        _logger = logger;
    }

    public bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            var value = key?.GetValue(AppName) as string;
            return !string.IsNullOrWhiteSpace(value);
        }
        catch (Exception ex)
        {
            _logger?.LogError("读取开机自启状态失败。", ex);
            return false;
        }
    }

    public void Enable(string executablePath)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
            key?.SetValue(AppName, $"\"{executablePath}\"");
        }
        catch (Exception ex)
        {
            _logger?.LogError("开启开机自启失败。", ex);
        }
    }

    public void Disable()
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
            key?.DeleteValue(AppName, throwOnMissingValue: false);
        }
        catch (Exception ex)
        {
            _logger?.LogError("关闭开机自启失败。", ex);
        }
    }
}