using System;
using System.IO;

namespace ResourceRouter.Infrastructure.Storage;

public static class LocalPathProvider
{
    private const string RootFolderName = "ResourceRouter";
    private const string RawFolderName = "raw";
    private const string ProcessedFolderName = "processed";
    private const string ThumbsFolderName = "thumbs";
    private const string TempFolderName = "tmp";
    private const string DbFolderName = "db";
    private const string LogsFolderName = "logs";
    private const string PluginsFolderName = "plugins";
    private const string ConfigResourcesFolderName = "config-resources";

    private const string ConfigHubTempFolderName = "config-hub";
    private const string AudioTranscriptionTempFolderName = "audio-transcription";
    private const string TestTempFolderName = "tests";

    public static string RootDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), RootFolderName);

    public static string RawDirectory => Path.Combine(RootDirectory, RawFolderName);

    public static string ProcessedDirectory => Path.Combine(RootDirectory, ProcessedFolderName);

    public static string ThumbsDirectory => Path.Combine(RootDirectory, ThumbsFolderName);

    public static string TempDirectory => Path.Combine(RootDirectory, TempFolderName);

    public static string DbDirectory => Path.Combine(RootDirectory, DbFolderName);

    public static string LogsDirectory => Path.Combine(RootDirectory, LogsFolderName);

    public static string PluginsDirectory => Path.Combine(RootDirectory, PluginsFolderName);

    public static string ConfigResourcesDirectory => Path.Combine(RootDirectory, ConfigResourcesFolderName);

    public static string GetRawResourceDirectory(Guid resourceId)
    {
        return Path.Combine(RawDirectory, resourceId.ToString("N"));
    }

    public static string GetProcessedResourceDirectory(Guid resourceId)
    {
        return Path.Combine(ProcessedDirectory, resourceId.ToString("N"));
    }

    public static string ConfigHubTempDirectory => EnsureTempSubdirectory(ConfigHubTempFolderName);

    public static string AudioTranscriptionTempDirectory => EnsureTempSubdirectory(AudioTranscriptionTempFolderName);

    public static string TestTempDirectory => EnsureTempSubdirectory(TestTempFolderName);

    public static string EnsureTempSubdirectory(string folderName)
    {
        if (string.IsNullOrWhiteSpace(folderName))
        {
            throw new ArgumentException("folderName is required.", nameof(folderName));
        }

        var path = Path.Combine(TempDirectory, folderName);
        Directory.CreateDirectory(path);
        return path;
    }

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
        Directory.CreateDirectory(ConfigResourcesDirectory);
        Directory.CreateDirectory(Path.Combine(TempDirectory, ConfigHubTempFolderName));
        Directory.CreateDirectory(Path.Combine(TempDirectory, AudioTranscriptionTempFolderName));
        Directory.CreateDirectory(Path.Combine(TempDirectory, TestTempFolderName));
    }
}