using System;
using System.Collections.Generic;
using System.Data;
using System.Text.Json;
using Dapper;

namespace ResourceRouter.Infrastructure.Storage;

internal sealed class StringListTypeHandler : SqlMapper.TypeHandler<IReadOnlyList<string>>
{
    public override void SetValue(IDbDataParameter parameter, IReadOnlyList<string>? value)
    {
        parameter.DbType = DbType.String;
        parameter.Value = JsonSerializer.Serialize(value ?? Array.Empty<string>());
    }

    public override IReadOnlyList<string> Parse(object? value)
    {
        if (value is null || value is DBNull)
        {
            return Array.Empty<string>();
        }

        if (value is not string json || string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<string>();
        }

        return JsonSerializer.Deserialize<string[]>(json) ?? Array.Empty<string>();
    }
}
