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

internal interface IAudioTranscriptionEngine
{
    string Key { get; }

    bool CanHandle(AppConfig config);

    Task<AudioTranscriptionResult> TranscribeAsync(string filePath, AppConfig config, CancellationToken cancellationToken = default);
}

internal sealed class AudioTranscriptionEngineRouter
{
    private readonly Dictionary<string, IAudioTranscriptionEngine> _engines;

    public AudioTranscriptionEngineRouter(IEnumerable<IAudioTranscriptionEngine> engines)
    {
        _engines = engines.ToDictionary(engine => engine.Key, StringComparer.OrdinalIgnoreCase);
    }

    public Task<AudioTranscriptionResult> TranscribeAsync(string filePath, AppConfig config, CancellationToken cancellationToken = default)
    {
        var configured = string.IsNullOrWhiteSpace(config.AudioTranscriptionProvider)
            ? NativeCapabilityProviders.AudioTranscription.Auto
            : config.AudioTranscriptionProvider.Trim();

        if (_engines.TryGetValue(configured, out var explicitEngine) &&
            !string.Equals(explicitEngine.Key, NativeCapabilityProviders.AudioTranscription.Auto, StringComparison.OrdinalIgnoreCase))
        {
            return explicitEngine.TranscribeAsync(filePath, config, cancellationToken);
        }

        if (_engines.TryGetValue(NativeCapabilityProviders.AudioTranscription.WhisperCli, out var whisperEngine) && whisperEngine.CanHandle(config))
        {
            return whisperEngine.TranscribeAsync(filePath, config, cancellationToken);
        }

        if (_engines.TryGetValue(NativeCapabilityProviders.AudioTranscription.OpenAiCompatible, out var openAiEngine) && openAiEngine.CanHandle(config))
        {
            return openAiEngine.TranscribeAsync(filePath, config, cancellationToken);
        }

        if (_engines.TryGetValue(NativeCapabilityProviders.AudioTranscription.None, out var noneEngine))
        {
            return noneEngine.TranscribeAsync(filePath, config, cancellationToken);
        }

        return Task.FromResult(new AudioTranscriptionResult
        {
            Success = false,
            Engine = NativeCapabilityProviders.AudioTranscription.None,
            ErrorMessage = "No audio transcription engine is available."
        });
    }
}

internal sealed class NoOpAudioTranscriptionEngine : IAudioTranscriptionEngine
{
    public string Key => NativeCapabilityProviders.AudioTranscription.None;

    public bool CanHandle(AppConfig config)
    {
        return true;
    }

    public Task<AudioTranscriptionResult> TranscribeAsync(string filePath, AppConfig config, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new AudioTranscriptionResult
        {
            Success = false,
            Engine = NativeCapabilityProviders.AudioTranscription.None,
            ErrorMessage = "No audio transcription engine configured."
        });
    }
}

internal sealed class WhisperCliAudioTranscriptionEngine : IAudioTranscriptionEngine
{
    private readonly IAppLogger? _logger;

    public WhisperCliAudioTranscriptionEngine(IAppLogger? logger = null)
    {
        _logger = logger;
    }

    public string Key => NativeCapabilityProviders.AudioTranscription.WhisperCli;

    public bool CanHandle(AppConfig config)
    {
        return !string.IsNullOrWhiteSpace(config.AudioTranscriptionCliPath)
             || string.Equals(config.AudioTranscriptionProvider, NativeCapabilityProviders.AudioTranscription.WhisperCli, StringComparison.OrdinalIgnoreCase)
             || string.Equals(config.AudioTranscriptionProvider, NativeCapabilityProviders.AudioTranscription.Auto, StringComparison.OrdinalIgnoreCase);
    }

