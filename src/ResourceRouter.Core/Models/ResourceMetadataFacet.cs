using System;
using System.Collections.Generic;

namespace ResourceRouter.Core.Models;

public sealed class ResourceMetadataFacet
{
    public string? TitleOverride { get; init; }

    public string? Annotations { get; init; }

    public string? Summary { get; init; }

    public IReadOnlyList<string> ConditionTags { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> PropertyTags { get; init; } = Array.Empty<string>();

    public string OriginalFileName { get; init; } = string.Empty;

    public string MimeType { get; init; } = "application/octet-stream";

    public long FileSize { get; init; }

    public ResourceSource Source { get; init; } = ResourceSource.Unknown;

    public DateTimeOffset CreatedAt { get; init; }

    public IReadOnlyDictionary<string, string?> ExtensionMetadata { get; init; } =
        new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
}