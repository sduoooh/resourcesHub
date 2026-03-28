using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ResourceRouter.Core.Abstractions;
using ResourceRouter.Core.Models;
using ResourceRouter.Infrastructure.Storage;

namespace ResourceRouter.App.Interop;

public sealed class ThumbnailProvider : IThumbnailProvider
{
    private readonly IThumbnailProvider _innerProvider;
    private readonly IAppLogger? _logger;
    private readonly System.Func<AppConfig>? _configAccessor;

    public ThumbnailProvider(IThumbnailProvider innerProvider, IAppLogger? logger = null, System.Func<AppConfig>? configAccessor = null)
    {
        _innerProvider = innerProvider;
        _logger = logger;
        _configAccessor = configAccessor;
    }

    public async Task<string?> GenerateAsync(Resource resource, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(resource.ThumbnailPath) && File.Exists(resource.ThumbnailPath))
        {
            return resource.ThumbnailPath;
        }

        var generated = await _innerProvider.GenerateAsync(resource, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(generated) && File.Exists(generated))
        {
            return generated;
        }

        var mode = ResolveMode();
        if (mode == NativeCapabilityProviders.Thumbnail.None)
        {
            return null;
        }

        var activePath = resource.GetActivePath();
        if (string.IsNullOrWhiteSpace(activePath) || !File.Exists(activePath))
        {
            return null;
        }

        var thumbPath = Path.Combine(LocalPathProvider.ThumbsDirectory, resource.Id.ToString("N") + ".png");
        if (File.Exists(thumbPath))
        {
            return thumbPath;
        }

        try
        {
            if (mode is NativeCapabilityProviders.Thumbnail.Auto or NativeCapabilityProviders.Thumbnail.Shell)
            {
                using var shellBitmap = ShellThumbnailHelper.TryGetThumbnailBitmap(activePath, 256);
                if (shellBitmap is not null)
                {
                    shellBitmap.Save(thumbPath, ImageFormat.Png);
                    return thumbPath;
                }
            }

            using var icon = Icon.ExtractAssociatedIcon(activePath) ?? SystemIcons.Application;
            using var bitmap = icon.ToBitmap();
            bitmap.Save(thumbPath, ImageFormat.Png);
            return thumbPath;
        }
        catch (System.Exception ex)
        {
            _logger?.LogWarning($"缩略图 fallback 失败: {resource.Id}, {ex.Message}");
            return null;
        }
    }

    private string ResolveMode()
    {
        var mode = _configAccessor?.Invoke().ThumbnailProviderMode;
        if (string.IsNullOrWhiteSpace(mode))
        {
            return NativeCapabilityProviders.Thumbnail.Auto;
        }

        var normalized = mode.Trim().ToLowerInvariant();
        return normalized is NativeCapabilityProviders.Thumbnail.Auto
            or NativeCapabilityProviders.Thumbnail.Shell
            or NativeCapabilityProviders.Thumbnail.ManagedIcon
            or NativeCapabilityProviders.Thumbnail.None
            ? normalized
            : NativeCapabilityProviders.Thumbnail.Auto;
    }
}