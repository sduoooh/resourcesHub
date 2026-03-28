using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using ResourceRouter.Core.Abstractions;
using ResourceRouter.Core.Models;
using ResourceRouter.PluginSdk;

namespace ResourceRouter.Core.Services;

public sealed class PluginHost : IFormatConverterResolver
{
    private static readonly Version HostVersion = new(1, 0, 0);
    private readonly List<IFormatConverter> _converters = [];
    private readonly Dictionary<IFormatConverter, ConverterDescriptor> _descriptors = new();
    private readonly IAppLogger? _logger;

    public PluginHost(IAppLogger? logger = null)
    {
        _logger = logger;
    }

    public IReadOnlyList<IFormatConverter> Converters => _converters;

    public void Register(IFormatConverter converter)
    {
        if (_converters.Contains(converter))
        {
            return;
        }

        _converters.Add(converter);
        _descriptors[converter] = ConverterDescriptor.Create(converter, pluginAttribute: null);
    }

    private void Register(IFormatConverter converter, PluginAttribute? pluginAttribute)
    {
        if (_converters.Contains(converter))
        {
            return;
        }

        _converters.Add(converter);
        _descriptors[converter] = ConverterDescriptor.Create(converter, pluginAttribute);
    }

    public void LoadPlugins(string pluginDirectory)
    {
        if (!Directory.Exists(pluginDirectory))
        {
            return;
        }

        foreach (var dllPath in Directory.EnumerateFiles(pluginDirectory, "*.dll", SearchOption.TopDirectoryOnly))
        {
            try
            {
                var loadContext = new AssemblyLoadContext($"Plugin:{Path.GetFileNameWithoutExtension(dllPath)}", isCollectible: true);
                var assembly = loadContext.LoadFromAssemblyPath(Path.GetFullPath(dllPath));

                var converters = assembly
                    .GetTypes()
                    .Where(t => !t.IsAbstract && typeof(IFormatConverter).IsAssignableFrom(t))
                    .Select(t => Activator.CreateInstance(t) as IFormatConverter)
                    .Where(c => c is not null)
                    .Cast<IFormatConverter>();

                foreach (var converter in converters)
                {
                    var converterType = converter.GetType();
                    var pluginAttribute = converterType.GetCustomAttribute<PluginAttribute>();
                    if (pluginAttribute is not null &&
                        Version.TryParse(pluginAttribute.MinHostVersion, out var minVersion) &&
                        minVersion > HostVersion)
                    {
                        _logger?.LogWarning($"插件 {converter.Name} 要求主机版本 {pluginAttribute.MinHostVersion}，当前版本 {HostVersion}。已跳过。");
                        continue;
                    }

                    Register(converter, pluginAttribute);
                    _logger?.LogInfo($"插件加载成功: {converter.Name} ({dllPath})");
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError($"插件加载失败: {dllPath}", ex);
            }
        }
    }

    public IFormatConverter? Resolve(string mimeType)
    {
        return Resolve(ResourceSource.Unknown, mimeType, preferredRouteId: null);
    }

    public IFormatConverter? Resolve(ResourceSource source, string mimeType, string? preferredRouteId = null)
    {
        var routes = BuildRouteCandidates(source, mimeType);
        if (routes.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(preferredRouteId))
        {
            var preferred = routes.FirstOrDefault(route =>
                string.Equals(route.Route.RouteId, preferredRouteId, StringComparison.OrdinalIgnoreCase));
            if (preferred is not null)
            {
                return preferred.Converter;
            }
        }

        return routes[0].Converter;
    }

    public IReadOnlyList<ProcessedRouteOption> ResolveRoutes(ResourceSource source, string mimeType)
    {
        return BuildRouteCandidates(source, mimeType)
            .Select(static candidate => candidate.Route)
            .ToArray();
    }

    private List<RouteCandidate> BuildRouteCandidates(ResourceSource source, string mimeType)
    {
        var normalizedMime = NormalizeMimeType(mimeType);
        var sourceKey = NormalizeSource(source);
        var candidates = new List<RouteCandidate>();

        foreach (var converter in _converters)
        {
            if (!_descriptors.TryGetValue(converter, out var descriptor))
            {
                descriptor = ConverterDescriptor.Create(converter, pluginAttribute: null);
                _descriptors[converter] = descriptor;
            }

            var sourceMatch = EvaluateSourceMatch(descriptor.SourceFilters, sourceKey);
            if (sourceMatch == RouteSourceMatchLevel.None)
            {
                continue;
            }

            var formatMatch = EvaluateFormatMatch(converter.SupportedMimeTypes, normalizedMime);
            if (formatMatch == RouteFormatMatchLevel.None)
            {
                continue;
            }

            var route = new ProcessedRouteOption
            {
                RouteId = descriptor.RouteId,
                DisplayName = descriptor.DisplayName,
                ConverterName = converter.Name,
                SourceMatchLevel = sourceMatch,
                FormatMatchLevel = formatMatch,
                Rank = ComputeRank(sourceMatch, formatMatch, descriptor.Priority)
            };

            candidates.Add(new RouteCandidate(converter, route));
        }

        candidates.Sort(static (left, right) =>
        {
            // 第一权重：来源的特定精确匹配
            var sourceCompare = right.Route.SourceMatchLevel.CompareTo(left.Route.SourceMatchLevel);
            if (sourceCompare != 0)
            {
                return sourceCompare;
            }

            // 第二权重：格式的精确匹配
            var formatCompare = right.Route.FormatMatchLevel.CompareTo(left.Route.FormatMatchLevel);
            if (formatCompare != 0)
            {
                return formatCompare;
            }

            // 第三权重：插件自带的优先级与基础兜底能力
            var rankCompare = right.Route.Rank.CompareTo(left.Route.Rank);
            if (rankCompare != 0)
            {
                return rankCompare;
            }

            return string.Compare(left.Route.RouteId, right.Route.RouteId, StringComparison.OrdinalIgnoreCase);
        });

        return candidates;
    }

    private static int ComputeRank(RouteSourceMatchLevel sourceMatch, RouteFormatMatchLevel formatMatch, int priority)
    {
        // 消除硬编码的大整数，排序完全交由级联判定，这里仅保留 Priority 即可
        return priority;
    }

    private static string NormalizeMimeType(string? mimeType)
    {
        return string.IsNullOrWhiteSpace(mimeType)
            ? "application/octet-stream"
            : mimeType.Trim().ToLowerInvariant();
    }

    private static string NormalizeSource(ResourceSource source)
    {
        return source switch
        {
            ResourceSource.FromDesktop => "desktop",
            ResourceSource.FromBrowser => "browser",
            ResourceSource.FromVSCode => "vscode",
            ResourceSource.FromQQ => "qq",
            ResourceSource.Manual => "manual",
            _ => "unknown"
        };
    }

    private static RouteSourceMatchLevel EvaluateSourceMatch(IReadOnlyList<string> filters, string sourceKey)
    {
        if (filters.Count == 0)
        {
            return RouteSourceMatchLevel.Any;
        }

        var best = RouteSourceMatchLevel.None;
        foreach (var raw in filters)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            var filter = raw.Trim().ToLowerInvariant();
            if (filter is "*" or "any" or "all")
            {
                best = Max(best, RouteSourceMatchLevel.Any);
                continue;
            }

            if (string.Equals(filter, sourceKey, StringComparison.OrdinalIgnoreCase))
            {
                best = Max(best, RouteSourceMatchLevel.Exact);
                continue;
            }

            if (filter.Contains('*'))
            {
                var regex = "^" + System.Text.RegularExpressions.Regex.Escape(filter).Replace("\\*", ".*") + "$";
                if (System.Text.RegularExpressions.Regex.IsMatch(sourceKey, regex, System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                {
                    best = Max(best, RouteSourceMatchLevel.Fuzzy);
                }
            }
        }

        return best;
    }

    private static RouteSourceMatchLevel Max(RouteSourceMatchLevel left, RouteSourceMatchLevel right)
    {
        return left >= right ? left : right;
    }

    private static RouteFormatMatchLevel EvaluateFormatMatch(IReadOnlyCollection<string> supportedMimeTypes, string normalizedMime)
    {
        var best = RouteFormatMatchLevel.None;
        foreach (var mime in supportedMimeTypes)
        {
            if (string.IsNullOrWhiteSpace(mime))
            {
                continue;
            }

            var supported = mime.Trim().ToLowerInvariant();
            if (string.Equals(supported, normalizedMime, StringComparison.OrdinalIgnoreCase))
            {
                best = RouteFormatMatchLevel.Exact;
                continue;
            }

            if (supported.EndsWith("/*", StringComparison.Ordinal))
            {
                var prefix = supported[..^1];
                if (normalizedMime.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    if (best < RouteFormatMatchLevel.Wildcard)
                    {
                        best = RouteFormatMatchLevel.Wildcard;
                    }
                }

                continue;
            }

            if (string.Equals(supported, "text/plain", StringComparison.OrdinalIgnoreCase) &&
                normalizedMime.StartsWith("text/", StringComparison.OrdinalIgnoreCase))
            {
                if (best < RouteFormatMatchLevel.FallbackText)
                {
                    best = RouteFormatMatchLevel.FallbackText;
                }
            }
        }

        return best;
    }

    private sealed record RouteCandidate(IFormatConverter Converter, ProcessedRouteOption Route);

    private sealed class ConverterDescriptor
    {
        public required string RouteId { get; init; }

        public required string DisplayName { get; init; }

        public required IReadOnlyList<string> SourceFilters { get; init; }

        public int Priority { get; init; }

        public static ConverterDescriptor Create(IFormatConverter converter, PluginAttribute? pluginAttribute)
        {
            var routeId = string.IsNullOrWhiteSpace(pluginAttribute?.Id)
                ? converter.Name
                : pluginAttribute!.Id.Trim();

            return new ConverterDescriptor
            {
                RouteId = routeId,
                DisplayName = converter.Name,
                SourceFilters = pluginAttribute?.SourceFilters ?? Array.Empty<string>(),
                Priority = pluginAttribute?.Priority ?? 0
            };
        }
    }
}