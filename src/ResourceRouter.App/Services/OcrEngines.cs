using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ResourceRouter.Core.Abstractions;
using ResourceRouter.Core.Models;
using ResourceRouter.PluginSdk;

namespace ResourceRouter.App.Services;

internal interface IOcrEngine
{
    string Key { get; }

    bool CanHandle(AppConfig config);

    Task<OcrResult> RunAsync(string filePath, AppConfig config, CancellationToken cancellationToken = default);
}

internal sealed class OcrEngineRouter
{
    private readonly Dictionary<string, IOcrEngine> _engines;

    public OcrEngineRouter(IEnumerable<IOcrEngine> engines)
    {
        _engines = engines.ToDictionary(engine => engine.Key, StringComparer.OrdinalIgnoreCase);
    }

    public Task<OcrResult> RunAsync(string filePath, AppConfig config, CancellationToken cancellationToken = default)
    {
        var configured = string.IsNullOrWhiteSpace(config.OcrProvider)
            ? NativeCapabilityProviders.Ocr.Auto
            : config.OcrProvider.Trim();

        if (_engines.TryGetValue(configured, out var explicitEngine) &&
            !string.Equals(explicitEngine.Key, NativeCapabilityProviders.Ocr.Auto, StringComparison.OrdinalIgnoreCase))
        {
            return explicitEngine.RunAsync(filePath, config, cancellationToken);
        }

        if (_engines.TryGetValue(NativeCapabilityProviders.Ocr.TesseractCli, out var tesseract) && tesseract.CanHandle(config))
        {
            return tesseract.RunAsync(filePath, config, cancellationToken);
        }

        if (_engines.TryGetValue(NativeCapabilityProviders.Ocr.OpenAiCompatible, out var openAi) && openAi.CanHandle(config))
        {
            return openAi.RunAsync(filePath, config, cancellationToken);
        }

        if (_engines.TryGetValue(NativeCapabilityProviders.Ocr.None, out var none))
        {
            return none.RunAsync(filePath, config, cancellationToken);
        }

        return Task.FromResult(new OcrResult
        {
            Success = false,
            Engine = NativeCapabilityProviders.Ocr.None,
            ErrorMessage = "No OCR engine is available."
        });
    }
}

internal sealed class NoOpOcrEngine : IOcrEngine
{
    public string Key => NativeCapabilityProviders.Ocr.None;

    public bool CanHandle(AppConfig config)
    {
        return true;
    }

    public Task<OcrResult> RunAsync(string filePath, AppConfig config, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new OcrResult
        {
            Success = false,
            Engine = NativeCapabilityProviders.Ocr.None,
            ErrorMessage = "No OCR engine configured."
        });
    }
}

internal sealed class TesseractCliOcrEngine : IOcrEngine
{
    private readonly IAppLogger? _logger;

    public TesseractCliOcrEngine(IAppLogger? logger = null)
    {
        _logger = logger;
    }

    public string Key => NativeCapabilityProviders.Ocr.TesseractCli;

    public bool CanHandle(AppConfig config)
    {
        return !string.IsNullOrWhiteSpace(config.OcrCliPath)
             || string.Equals(config.OcrProvider, NativeCapabilityProviders.Ocr.Auto, StringComparison.OrdinalIgnoreCase)
             || string.Equals(config.OcrProvider, NativeCapabilityProviders.Ocr.TesseractCli, StringComparison.OrdinalIgnoreCase);
    }

    public async Task<OcrResult> RunAsync(string filePath, AppConfig config, CancellationToken cancellationToken = default)
    {
        var executable = string.IsNullOrWhiteSpace(config.OcrCliPath)
            ? "tesseract"
            : config.OcrCliPath.Trim();
        var language = string.IsNullOrWhiteSpace(config.OcrModel)
            ? "eng+chi_sim"
            : config.OcrModel.Trim();

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            Arguments = $"\"{filePath}\" stdout -l {language}",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        try
        {
            using var process = new Process { StartInfo = startInfo };
            process.Start();

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            var output = await outputTask.ConfigureAwait(false);
            var error = await errorTask.ConfigureAwait(false);

            if (process.ExitCode == 0)
            {
                if (string.IsNullOrWhiteSpace(output))
                {
                    return new OcrResult
                    {
                        Success = false,
                        Engine = NativeCapabilityProviders.Ocr.TesseractCli,
                        ErrorMessage = "OCR completed but no text was recognized."
                    };
                }

                return new OcrResult
                {
                    Success = true,
                    Engine = NativeCapabilityProviders.Ocr.TesseractCli,
                    Text = output.Trim()
                };
            }

            _logger?.LogWarning($"Tesseract OCR failed with exit code {process.ExitCode}: {error}");
            return new OcrResult
            {
                Success = false,
                Engine = NativeCapabilityProviders.Ocr.TesseractCli,
                ErrorMessage = string.IsNullOrWhiteSpace(error)
                    ? "Tesseract OCR failed."
                    : error.Trim()
            };
        }
        catch (Win32Exception)
        {
            return new OcrResult
            {
                Success = false,
                Engine = NativeCapabilityProviders.Ocr.TesseractCli,
                ErrorMessage = "Tesseract is not installed or not found in PATH."
            };
        }
        catch (OperationCanceledException)
        {
            return new OcrResult
            {
                Success = false,
                Engine = NativeCapabilityProviders.Ocr.TesseractCli,
                ErrorMessage = "OCR operation was canceled."
            };
        }
        catch (Exception ex)
        {
            _logger?.LogError("OCR engine execution failed.", ex);
            return new OcrResult
            {
                Success = false,
                Engine = NativeCapabilityProviders.Ocr.TesseractCli,
                ErrorMessage = ex.Message
            };
        }
    }
}

