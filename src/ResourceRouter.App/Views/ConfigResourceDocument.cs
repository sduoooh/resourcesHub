using System;

namespace ResourceRouter.App.Views;

internal sealed class ConfigResourceDocument
{
    public required string Id { get; init; }

    public required string DisplayName { get; init; }

    public required string[] Tags { get; init; }

    public required string JsonContent { get; set; }

    public string TagDisplay => string.Join(", ", Tags);

    public string SuggestedFileName => $"{Id}.{DateTime.UtcNow:yyyyMMddHHmmss}.json";
}
