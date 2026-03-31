using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ResourceRouter.Core.Abstractions;
using ResourceRouter.Core.Models;
using ResourceRouter.Core.Services;
using ResourceRouter.Infrastructure.Format;

namespace ResourceRouter.Infrastructure.Storage;

public sealed class LocalFileStorage : IStorageProvider
{
    private readonly IRawPayloadMaterializerResolver _materializerResolver;

    public LocalFileStorage(IAppLogger? logger = null, IRawPayloadMaterializerResolver? materializerResolver = null)
    {
        _materializerResolver = materializerResolver ?? DefaultRawPayloadMaterializerResolver.CreateDefault();
        LocalPathProvider.EnsureAll();
    }

    public async Task<Resource> StoreRawAsync(
        RawDropData dropData,
        ResourceIngestOptions options,
        CancellationToken cancellationToken = default)
    {
        var resourceId = Guid.NewGuid();
        var resourceDir = LocalPathProvider.GetRawResourceDirectory(resourceId);

        string? sourceUri = null;
        string? internalPath = null;
        DateTimeOffset? sourceLastModifiedAt = null;

        var finalPolicy = options.PersistencePolicy;
        if (PersistencePolicyRules.ShouldForceUnified(dropData.Kind))
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
            RawKind = dropData.Kind,
            SourceAppHint = dropData.SourceAppHint,
            CapturedAt = dropData.CapturedAt,
            OriginalSuggestedName = dropData.OriginalSuggestedName,
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
            TitleOverride = ResourceAliasRules.Normalize(options.TitleOverride),
            State = ResourceState.Waiting
        };
    }

    private async Task<string> WriteRawPayloadAsync(string resourceDir, RawDropData dropData, CancellationToken cancellationToken)
    {
        var context = new RawPayloadMaterializationContext
        {
            ResourceDirectory = resourceDir,
            DropData = dropData
        };

        var materializer = _materializerResolver.Resolve(context);
        return await materializer.MaterializeAsync(context, cancellationToken).ConfigureAwait(false);
    }
}