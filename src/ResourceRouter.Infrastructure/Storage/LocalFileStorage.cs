using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ResourceRouter.Core.Abstractions;
using ResourceRouter.Core.Models;
using ResourceRouter.Infrastructure.Format;

namespace ResourceRouter.Infrastructure.Storage;

public sealed class LocalFileStorage : IStorageProvider
{
    private readonly IAppLogger? _logger;

    public LocalFileStorage(IAppLogger? logger = null)
    {
        _logger = logger;
        LocalPathProvider.EnsureAll();
    }

    public async Task<Resource> StoreRawAsync(
        RawDropData dropData,
        ResourceIngestOptions options,
        CancellationToken cancellationToken = default)
    {
        var resourceId = Guid.NewGuid();
        var resourceDir = Path.Combine(LocalPathProvider.RawDirectory, resourceId.ToString("N"));

        string? sourceUri = null;
        string? internalPath = null;
        DateTimeOffset? sourceLastModifiedAt = null;

        var finalPolicy = options.PersistencePolicy;
        // Temporary resources MUST use Unified storage
        if (dropData.Kind != RawDropKind.File)
        {
            finalPolicy = PersistencePolicy.Unified;
        }

        if (finalPolicy == PersistencePolicy.InPlace)
        {
            if (dropData.FilePaths.Count == 0)
            {
                throw new InvalidOperationException("原地存储策略必须提供有效的文件路径。");
            }

            sourceUri = dropData.FilePaths[0];
            var info = new FileInfo(sourceUri);
            if (info.Exists)
            {
                sourceLastModifiedAt = info.LastWriteTimeUtc;
            }
        }
        else if (finalPolicy == PersistencePolicy.Unified)
        {
            Directory.CreateDirectory(resourceDir);
            internalPath = await WriteRawPayloadAsync(resourceDir, dropData, cancellationToken).ConfigureAwait(false);
            
            // Note: The user explicitly stated that for fixed resources,
            // "统一存储（临时资源默认；固定资源选后应当从引用处复制一份至内部存储目录...原路径丢失，此操作不可逆）"
            // So we intentionally leave sourceUri null for Unified policy!
        }
        else if (finalPolicy == PersistencePolicy.Backup)
        {
            if (dropData.Kind != RawDropKind.File || dropData.FilePaths.Count == 0)
            {
                 throw new InvalidOperationException("备份存储策略必须提供有效的文件路径。");
            }

            sourceUri = dropData.FilePaths[0];
            var info = new FileInfo(sourceUri);
            if (info.Exists)
            {
                sourceLastModifiedAt = info.LastWriteTimeUtc;
            }

            Directory.CreateDirectory(resourceDir);
            internalPath = await WriteRawPayloadAsync(resourceDir, dropData, cancellationToken).ConfigureAwait(false);
        }

        var source = options.Source == ResourceSource.Unknown
            ? MimeDetector.InferSource(dropData)
            : options.Source;

        var pathForMetadata = internalPath ?? sourceUri ?? string.Empty;
        var infoForSize = new FileInfo(pathForMetadata);

        string originalFileName;
        if (!string.IsNullOrWhiteSpace(sourceUri))
        {
            originalFileName = Path.GetFileName(sourceUri);
        }
        else if (dropData.Kind == RawDropKind.File && dropData.FilePaths.Count > 0)
        {
            originalFileName = Path.GetFileName(dropData.FilePaths[0]);
        }
        else
        {
            originalFileName = dropData.OriginalSuggestedName ?? Path.GetFileName(internalPath) ?? "content";
        }

        return new Resource
        {
            Id = resourceId,
            CreatedAt = DateTimeOffset.UtcNow,
            SourceUri = sourceUri,
            InternalPath = internalPath,
            PersistencePolicy = finalPolicy,
            SourceLastModifiedAt = sourceLastModifiedAt,
            OriginalFileName = originalFileName,
            MimeType = MimeDetector.DetectFromDropData(dropData, pathForMetadata),
            FileSize = infoForSize.Exists ? infoForSize.Length : 0,
            Source = source,
            Privacy = options.Privacy,
            SyncPolicy = options.SyncPolicy,
            ProcessingModel = options.ProcessingModel,
            PermissionPresetId = options.PermissionPresetId,
            UserTitle = options.UserTitle,
            State = ResourceState.Waiting
        };
    }

    private async Task<string> WriteRawPayloadAsync(string resourceDir, RawDropData dropData, CancellationToken cancellationToken)
    {
        return dropData.Kind switch
        {
            RawDropKind.File => await CopyFileAsync(resourceDir, dropData, cancellationToken).ConfigureAwait(false),
            RawDropKind.Text => await WriteTextAsync(resourceDir, dropData.Text ?? string.Empty, ".txt", dropData.OriginalSuggestedName, cancellationToken).ConfigureAwait(false),
            RawDropKind.Html => await WriteTextAsync(resourceDir, dropData.Html ?? string.Empty, ".html", dropData.OriginalSuggestedName, cancellationToken).ConfigureAwait(false),
            RawDropKind.Url => await WriteUrlAsync(resourceDir, dropData.Url ?? dropData.Text ?? string.Empty, cancellationToken).ConfigureAwait(false),
            RawDropKind.Bitmap => await WriteBitmapAsync(resourceDir, dropData.BitmapBytes, cancellationToken).ConfigureAwait(false),
            _ => await WriteTextAsync(resourceDir, dropData.Text ?? string.Empty, ".txt", "content.txt", cancellationToken).ConfigureAwait(false)
        };
    }

    private async Task<string> CopyFileAsync(string resourceDir, RawDropData dropData, CancellationToken cancellationToken)
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

    private async Task<string> WriteTextAsync(
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

    private Task<string> WriteUrlAsync(string resourceDir, string url, CancellationToken cancellationToken)
    {
        var normalized = url.Trim();
        var internetShortcut = $"[InternetShortcut]{Environment.NewLine}URL={normalized}";
        return WriteTextAsync(resourceDir, internetShortcut, ".url", "link.url", cancellationToken);
    }

    private async Task<string> WriteBitmapAsync(string resourceDir, byte[]? bytes, CancellationToken cancellationToken)
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