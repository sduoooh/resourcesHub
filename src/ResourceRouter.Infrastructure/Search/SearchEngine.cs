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

    public async Task<IReadOnlyList<Resource>> QueryAsync(
        string query,
        int limit,
        int offset,
        IReadOnlyList<string>? tagFilters = null,
        bool applyConditionVisibility = true,
        CancellationToken cancellationToken = default)
    {
        var ftsQuery = BuildFtsQuery(query);
        if (string.IsNullOrWhiteSpace(ftsQuery))
        {
            var literalQuery = query.Trim();
            var safeOffset = offset < 0 ? 0 : offset;
            var fetchCount = Math.Max(limit + safeOffset, limit * 4);
            var recent = await _resourceStore.ListRecentAsync(
                    fetchCount,
                    tagFilters,
                    applyConditionVisibility,
                    cancellationToken)
                .ConfigureAwait(false);

            var filtered = recent
                .Where(resource => ContainsLiteral(resource, literalQuery))
                .ToArray();

            return filtered
                .Skip(safeOffset)
                .Take(limit)
                .ToArray();
        }

        return await _resourceStore.SearchAsync(
                ftsQuery,
                limit,
                offset,
                tagFilters,
                applyConditionVisibility,
                cancellationToken)
            .ConfigureAwait(false);
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

    private static bool ContainsLiteral(Resource resource, string literalQuery)
    {
        if (string.IsNullOrEmpty(literalQuery))
        {
            return false;
        }

        return Contains(resource.DisplayTitle, literalQuery)
               || Contains(resource.OriginalFileName, literalQuery)
               || Contains(resource.SourceUri, literalQuery)
               || Contains(resource.InternalPath, literalQuery)
               || Contains(resource.Summary, literalQuery)
               || Contains(resource.Annotations, literalQuery)
               || Contains(resource.ProcessedText, literalQuery);
    }

    private static bool Contains(string? source, string value)
    {
        return !string.IsNullOrWhiteSpace(source)
               && source.Contains(value, System.StringComparison.OrdinalIgnoreCase);
    }
}