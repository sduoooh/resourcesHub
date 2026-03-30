using System;
using System.Collections.Generic;
using System.Linq;
using ResourceRouter.Core.Abstractions;
using ResourceRouter.Core.Models;

namespace ResourceRouter.Core.Services;

public sealed class DefaultResourceMetadataFacetPolicy : IResourceMetadataFacetPolicy
{
    public ResourceMetadataFacet Read(Resource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);

        return new ResourceMetadataFacet
        {
            TitleOverride = resource.TitleOverride,
            Annotations = resource.Annotations,
            Summary = resource.Summary,
            ConditionTags = NormalizeTags(resource.ConditionTags),
            PropertyTags = NormalizeTags(resource.PropertyTags),
            OriginalFileName = resource.OriginalFileName,
            MimeType = resource.MimeType,
            FileSize = resource.FileSize,
            Source = resource.Source,
            CreatedAt = resource.CreatedAt,
            ExtensionMetadata = BuildExtensionMetadata(resource)
        };
    }

    public bool Apply(Resource resource, ResourceMetadataFacet nextFacet)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(nextFacet);

        var normalizedTitle = Normalize(nextFacet.TitleOverride);
        var normalizedAnnotations = Normalize(nextFacet.Annotations);
        var normalizedSummary = Normalize(nextFacet.Summary);
        var normalizedConditionTags = NormalizeTags(nextFacet.ConditionTags);
        var normalizedPropertyTags = NormalizeTags(nextFacet.PropertyTags);

        var changed =
            !string.Equals(resource.TitleOverride, normalizedTitle, StringComparison.Ordinal) ||
            !string.Equals(resource.Annotations, normalizedAnnotations, StringComparison.Ordinal) ||
            !string.Equals(resource.Summary, normalizedSummary, StringComparison.Ordinal) ||
            !TagSetsEqual(resource.ConditionTags, normalizedConditionTags) ||
            !TagSetsEqual(resource.PropertyTags, normalizedPropertyTags);

        if (!changed)
        {
            return false;
        }

        resource.TitleOverride = normalizedTitle;
        resource.Annotations = normalizedAnnotations;
        resource.Summary = normalizedSummary;
        resource.ConditionTags = normalizedConditionTags;
        resource.PropertyTags = normalizedPropertyTags;
        return true;
    }

    private static IReadOnlyList<string> NormalizeTags(IReadOnlyList<string>? tags)
    {
        if (tags is null || tags.Count == 0)
        {
            return Array.Empty<string>();
        }

        return tags
            .Where(static tag => !string.IsNullOrWhiteSpace(tag))
            .Select(static tag => tag.Trim().TrimStart('#'))
            .Where(static tag => !string.IsNullOrWhiteSpace(tag))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static tag => tag, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool TagSetsEqual(IReadOnlyList<string>? left, IReadOnlyList<string>? right)
    {
        var normalizedLeft = NormalizeTags(left);
        var normalizedRight = NormalizeTags(right);
        return normalizedLeft.SequenceEqual(normalizedRight, StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyDictionary<string, string?> BuildExtensionMetadata(Resource resource)
    {
        return new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["rawKind"] = resource.RawKind.ToString(),
            ["sourceUri"] = resource.SourceUri,
            ["internalPath"] = resource.InternalPath,
            ["processedRouteId"] = resource.ProcessedRouteId,
            ["processedFilePath"] = resource.ProcessedFilePath,
            ["thumbnailPath"] = resource.ThumbnailPath,
            ["featureHash"] = resource.FeatureHash,
            ["sourceAppHint"] = resource.SourceAppHint,
            ["capturedAt"] = resource.CapturedAt?.ToString("O"),
            ["originalSuggestedName"] = resource.OriginalSuggestedName,
            ["persistencePolicy"] = resource.PersistencePolicy.ToString(),
            ["privacy"] = resource.Privacy.ToString(),
            ["syncPolicy"] = resource.SyncPolicy.ToString(),
            ["processingModel"] = resource.ProcessingModel.ToString(),
            ["permissionPresetId"] = resource.PermissionPresetId,
            ["state"] = resource.State.ToString(),
            ["waitingExpiresAt"] = resource.WaitingExpiresAt?.ToString("O"),
            ["lastError"] = resource.LastError
        };
    }

    private static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }
}