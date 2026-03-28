using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ResourceRouter.Core.Abstractions;
using ResourceRouter.Core.Models;
using ResourceRouter.Core.Services;
using ResourceRouter.PluginSdk;

namespace ResourceRouter.App.Services;

public sealed class DefaultProcessingCapabilityApi : IProcessingCapabilityApi
{
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".markdown", ".html", ".htm", ".json"
    };

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp", ".tif", ".tiff"
    };

    private static readonly HttpClient SharedHttpClient = new() { Timeout = TimeSpan.FromSeconds(90) };

    private readonly Func<AppConfig> _configAccessor;
    private readonly OcrEngineRouter _ocrRouter;
    private readonly AudioTranscriptionEngineRouter _audioRouter;
    private readonly ResourceManager? _resourceManager;
    private readonly IAppLogger? _logger;

    public DefaultProcessingCapabilityApi(
        Func<AppConfig> configAccessor,
        ResourceManager? resourceManager = null,
        IAppLogger? logger = null,
        HttpClient? httpClient = null)
    {
        _configAccessor = configAccessor;
        _resourceManager = resourceManager;
        _logger = logger;

        _ocrRouter = new OcrEngineRouter(new IOcrEngine[]
        {
            new NoOpOcrEngine(),
            new TesseractCliOcrEngine(_logger),
            new OpenAiCompatibleOcrEngine(httpClient ?? SharedHttpClient, _logger)
        });

        _audioRouter = new AudioTranscriptionEngineRouter(new IAudioTranscriptionEngine[]
        {
            new NoOpAudioTranscriptionEngine(),
            new WhisperCliAudioTranscriptionEngine(_logger),
            new OpenAiCompatibleAudioTranscriptionEngine(httpClient ?? SharedHttpClient, _logger)
        });
    }

    public async Task<OcrResult> RunOcrAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var config = _configAccessor();
        if (!config.EnableOcr)
        {
            return new OcrResult
            {
                Success = false,
                Engine = NativeCapabilityProviders.Ocr.None,
                ErrorMessage = "OCR is disabled in settings."
            };
        }

        if (!File.Exists(filePath))
        {
            return new OcrResult
            {
                Success = false,
                Engine = NativeCapabilityProviders.Ocr.None,
                ErrorMessage = "File not found."
            };
        }

        var extension = Path.GetExtension(filePath);
        if (TextExtensions.Contains(extension))
        {
            var text = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
            return new OcrResult
            {
                Success = true,
                Engine = "builtin-text-pass",
                Text = text
            };
        }

        if (!ImageExtensions.Contains(extension) &&
            string.IsNullOrWhiteSpace(config.OcrEndpoint) &&
            string.Equals(config.OcrProvider, NativeCapabilityProviders.Ocr.Auto, StringComparison.OrdinalIgnoreCase))
        {
            _logger?.LogWarning($"OCR API skipped for non-image input without configured OCR endpoint: {filePath}");
            return new OcrResult
            {
                Success = false,
                Engine = NativeCapabilityProviders.Ocr.None,
                ErrorMessage = "OCR for non-image files requires an endpoint-based OCR provider."
            };
        }

        return await _ocrRouter.RunAsync(filePath, config, cancellationToken).ConfigureAwait(false);
    }

    public Task<AudioTranscriptionResult> RunAudioTranscriptionAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var config = _configAccessor();
        if (!config.EnableAudioTranscription)
        {
            return Task.FromResult(new AudioTranscriptionResult
            {
                Success = false,
                Engine = NativeCapabilityProviders.AudioTranscription.None,
                ErrorMessage = "Audio transcription is disabled in settings."
            });
        }

        if (!File.Exists(filePath))
        {
            return Task.FromResult(new AudioTranscriptionResult
            {
                Success = false,
                Engine = NativeCapabilityProviders.AudioTranscription.None,
                ErrorMessage = "File not found."
            });
        }

        return _audioRouter.TranscribeAsync(filePath, config, cancellationToken);
    }

    public async Task<FeatureSubmissionResult> SubmitStructuredFeaturesAsync(
        StructuredFeatureSet featureSet,
        CancellationToken cancellationToken = default)
    {
        if (featureSet.ResourceId == Guid.Empty)
        {
            return new FeatureSubmissionResult
            {
                Success = false,
                Status = "invalid-request",
                ErrorMessage = "ResourceId is required."
            };
        }

        var config = _configAccessor();
        if (!IsFeatureizationMechanismEnabled(config))
        {
            return new FeatureSubmissionResult
            {
                Success = false,
                Status = "disabled",
                ErrorMessage = "Featureization mechanism is currently disabled."
            };
        }

        if (_resourceManager is null)
        {
            return new FeatureSubmissionResult
            {
                Success = false,
                Status = "unavailable",
                ErrorMessage = "Resource manager is unavailable."
            };
        }

        var resource = await _resourceManager.GetByIdAsync(featureSet.ResourceId, cancellationToken).ConfigureAwait(false);
        if (resource is null)
        {
            return new FeatureSubmissionResult
            {
                Success = false,
                Status = "not-found",
                ErrorMessage = "Resource not found."
            };
        }

        if (!IsFeatureizationEnabledForResource(config, resource))
        {
            return new FeatureSubmissionResult
            {
                Success = false,
                Status = "disabled",
                ErrorMessage = "Featureization switch is disabled for this resource."
            };
        }

        var canonicalPayload = BuildCanonicalFeaturePayload(featureSet);
        if (string.IsNullOrWhiteSpace(canonicalPayload))
        {
            return new FeatureSubmissionResult
            {
                Success = false,
                Status = "invalid-request",
                ErrorMessage = "Structured feature payload is empty."
            };
        }

        resource.FeatureHash = ComputeFeatureHash(canonicalPayload);
        await _resourceManager.UpdateAsync(resource, cancellationToken).ConfigureAwait(false);

        return new FeatureSubmissionResult
        {
            Success = true,
            Status = "stored"
        };
    }

    private static bool IsFeatureizationMechanismEnabled(AppConfig config)
    {
        foreach (var item in config.FeatureizationByType)
        {
            if (item.Value)
            {
                return true;
            }
        }

        foreach (var item in config.FeatureizationBySource)
        {
            if (item.Value)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsFeatureizationEnabledForResource(AppConfig config, Resource resource)
    {
        var mimeType = NormalizeMimeType(resource.MimeType);
        var source = resource.Source.ToString();

        return IsSwitchEnabled(config.FeatureizationByType, mimeType)
            || IsSwitchEnabled(config.FeatureizationBySource, source);
    }

    private static bool IsSwitchEnabled(IReadOnlyDictionary<string, bool> switches, string key)
    {
        if (switches.TryGetValue(key, out var exact))
        {
            return exact;
        }

        if (switches.TryGetValue("*", out var wildcard))
        {
            return wildcard;
        }

        if (switches.TryGetValue("all", out var all))
        {
            return all;
        }

        var slashIndex = key.IndexOf('/');
        if (slashIndex > 0)
        {
            var group = key[..slashIndex] + "/*";
            if (switches.TryGetValue(group, out var groupSwitch))
            {
                return groupSwitch;
            }
        }

        return false;
    }

    private static string NormalizeMimeType(string? mimeType)
    {
        return string.IsNullOrWhiteSpace(mimeType)
            ? "application/octet-stream"
            : mimeType.Trim().ToLowerInvariant();
    }

    private static string BuildCanonicalFeaturePayload(StructuredFeatureSet featureSet)
    {
        var builder = new StringBuilder();

        var producer = string.IsNullOrWhiteSpace(featureSet.Producer)
            ? "unknown"
            : featureSet.Producer.Trim();
        builder.Append("producer=").Append(producer).Append('\n');

        foreach (var pair in featureSet.ScalarFeatures.OrderBy(static p => p.Key, StringComparer.Ordinal))
        {
            builder
                .Append("scalar:")
                .Append(pair.Key.Trim())
                .Append('=')
                .Append(pair.Value?.Trim() ?? string.Empty)
                .Append('\n');
        }

        foreach (var pair in featureSet.NumericFeatures.OrderBy(static p => p.Key, StringComparer.Ordinal))
        {
            builder
                .Append("numeric:")
                .Append(pair.Key.Trim())
                .Append('=')
                .Append(pair.Value.ToString("R", CultureInfo.InvariantCulture))
                .Append('\n');
        }

        foreach (var pair in featureSet.MultiValueFeatures.OrderBy(static p => p.Key, StringComparer.Ordinal))
        {
            var values = pair.Value
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Select(static value => value.Trim())
                .OrderBy(static value => value, StringComparer.Ordinal);

            builder
                .Append("multi:")
                .Append(pair.Key.Trim())
                .Append('=')
                .Append(string.Join("|", values))
                .Append('\n');
        }

        return builder.ToString();
    }

    private static string ComputeFeatureHash(string canonicalPayload)
    {
        var bytes = Encoding.UTF8.GetBytes(canonicalPayload);
        var hashBytes = SHA256.HashData(bytes);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}