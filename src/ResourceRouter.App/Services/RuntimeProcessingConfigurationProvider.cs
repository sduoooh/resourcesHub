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

    public bool EnableOcr => _configAccessor().EnableOcr;

    public bool EnableAudioTranscription => _configAccessor().EnableAudioTranscription;

    public IProcessingCapabilityApi CapabilityApi { get; }

    public IReadOnlyDictionary<string, string> GetPluginOptions(string converterName, string mimeType)
    {
        var config = _configAccessor();

        if (config.PluginSettings.TryGetValue(converterName, out var byConverter))
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