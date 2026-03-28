using System;
using System.Data;
using System.Text.Json;
using Dapper;
using ResourceRouter.Core.Models;

namespace ResourceRouter.Infrastructure.Storage;

internal sealed class ResourceHealthStatusTypeHandler : SqlMapper.TypeHandler<ResourceHealthStatus>
{
    public override void SetValue(IDbDataParameter parameter, ResourceHealthStatus? value)
    {
        parameter.DbType = DbType.String;

        if (value is null || IsEmpty(value))
        {
            parameter.Value = DBNull.Value;
            return;
        }

        parameter.Value = JsonSerializer.Serialize(value);
    }

    public override ResourceHealthStatus Parse(object? value)
    {
        if (value is null || value is DBNull)
        {
            return new ResourceHealthStatus();
        }

        if (value is not string json || string.IsNullOrWhiteSpace(json))
        {
            return new ResourceHealthStatus();
        }

        return JsonSerializer.Deserialize<ResourceHealthStatus>(json) ?? new ResourceHealthStatus();
    }

    private static bool IsEmpty(ResourceHealthStatus value)
    {
        return value.LastCheckAt is null
               && value.LastCheckPassed is null
               && string.IsNullOrWhiteSpace(value.LastCheckMessage);
    }
}
