using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ResourceRouter.Core.Abstractions;
using ResourceRouter.Core.Events;
using ResourceRouter.Core.Models;

namespace ResourceRouter.Core.Services;

public sealed class ResourceManager
{
    private readonly IResourceStore _resourceStore;
    private readonly ISearchIndex _searchIndex;

    public event EventHandler<ResourceCreatedEventArgs>? OnResourceCreated;
    public event EventHandler<ResourceUpdatedEventArgs>? OnResourceUpdated;
    public event EventHandler<ResourceDeletedEventArgs>? OnResourceDeleted;

    public ResourceManager(IResourceStore resourceStore, ISearchIndex searchIndex)
    {
        _resourceStore = resourceStore;
        _searchIndex = searchIndex;
    }

    public async Task AddAsync(Resource resource, CancellationToken cancellationToken = default)
    {
        await _resourceStore.UpsertAsync(resource, cancellationToken).ConfigureAwait(false);
        await _searchIndex.IndexAsync(resource, cancellationToken).ConfigureAwait(false);
        OnResourceCreated?.Invoke(this, new ResourceCreatedEventArgs { Resource = resource });
    }

    public async Task UpdateAsync(Resource resource, CancellationToken cancellationToken = default)
    {
        await _resourceStore.UpsertAsync(resource, cancellationToken).ConfigureAwait(false);
        await _searchIndex.IndexAsync(resource, cancellationToken).ConfigureAwait(false);
        OnResourceUpdated?.Invoke(this, new ResourceUpdatedEventArgs { Resource = resource });
    }

    public Task<Resource?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return _resourceStore.GetByIdAsync(id, cancellationToken);
    }

    public Task<Resource?> GetByFeatureHashAsync(string featureHash, CancellationToken cancellationToken = default)
    {
        return _resourceStore.GetByFeatureHashAsync(featureHash, cancellationToken);
    }

    public Task<IReadOnlyList<Resource>> ListRecentAsync(
        int limit,
        IReadOnlyList<string>? tagFilters = null,
        bool applyConditionVisibility = true,
        CancellationToken cancellationToken = default)
    {
        return _resourceStore.ListRecentAsync(limit, tagFilters, applyConditionVisibility, cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await _resourceStore.DeleteAsync(id, cancellationToken).ConfigureAwait(false);
        OnResourceDeleted?.Invoke(this, new ResourceDeletedEventArgs { ResourceId = id });
    }
}