using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ResourceRouter.Core.Abstractions;
using ResourceRouter.Core.Models;
using ResourceRouter.Infrastructure.Search;

namespace ResourceRouter.Core.Tests;

public class SearchEngineTests
{
    [Fact]
    public async Task QueryAsync_UsesAndJoinedPrefixTokens()
    {
        var store = new CapturingStore();
        var engine = new SearchEngine(store);

        await engine.QueryAsync("alpha beta", 20, 0);

        Assert.Equal("\"alpha\"* AND \"beta\"*", store.LastQuery);
    }

    [Fact]
    public async Task QueryAsync_SanitizesUnsupportedCharacters()
    {
        var store = new CapturingStore();
        var engine = new SearchEngine(store);

        await engine.QueryAsync("C# Win32?", 20, 0);

        Assert.Equal("\"C\"* AND \"Win32\"*", store.LastQuery);
    }

    [Fact]
    public async Task QueryAsync_ReturnsEmptyQueryForWhitespaceInput()
    {
        var store = new CapturingStore();
        var engine = new SearchEngine(store);

        await engine.QueryAsync("   ", 20, 0);

        Assert.Equal(string.Empty, store.LastQuery);
    }

    private sealed class CapturingStore : IResourceStore
    {
        public string LastQuery { get; private set; } = string.Empty;

        public Task UpsertAsync(Resource resource, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<Resource?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<Resource?>(null);
        }

        public Task<Resource?> GetByFeatureHashAsync(string featureHash, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<Resource?>(null);
        }

        public Task<IReadOnlyList<Resource>> ListRecentAsync(
            int limit,
            IReadOnlyList<string>? tagFilters = null,
            bool applyConditionVisibility = true,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<Resource>>(Array.Empty<Resource>());
        }

        public Task<IReadOnlyList<Resource>> SearchAsync(
            string query,
            int limit,
            int offset,
            IReadOnlyList<string>? tagFilters = null,
            bool applyConditionVisibility = true,
            CancellationToken cancellationToken = default)
        {
            LastQuery = query;
            return Task.FromResult<IReadOnlyList<Resource>>(Array.Empty<Resource>());
        }

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}