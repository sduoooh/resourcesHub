using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ResourceRouter.Core.Abstractions;
using ResourceRouter.Core.Models;
using ResourceRouter.Core.Services;

namespace ResourceRouter.Core.Tests;

public class ResourceManagerTests
{
    [Fact]
    public async Task ResourceManager_CanRunBasicLifecycle()
    {
        var store = new InMemoryResourceStore();
        var index = new InMemorySearchIndex(store);
        var manager = new ResourceManager(store, index);

        var resource = new Resource
        {
            Id = Guid.NewGuid(),
            SourceUri = @"C:\temp\1.txt",
            OriginalFileName = "1.txt",
            MimeType = "text/plain",
            State = ResourceState.Waiting
        };

        await manager.AddAsync(resource);

        resource.State = ResourceState.Ready;
        resource.ProcessedText = "done";
        await manager.UpdateAsync(resource);

        var restored = await manager.GetByIdAsync(resource.Id);
        var list = await manager.ListRecentAsync(10);

        Assert.NotNull(restored);
        Assert.Equal(ResourceState.Ready, restored!.State);
        Assert.Single(list);
    }

    private sealed class InMemoryResourceStore : IResourceStore
    {
        private readonly ConcurrentDictionary<Guid, Resource> _resources = new();

        public Task UpsertAsync(Resource resource, CancellationToken cancellationToken = default)
        {
            _resources[resource.Id] = resource;
            return Task.CompletedTask;
        }

        public Task<Resource?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            _resources.TryGetValue(id, out var resource);
            return Task.FromResult(resource);
        }

        public Task<Resource?> GetByFeatureHashAsync(string featureHash, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(featureHash))
            {
                return Task.FromResult<Resource?>(null);
            }

            var resource = _resources.Values
                .FirstOrDefault(r => string.Equals(r.FeatureHash, featureHash, StringComparison.OrdinalIgnoreCase));

            return Task.FromResult(resource);
        }

        public Task<IReadOnlyList<Resource>> ListRecentAsync(int limit, CancellationToken cancellationToken = default)
        {
            var list = _resources.Values
                .OrderByDescending(x => x.CreatedAt)
                .Take(limit)
                .ToList();

            return Task.FromResult<IReadOnlyList<Resource>>(list);
        }

        public Task<IReadOnlyList<Resource>> SearchAsync(string query, int limit, int offset, CancellationToken cancellationToken = default)
        {
            IEnumerable<Resource> queryable = _resources.Values;
            if (!string.IsNullOrWhiteSpace(query))
            {
                queryable = queryable.Where(x =>
                    x.DisplayTitle.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    (x.ProcessedText?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false));
            }

            var list = queryable
                .Skip(offset)
                .Take(limit)
                .ToList();

            return Task.FromResult<IReadOnlyList<Resource>>(list);
        }

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        {
            _resources.TryRemove(id, out _);
            return Task.CompletedTask;
        }
    }

    private sealed class InMemorySearchIndex : ISearchIndex
    {
        private readonly IResourceStore _store;

        public InMemorySearchIndex(IResourceStore store)
        {
            _store = store;
        }

        public Task IndexAsync(Resource resource, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<Resource>> QueryAsync(string query, int limit, int offset, CancellationToken cancellationToken = default)
        {
            return _store.SearchAsync(query, limit, offset, cancellationToken);
        }
    }
}