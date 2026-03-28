using System;
using System.IO;

namespace ResourceRouter.Infrastructure.Storage;

public static class LocalPathProvider
{
    public static string RootDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ResourceRouter");

    public static string RawDirectory => Path.Combine(RootDirectory, "raw");

    public static string ProcessedDirectory => Path.Combine(RootDirectory, "processed");

    public static string ThumbsDirectory => Path.Combine(RootDirectory, "thumbs");

    public static string TempDirectory => Path.Combine(RootDirectory, "tmp");

    public static string DbDirectory => Path.Combine(RootDirectory, "db");

    public static string LogsDirectory => Path.Combine(RootDirectory, "logs");

    public static string PluginsDirectory => Path.Combine(RootDirectory, "plugins");

    public static void EnsureAll()
    {
        Directory.CreateDirectory(RootDirectory);
        Directory.CreateDirectory(RawDirectory);
        Directory.CreateDirectory(ProcessedDirectory);
        Directory.CreateDirectory(ThumbsDirectory);
        Directory.CreateDirectory(TempDirectory);
        Directory.CreateDirectory(DbDirectory);
        Directory.CreateDirectory(LogsDirectory);
        Directory.CreateDirectory(PluginsDirectory);
    }
}