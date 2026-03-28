using System;

namespace ResourceRouter.PluginSdk;

[AttributeUsage(AttributeTargets.Class)]
public sealed class PluginAttribute : Attribute
{
    public PluginAttribute(string id)
    {
        Id = id;
    }

    public string Id { get; }

    public string MinHostVersion { get; init; } = "1.0.0";

    public string[] SourceFilters { get; init; } = [];

    public int Priority { get; init; }
}