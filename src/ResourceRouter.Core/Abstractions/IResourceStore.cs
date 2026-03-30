using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ResourceRouter.Core.Models;

namespace ResourceRouter.Core.Abstractions;

public interface IResourceStore
{
    Task UpsertAsync(Resource resource, CancellationToken cancellationToken = default);

    Task<Resource?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Resource?> GetByFeatureHashAsync(string featureHash, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Resource>> ListRecentAsync(
        int limit,
        IReadOnlyList<string>? tagFilters = null,
        bool applyConditionVisibility = true,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Resource>> SearchAsync(
        string query,
        int limit,
        int offset,
        IReadOnlyList<string>? tagFilters = null,
        bool applyConditionVisibility = true,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}