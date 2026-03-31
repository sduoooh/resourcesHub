using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ResourceRouter.Core.Models;

namespace ResourceRouter.Infrastructure.Storage;

public sealed class DefaultRawPayloadMaterializerResolver : IRawPayloadMaterializerResolver
{
    private readonly IReadOnlyList<IRawPayloadMaterializer> _materializers;

    public DefaultRawPayloadMaterializerResolver(IReadOnlyList<IRawPayloadMaterializer> materializers)
    {
        _materializers = materializers ?? throw new ArgumentNullException(nameof(materializers));
        if (_materializers.Count == 0)
        {
            throw new ArgumentException("至少需要一个原始载体物化器。", nameof(materializers));
        }
    }

    public static DefaultRawPayloadMaterializerResolver CreateDefault()
    {
        return new DefaultRawPayloadMaterializerResolver(
            new IRawPayloadMaterializer[]
            {
                new FileRawPayloadMaterializer(),
                new TextRawPayloadMaterializer(),
                new HtmlRawPayloadMaterializer(),
                new UrlRawPayloadMaterializer(),
                new BitmapRawPayloadMaterializer(),
                new FallbackRawPayloadMaterializer()
            });
    }

    public IRawPayloadMaterializer Resolve(RawPayloadMaterializationContext context)
    {
        foreach (var materializer in _materializers)
        {
            if (materializer.CanHandle(context))
            {
                return materializer;
            }
        }

        return _materializers[^1];
    }

    private sealed class FileRawPayloadMaterializer : IRawPayloadMaterializer
    {
        public bool CanHandle(RawPayloadMaterializationContext context)
        {
            return context.DropData.Kind == RawDropKind.File;
        }

        public Task<string> MaterializeAsync(RawPayloadMaterializationContext context, CancellationToken cancellationToken)
        {
            return RawPayloadStorageWriter.CopyFirstFileAsync(
                context.ResourceDirectory,
                context.DropData,
                cancellationToken);
        }
    }

    private sealed class TextRawPayloadMaterializer : IRawPayloadMaterializer
    {
        public bool CanHandle(RawPayloadMaterializationContext context)
        {
            return context.DropData.Kind == RawDropKind.Text;
        }

        public Task<string> MaterializeAsync(RawPayloadMaterializationContext context, CancellationToken cancellationToken)
        {
            return RawPayloadStorageWriter.WriteTextAsync(
                context.ResourceDirectory,
                context.DropData.Text ?? string.Empty,
                ".txt",
                context.DropData.OriginalSuggestedName,
                cancellationToken);
        }
    }

    private sealed class HtmlRawPayloadMaterializer : IRawPayloadMaterializer
    {
        public bool CanHandle(RawPayloadMaterializationContext context)
        {
            return context.DropData.Kind == RawDropKind.Html;
        }

        public Task<string> MaterializeAsync(RawPayloadMaterializationContext context, CancellationToken cancellationToken)
        {
            return RawPayloadStorageWriter.WriteTextAsync(
                context.ResourceDirectory,
                context.DropData.Html ?? string.Empty,
                ".html",
                context.DropData.OriginalSuggestedName,
                cancellationToken);
        }
    }

    private sealed class UrlRawPayloadMaterializer : IRawPayloadMaterializer
    {
        public bool CanHandle(RawPayloadMaterializationContext context)
        {
            return context.DropData.Kind == RawDropKind.Url;
        }

        public Task<string> MaterializeAsync(RawPayloadMaterializationContext context, CancellationToken cancellationToken)
        {
            var url = context.DropData.Url ?? context.DropData.Text ?? string.Empty;
            return RawPayloadStorageWriter.WriteUrlAsync(
                context.ResourceDirectory,
                url,
                cancellationToken);
        }
    }

    private sealed class BitmapRawPayloadMaterializer : IRawPayloadMaterializer
    {
        public bool CanHandle(RawPayloadMaterializationContext context)
        {
            return context.DropData.Kind == RawDropKind.Bitmap;
        }

        public Task<string> MaterializeAsync(RawPayloadMaterializationContext context, CancellationToken cancellationToken)
        {
            return RawPayloadStorageWriter.WriteBitmapAsync(
                context.ResourceDirectory,
                context.DropData.BitmapBytes,
                cancellationToken);
        }
    }

    private sealed class FallbackRawPayloadMaterializer : IRawPayloadMaterializer
    {
        public bool CanHandle(RawPayloadMaterializationContext context)
        {
            return true;
        }

        public Task<string> MaterializeAsync(RawPayloadMaterializationContext context, CancellationToken cancellationToken)
        {
            return RawPayloadStorageWriter.WriteTextAsync(
                context.ResourceDirectory,
                context.DropData.Text ?? string.Empty,
                ".txt",
                context.DropData.OriginalSuggestedName,
                cancellationToken);
        }
    }
}
