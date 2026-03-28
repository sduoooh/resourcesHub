using System;
using System.Collections.Generic;
using ResourceRouter.Core.Models;
using ResourceRouter.PluginSdk;

namespace ResourceRouter.Core.Abstractions;

public interface IFormatConverterResolver
{
    IFormatConverter? Resolve(string mimeType);

    IFormatConverter? Resolve(ResourceSource source, string mimeType, string? preferredRouteId = null)
    {
        if (!string.IsNullOrWhiteSpace(preferredRouteId))
        {
            var routes = ResolveRoutes(source, mimeType);
            foreach (var route in routes)
            {
                if (string.Equals(route.RouteId, preferredRouteId, StringComparison.OrdinalIgnoreCase))
                {
                    return Resolve(mimeType);
                }
            }
        }

        return Resolve(mimeType);
    }

    IReadOnlyList<ProcessedRouteOption> ResolveRoutes(ResourceSource source, string mimeType)
    {
        var converter = Resolve(mimeType);
        if (converter is null)
        {
            return Array.Empty<ProcessedRouteOption>();
        }

        return new[]
        {
            new ProcessedRouteOption
            {
                RouteId = converter.Name,
                DisplayName = converter.Name,
                ConverterName = converter.Name,
                SourceMatchLevel = RouteSourceMatchLevel.Any,
                FormatMatchLevel = RouteFormatMatchLevel.Exact,
                Rank = 1
            }
        };
    }
}