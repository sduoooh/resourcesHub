using System;
using System.IO;
using System.Reflection;

namespace ResourceRouter.Infrastructure.Storage;

internal static class EmbeddedSql
{
    private const string ResourcePrefix = "ResourceRouter.Infrastructure.Storage.Sql.";

    public static string Load(string fileName)
    {
        var resourceName = ResourcePrefix + fileName;
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"未找到嵌入 SQL 脚本: {resourceName}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
