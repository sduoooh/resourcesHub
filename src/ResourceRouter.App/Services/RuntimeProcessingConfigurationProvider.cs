using System;
using System.Collections.Generic;
using ResourceRouter.Core.Abstractions;
using ResourceRouter.Core.Models;
using ResourceRouter.PluginSdk;

namespace ResourceRouter.App.Services;

public sealed class RuntimeProcessingConfigurationProvider : IProcessingConfigurationProvider
{
    private readonly Func<AppConfig> _configAccessor;

    public RuntimeProcessingConfigurationProvider(Func<AppConfig> configAccessor, IProcessingCapabilityApi capabilityApi)
    {
        _configAccessor = configAccessor;
        CapabilityApi = capabilityApi;
    }

    public IProcessingCapabilityApi CapabilityApi { get; }

    public ProcessingConfigurationSnapshot Resolve(Resource resource, IFormatConverter? converter)
    {
        var config = _configAccessor();
        var converterName = converter?.Name ?? string.Empty;

        return new ProcessingConfigurationSnapshot
        {
            EnableOcr = config.EnableOcr,
            EnableAudioTranscription = config.EnableAudioTranscription,
            CapabilityApi = CapabilityApi,
            PluginOptions = GetPluginOptions(config, converterName, resource.MimeType)
        };
    }

    private static IReadOnlyDictionary<string, string> GetPluginOptions(AppConfig config, string converterName, string mimeType)
    {
        if (!string.IsNullOrWhiteSpace(converterName)
            && config.PluginSettings.TryGetValue(converterName, out var byConverter))
        {
            return byConverter;
        }

        if (config.PluginSettings.TryGetValue(mimeType, out var byMime))
        {
            return byMime;
        }

        if (config.PluginSettings.TryGetValue("*", out var wildcard))
        {
            return wildcard;
        }

        return EmptyOptions;
    }

    private static readonly IReadOnlyDictionary<string, string> EmptyOptions = new Dictionary<string, string>();
}