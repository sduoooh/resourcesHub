using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ResourceRouter.Core.Abstractions;
using ResourceRouter.Core.Models;

namespace ResourceRouter.Infrastructure.Search;

public sealed class SearchEngine : ISearchIndex
{
    private readonly IResourceStore _resourceStore;

    public SearchEngine(IResourceStore resourceStore)
    {
        _resourceStore = resourceStore;
    }

    public Task IndexAsync(Resource resource, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Resource>> QueryAsync(
        string query,
        int limit,
        int offset,
        IReadOnlyList<string>? tagFilters = null,
        bool applyConditionVisibility = true,
        CancellationToken cancellationToken = default)
    {
        var ftsQuery = BuildFtsQuery(query);
        return _resourceStore.SearchAsync(
            ftsQuery,
            limit,
            offset,
            tagFilters,
            applyConditionVisibility,
            cancellationToken);
    }

    private static string BuildFtsQuery(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return string.Empty;
        }

        var tokens = Regex.Split(query, "\\s+")
            .Select(token => Regex.Replace(token, "[^\\p{L}\\p{N}_]", string.Empty))
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(token => $"\"{token}\"*")
            .ToArray();

        return tokens.Length == 0 ? string.Empty : string.Join(" AND ", tokens);
    }
}