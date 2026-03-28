using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ResourceRouter.PluginSdk;

namespace ResourceRouter.SampleTextPlugin;

[Plugin("sample-text-plugin", MinHostVersion = "1.0.0")]
public sealed class SampleTextPlugin : IFormatConverter
{
    public string Name => "SampleTextPlugin";

    public System.Collections.Generic.IReadOnlyCollection<string> SupportedMimeTypes { get; } =
        new[] { "text/plain" };

    public async Task<ConversionResult> ConvertToFriendlyAsync(string inputPath, ConvertOptions options, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(options.OutputDirectory);

        var original = await File.ReadAllTextAsync(inputPath, cancellationToken).ConfigureAwait(false);
        var prefix = options.PluginOptions.TryGetValue("prefix", out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : "[SamplePlugin]";

        var builder = new StringBuilder();
        builder.AppendLine(prefix);
        builder.AppendLine(original);

        if (options.EnableOcr && options.CapabilityApi is not null)
        {
            var ocr = await options.CapabilityApi.RunOcrAsync(inputPath, cancellationToken).ConfigureAwait(false);
            if (ocr.Success && !string.IsNullOrWhiteSpace(ocr.Text))
            {
                builder.AppendLine();
                builder.AppendLine("[OCR]");
                builder.AppendLine(ocr.Text);
            }
        }

        var processed = builder.ToString().Trim();

        var outputPath = Path.Combine(options.OutputDirectory, Path.GetFileNameWithoutExtension(inputPath) + ".sample.txt");
        await File.WriteAllTextAsync(outputPath, processed, cancellationToken).ConfigureAwait(false);

        return new ConversionResult
        {
            ProcessedFilePath = outputPath,
            ProcessedText = processed
        };
    }

    public async Task<ExtractedContent> ExtractContentAsync(string inputPath, CancellationToken cancellationToken = default)
    {
        var text = await File.ReadAllTextAsync(inputPath, cancellationToken).ConfigureAwait(false);
        return new ExtractedContent
        {
            Paragraphs = new[] { new ContentParagraph { Index = 0, Text = text } }
        };
    }

    public Task<string?> GenerateThumbnailAsync(string inputPath, string outputPath, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<string?>(null);
    }
}
