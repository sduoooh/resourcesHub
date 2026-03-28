using System;
using System.Collections.Generic;

namespace ResourceRouter.Core.Models;

public enum PrivacyLevel
{
    Private,
    Public
}

public enum SyncPolicy
{
    LocalOnly,
    CloudDefault
}

public enum ModelType
{
    None,
    LocalSmall,
    CloudAI
}

public enum ResourceState
{
    Waiting,
    Processing,
    Ready,
    Error
}

public enum ResourceSource
{
    Unknown,
    FromDesktop,
    FromBrowser,
    FromVSCode,
    FromQQ,
    Manual
}

public enum DragVariant
{
    Raw,
    Processed
}

public enum PersistencePolicy
{
    InPlace = 0,
    Unified = 1,
    Backup = 2
}

public sealed class ResourceHealthStatus
{
    public DateTimeOffset? LastCheckAt { get; set; }

    public bool? LastCheckPassed { get; set; }

    public string? LastCheckMessage { get; set; }
}

public sealed class Resource
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public string? SourceUri { get; set; }

    public string? InternalPath { get; set; }

    public DateTimeOffset? SourceLastModifiedAt { get; set; }

    public PersistencePolicy PersistencePolicy { get; set; } = PersistencePolicy.InPlace;

    public string OriginalFileName { get; set; } = string.Empty;

    public string MimeType { get; set; } = "application/octet-stream";

    public long FileSize { get; set; }

    public ResourceSource Source { get; set; } = ResourceSource.Unknown;

    public string? ProcessedFilePath { get; set; }

    public string? ProcessedText { get; set; }

    public string? ProcessedRouteId { get; set; }

    public string? ThumbnailPath { get; set; }

    public string? Summary { get; set; }

    public IReadOnlyList<string> AutoTags { get; set; } = Array.Empty<string>();

    public string? UserTitle { get; set; }

    public string? UserNotes { get; set; }

    public IReadOnlyList<string> UserTags { get; set; } = Array.Empty<string>();

    public PrivacyLevel Privacy { get; set; } = PrivacyLevel.Private;

    public SyncPolicy SyncPolicy { get; set; } = SyncPolicy.LocalOnly;

    public IReadOnlyList<string> SyncTargetDevices { get; set; } = Array.Empty<string>();

    public ModelType ProcessingModel { get; set; } = ModelType.LocalSmall;

    public string PermissionPresetId { get; set; } = PermissionPreset.PrivatePresetId;

    public ResourceState State { get; set; } = ResourceState.Waiting;

    public DateTimeOffset? WaitingExpiresAt { get; set; }

    public string? LastError { get; set; }

    public string? FeatureHash { get; set; }

    public ResourceHealthStatus Health { get; set; } = new();

    public string DisplayTitle =>
        string.IsNullOrWhiteSpace(UserTitle)
            ? (string.IsNullOrWhiteSpace(OriginalFileName) ? Id.ToString() : OriginalFileName)
            : UserTitle;

    public string? GetActivePath()
    {
        return PersistencePolicy switch
        {
            PersistencePolicy.Unified => InternalPath,
            PersistencePolicy.InPlace => SourceUri,
            // Core model defers fallback validation (like network or disk reachability)
            // to the infrastructure layer, exposing the primary intended active path here.
            PersistencePolicy.Backup => SourceUri ?? InternalPath, 
            _ => null
        };
    }
}