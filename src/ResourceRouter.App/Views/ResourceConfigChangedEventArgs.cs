using System;
using ResourceRouter.Core.Models;

namespace ResourceRouter.App.Views;

public sealed class ResourceConfigChangedEventArgs : EventArgs
{
    public required Resource Resource { get; init; }

    public required PrivacyLevel PreviousPrivacy { get; init; }

    public required SyncPolicy PreviousSyncPolicy { get; init; }

    public required ModelType PreviousProcessingModel { get; init; }

    public required string PreviousPermissionPresetId { get; init; }

    public string? PreviousProcessedRouteId { get; init; }

    public PersistencePolicy PreviousPersistencePolicy { get; init; }
}
