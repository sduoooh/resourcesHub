using System;
using System.Collections.Generic;

namespace ResourceRouter.PluginSdk;

public sealed class StructuredFeatureSet
{
    public Guid ResourceId { get; init; }

    public string Producer { get; init; } = string.Empty;

    public IReadOnlyDictionary<string, string> ScalarFeatures { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, double> NumericFeatures { get; init; } =
        new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, IReadOnlyList<string>> MultiValueFeatures { get; init; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
}
