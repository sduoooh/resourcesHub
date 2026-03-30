using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ResourceRouter.Core.Models;

namespace ResourceRouter.Core.Abstractions;

public interface ISearchIndex
{
    Task IndexAsync(Resource resource, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Resource>> QueryAsync(
        string query,
        int limit,
        int offset,
        IReadOnlyList<string>? tagFilters = null,
        bool applyConditionVisibility = true,
        CancellationToken cancellationToken = default);
}