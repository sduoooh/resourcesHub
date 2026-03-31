using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ResourceRouter.Core.Models;

namespace ResourceRouter.Infrastructure.Storage;

internal static class RawPayloadStorageWriter
{
    public static async Task<string> CopyFirstFileAsync(
        string resourceDir,
        RawDropData dropData,
        CancellationToken cancellationToken)
    {
        if (dropData.FilePaths.Count == 0)
        {
            throw new InvalidOperationException("FileDrop 数据为空。");
        }

        var source = dropData.FilePaths[0];
        var fileName = SanitizeFileName(Path.GetFileName(source));
        var target = Path.Combine(resourceDir, fileName);
        var tmp = Path.Combine(LocalPathProvider.TempDirectory, $"{Guid.NewGuid():N}.tmp");

        await using (var src = File.OpenRead(source))
        await using (var dst = File.Create(tmp))
        {
            await src.CopyToAsync(dst, cancellationToken).ConfigureAwait(false);
        }

        File.Move(tmp, target, true);
        return target;
    }

    public static async Task<string> WriteTextAsync(
        string resourceDir,
        string content,
        string defaultExtension,
        string? suggestedName,
        CancellationToken cancellationToken)
    {
        var fileName = BuildSafeFileName(suggestedName, defaultExtension);
        var target = Path.Combine(resourceDir, fileName);
        var tmp = Path.Combine(LocalPathProvider.TempDirectory, $"{Guid.NewGuid():N}.tmp");

        await File.WriteAllTextAsync(tmp, content, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        File.Move(tmp, target, true);
        return target;
    }

    public static Task<string> WriteUrlAsync(string resourceDir, string url, CancellationToken cancellationToken)
    {
        var normalized = url.Trim();
        var internetShortcut = $"[InternetShortcut]{Environment.NewLine}URL={normalized}";
        return WriteTextAsync(resourceDir, internetShortcut, ".url", "link.url", cancellationToken);
    }

    public static async Task<string> WriteBitmapAsync(
        string resourceDir,
        byte[]? bytes,
        CancellationToken cancellationToken)
    {
        if (bytes is null || bytes.Length == 0)
        {
            throw new InvalidOperationException("Bitmap 数据为空。");
        }

        var target = Path.Combine(resourceDir, "image.png");
        var tmp = Path.Combine(LocalPathProvider.TempDirectory, $"{Guid.NewGuid():N}.tmp");

        await File.WriteAllBytesAsync(tmp, bytes, cancellationToken).ConfigureAwait(false);
        File.Move(tmp, target, true);
        return target;
    }

    private static string BuildSafeFileName(string? suggestedName, string defaultExtension)
    {
        if (string.IsNullOrWhiteSpace(suggestedName))
        {
            return "content" + defaultExtension;
        }

        var fileName = suggestedName;
        if (Path.GetExtension(fileName).Length == 0)
        {
            fileName += defaultExtension;
        }

        return SanitizeFileName(fileName);
    }

    private static string SanitizeFileName(string fileName)
    {
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            fileName = fileName.Replace(invalid, '_');
        }

        return string.IsNullOrWhiteSpace(fileName) ? "content.bin" : fileName;
    }
}
