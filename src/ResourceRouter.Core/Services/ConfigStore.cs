using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ResourceRouter.Core.Abstractions;
using ResourceRouter.Core.Models;

namespace ResourceRouter.Core.Services;

public sealed class ConfigStore
{
    private readonly string _configPath;
    private readonly IAppLogger? _logger;
    private readonly JsonSerializerOptions _jsonSerializerOptions = new() { WriteIndented = true };
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public ConfigStore(string? configPath = null, IAppLogger? logger = null)
    {
        _configPath = string.IsNullOrWhiteSpace(configPath) ? AppConfig.GetDefaultConfigPath() : configPath;
        _logger = logger;
    }

    public async Task<AppConfig> LoadAsync(CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(_configPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (!File.Exists(_configPath))
        {
            var defaultConfig = new AppConfig();
            await SaveAsync(defaultConfig, cancellationToken).ConfigureAwait(false);
            return defaultConfig;
        }

        try
        {
            await using var stream = File.OpenRead(_configPath);
            var loaded = await JsonSerializer.DeserializeAsync<AppConfig>(stream, _jsonSerializerOptions, cancellationToken)
                .ConfigureAwait(false);

            return loaded ?? new AppConfig();
        }
        catch (Exception ex)
        {
            _logger?.LogError("配置文件损坏，已回退到默认配置。", ex);
            var fallback = new AppConfig();
            await SaveAsync(fallback, cancellationToken).ConfigureAwait(false);
            return fallback;
        }
    }

    public async Task SaveAsync(AppConfig config, CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var directory = Path.GetDirectoryName(_configPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var tmpPath = $"{_configPath}.{Guid.NewGuid():N}.tmp";
            var json = JsonSerializer.Serialize(config, _jsonSerializerOptions);
            await File.WriteAllTextAsync(tmpPath, json, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
            File.Move(tmpPath, _configPath, true);
        }
        finally
        {
            _writeLock.Release();
        }
    }
}