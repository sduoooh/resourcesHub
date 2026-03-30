using System;
using System.IO;

namespace ResourceRouter.Infrastructure.Storage;

public static class LocalPathProvider
{
    private const string ConfigHubTempFolderName = "config-hub";
    private const string AudioTranscriptionTempFolderName = "audio-transcription";
    private const string TestTempFolderName = "tests";

    public static string RootDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ResourceRouter");

    public static string RawDirectory => Path.Combine(RootDirectory, "raw");

    public static string ProcessedDirectory => Path.Combine(RootDirectory, "processed");

    public static string ThumbsDirectory => Path.Combine(RootDirectory, "thumbs");

    public static string TempDirectory => Path.Combine(RootDirectory, "tmp");

    public static string DbDirectory => Path.Combine(RootDirectory, "db");

    public static string LogsDirectory => Path.Combine(RootDirectory, "logs");

    public static string PluginsDirectory => Path.Combine(RootDirectory, "plugins");

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
        Directory.CreateDirectory(Path.Combine(TempDirectory, ConfigHubTempFolderName));
        Directory.CreateDirectory(Path.Combine(TempDirectory, AudioTranscriptionTempFolderName));
        Directory.CreateDirectory(Path.Combine(TempDirectory, TestTempFolderName));
    }
}