namespace ResourceRouter.Core.Models;

public static class ResourceAliasRules
{
    public const int MaxLength = ResourceFieldLimits.AliasMaxLength;

    public static string? Normalize(string? alias)
    {
        if (string.IsNullOrWhiteSpace(alias))
        {
            return null;
        }

        var normalized = alias.Trim();
        if (normalized.Length > MaxLength)
        {
            normalized = normalized[..MaxLength];
        }

        return normalized;
    }
}