internal sealed class OpenAiCompatibleOcrEngine : IOcrEngine
{
    private readonly HttpClient _httpClient;
    private readonly IAppLogger? _logger;

    public OpenAiCompatibleOcrEngine(HttpClient httpClient, IAppLogger? logger = null)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public string Key => NativeCapabilityProviders.Ocr.OpenAiCompatible;

    public bool CanHandle(AppConfig config)
    {
        return !string.IsNullOrWhiteSpace(config.OcrEndpoint);
    }

    public async Task<OcrResult> RunAsync(string filePath, AppConfig config, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(config.OcrEndpoint))
        {
            return new OcrResult
            {
                Success = false,
                Engine = NativeCapabilityProviders.Ocr.OpenAiCompatible,
                ErrorMessage = "OCR endpoint is not configured."
            };
        }

        var endpoint = NormalizeEndpoint(config.OcrEndpoint);
        var model = string.IsNullOrWhiteSpace(config.OcrModel) ? "ocr-1" : config.OcrModel.Trim();

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        if (!string.IsNullOrWhiteSpace(config.OcrApiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.OcrApiKey.Trim());
        }

        using var form = new MultipartFormDataContent();
        await using var stream = File.OpenRead(filePath);
        using var fileContent = new StreamContent(stream);
        form.Add(fileContent, "file", Path.GetFileName(filePath));
        form.Add(new StringContent(model), "model");
        request.Content = form;

        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return new OcrResult
                {
                    Success = false,
                    Engine = NativeCapabilityProviders.Ocr.OpenAiCompatible,
                    ErrorMessage = $"HTTP {(int)response.StatusCode}: {payload}"
                };
            }

            var text = TryExtractText(payload);
            if (!string.IsNullOrWhiteSpace(text))
            {
                return new OcrResult
                {
                    Success = true,
                    Engine = NativeCapabilityProviders.Ocr.OpenAiCompatible,
                    Text = text.Trim()
                };
            }

            return new OcrResult
            {
                Success = false,
                Engine = NativeCapabilityProviders.Ocr.OpenAiCompatible,
                ErrorMessage = "OCR API response does not contain recognized text."
            };
        }
        catch (OperationCanceledException)
        {
            return new OcrResult
            {
                Success = false,
                Engine = NativeCapabilityProviders.Ocr.OpenAiCompatible,
                ErrorMessage = "OCR operation was canceled."
            };
        }
        catch (Exception ex)
        {
            _logger?.LogError("OpenAI-compatible OCR failed.", ex);
            return new OcrResult
            {
                Success = false,
                Engine = NativeCapabilityProviders.Ocr.OpenAiCompatible,
                ErrorMessage = ex.Message
            };
        }
    }

    private static string NormalizeEndpoint(string endpoint)
    {
        var normalized = endpoint.Trim().TrimEnd('/');
        if (normalized.EndsWith("/ocr", StringComparison.OrdinalIgnoreCase))
        {
            return normalized;
        }

        if (normalized.Contains("/v1/", StringComparison.OrdinalIgnoreCase))
        {
            return normalized;
        }

        return normalized + "/v1/ocr";
    }

    private static string? TryExtractText(string payload)
    {
        try
        {
            using var json = JsonDocument.Parse(payload);
            if (json.RootElement.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
            {
                return text.GetString();
            }

            if (json.RootElement.TryGetProperty("result", out var result) &&
                result.ValueKind == JsonValueKind.Object &&
                result.TryGetProperty("text", out var resultText) &&
                resultText.ValueKind == JsonValueKind.String)
            {
                return resultText.GetString();
            }

            if (json.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in data.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Object &&
                        item.TryGetProperty("text", out var itemText) &&
                        itemText.ValueKind == JsonValueKind.String)
                    {
                        return itemText.GetString();
                    }
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }
}
