using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ResourceRouter.PluginSdk;

public interface IFormatConverter
{
    string Name { get; }

    IReadOnlyCollection<string> SupportedMimeTypes { get; }

    Task<ConversionResult> ConvertToFriendlyAsync(
        string inputPath,
        ConvertOptions options,
        CancellationToken cancellationToken = default);

    Task<ExtractedContent> ExtractContentAsync(
        string inputPath,
        CancellationToken cancellationToken = default);

    Task<string?> GenerateThumbnailAsync(
        string inputPath,
        string outputPath,
        CancellationToken cancellationToken = default);
}