    public async Task<AudioTranscriptionResult> TranscribeAsync(string filePath, AppConfig config, CancellationToken cancellationToken = default)
    {
        var executable = string.IsNullOrWhiteSpace(config.AudioTranscriptionCliPath)
            ? "whisper-cli"
            : config.AudioTranscriptionCliPath.Trim();
        var model = string.IsNullOrWhiteSpace(config.AudioTranscriptionModel)
            ? "models/ggml-base.bin"
            : config.AudioTranscriptionModel.Trim();

        var outputBase = Path.Combine(Path.GetTempPath(), "resource-router-whisper-" + Guid.NewGuid().ToString("N"));
        var outputFile = outputBase + ".txt";

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                Arguments = $"-f \"{filePath}\" -m \"{model}\" -of \"{outputBase}\" -otxt",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = startInfo };
            process.Start();

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            var stdout = await stdoutTask.ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);

            if (process.ExitCode == 0)
            {
                if (File.Exists(outputFile))
                {
                    var text = await File.ReadAllTextAsync(outputFile, cancellationToken).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return new AudioTranscriptionResult
                        {
                            Success = true,
                            Engine = NativeCapabilityProviders.AudioTranscription.WhisperCli,
                            Transcript = text.Trim()
                        };
                    }
                }

                if (!string.IsNullOrWhiteSpace(stdout))
                {
                    return new AudioTranscriptionResult
                    {
                        Success = true,
                        Engine = NativeCapabilityProviders.AudioTranscription.WhisperCli,
                        Transcript = stdout.Trim()
                    };
                }

                return new AudioTranscriptionResult
                {
                    Success = false,
                    Engine = NativeCapabilityProviders.AudioTranscription.WhisperCli,
                    ErrorMessage = "Whisper completed but no transcription text was produced."
                };
            }

            _logger?.LogWarning($"whisper-cli transcription failed ({process.ExitCode}): {stderr}");
            return new AudioTranscriptionResult
            {
                Success = false,
                Engine = NativeCapabilityProviders.AudioTranscription.WhisperCli,
                ErrorMessage = string.IsNullOrWhiteSpace(stderr) ? "whisper-cli transcription failed." : stderr.Trim()
            };
        }
        catch (Win32Exception)
        {
            return new AudioTranscriptionResult
            {
                Success = false,
                Engine = NativeCapabilityProviders.AudioTranscription.WhisperCli,
                ErrorMessage = "whisper-cli is not installed or not found in PATH."
            };
        }
        catch (OperationCanceledException)
        {
            return new AudioTranscriptionResult
            {
                Success = false,
                Engine = NativeCapabilityProviders.AudioTranscription.WhisperCli,
                ErrorMessage = "Audio transcription was canceled."
            };
        }
        catch (Exception ex)
        {
            _logger?.LogError("whisper-cli transcription failed with exception.", ex);
            return new AudioTranscriptionResult
            {
                Success = false,
                Engine = NativeCapabilityProviders.AudioTranscription.WhisperCli,
                ErrorMessage = ex.Message
            };
        }
        finally
        {
            try
            {
                if (File.Exists(outputFile))
                {
                    File.Delete(outputFile);
                }
            }
            catch
            {
                // Ignore temporary cleanup failure.
            }
        }
    }
}

internal sealed class OpenAiCompatibleAudioTranscriptionEngine : IAudioTranscriptionEngine
{
    private readonly HttpClient _httpClient;
    private readonly IAppLogger? _logger;

    public OpenAiCompatibleAudioTranscriptionEngine(HttpClient httpClient, IAppLogger? logger = null)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public string Key => NativeCapabilityProviders.AudioTranscription.OpenAiCompatible;

    public bool CanHandle(AppConfig config)
    {
        return !string.IsNullOrWhiteSpace(config.AudioTranscriptionEndpoint);
    }

    public async Task<AudioTranscriptionResult> TranscribeAsync(string filePath, AppConfig config, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(config.AudioTranscriptionEndpoint))
        {
            return new AudioTranscriptionResult
            {
                Success = false,
                Engine = NativeCapabilityProviders.AudioTranscription.OpenAiCompatible,
                ErrorMessage = "Audio transcription endpoint is not configured."
            };
        }

        var endpoint = NormalizeEndpoint(config.AudioTranscriptionEndpoint);
        var model = string.IsNullOrWhiteSpace(config.AudioTranscriptionModel) ? "whisper-1" : config.AudioTranscriptionModel.Trim();

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        if (!string.IsNullOrWhiteSpace(config.AudioTranscriptionApiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.AudioTranscriptionApiKey.Trim());
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
                return new AudioTranscriptionResult
                {
                    Success = false,
                    Engine = NativeCapabilityProviders.AudioTranscription.OpenAiCompatible,
                    ErrorMessage = $"HTTP {(int)response.StatusCode}: {payload}"
                };
            }

            var text = TryExtractText(payload);
            if (!string.IsNullOrWhiteSpace(text))
            {
                return new AudioTranscriptionResult
                {
                    Success = true,
                    Engine = NativeCapabilityProviders.AudioTranscription.OpenAiCompatible,
                    Transcript = text.Trim()
                };
            }

            return new AudioTranscriptionResult
            {
                Success = false,
                Engine = NativeCapabilityProviders.AudioTranscription.OpenAiCompatible,
                ErrorMessage = "Transcription API response does not contain a text field."
            };
        }
        catch (OperationCanceledException)
        {
            return new AudioTranscriptionResult
            {
                Success = false,
                Engine = NativeCapabilityProviders.AudioTranscription.OpenAiCompatible,
                ErrorMessage = "Audio transcription was canceled."
            };
        }
        catch (Exception ex)
        {
            _logger?.LogError("OpenAI-compatible transcription failed.", ex);
            return new AudioTranscriptionResult
            {
                Success = false,
                Engine = NativeCapabilityProviders.AudioTranscription.OpenAiCompatible,
                ErrorMessage = ex.Message
            };
        }
    }

    private static string NormalizeEndpoint(string endpoint)
    {
        var normalized = endpoint.Trim().TrimEnd('/');
        if (normalized.EndsWith("/audio/transcriptions", StringComparison.OrdinalIgnoreCase))
        {
            return normalized;
        }

        return normalized + "/v1/audio/transcriptions";
    }

    private static string? TryExtractText(string jsonPayload)
    {
        try
        {
            using var json = JsonDocument.Parse(jsonPayload);
            if (json.RootElement.TryGetProperty("text", out var textElement) && textElement.ValueKind == JsonValueKind.String)
            {
                return textElement.GetString();
            }

            return null;
        }
        catch
        {
            return null;
        }
    }
}