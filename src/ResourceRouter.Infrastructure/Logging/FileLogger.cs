using System;
using System.IO;
using System.Text;
using ResourceRouter.Core.Abstractions;

namespace ResourceRouter.Infrastructure.Logging;

public sealed class FileLogger : IAppLogger
{
    private readonly object _gate = new();

    public FileLogger(string? logDirectory = null)
    {
        LogDirectory = string.IsNullOrWhiteSpace(logDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ResourceRouter", "logs")
            : logDirectory;

        Directory.CreateDirectory(LogDirectory);
    }

    public string LogDirectory { get; }

    public void LogInfo(string message)
    {
        Write("INFO", message, null);
    }

    public void LogWarning(string message)
    {
        Write("WARN", message, null);
    }

    public void LogError(string message, Exception? exception = null)
    {
        Write("ERROR", message, exception);
    }

    private void Write(string level, string message, Exception? exception)
    {
        var line = $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {message}";
        if (exception is not null)
        {
            line += Environment.NewLine + exception;
        }

        var path = Path.Combine(LogDirectory, $"resource-router-{DateTime.Now:yyyyMMdd}.log");
        lock (_gate)
        {
            File.AppendAllText(path, line + Environment.NewLine, Encoding.UTF8);
        }
    }
}