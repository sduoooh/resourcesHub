namespace ResourceRouter.Core.Models;

public static class ResourceTagRules
{
    public const int MaxLength = ResourceFieldLimits.TagMaxLength;

    public static string? Normalize(string? rawTag)
    {
        if (string.IsNullOrWhiteSpace(rawTag))
        {
            return null;
        }

        var normalized = rawTag.Trim().TrimStart('#');
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        if (normalized.Length > MaxLength)
        {
            normalized = normalized[..MaxLength];
        }

        return normalized;
    }

    public static bool ExceedsLimit(string? rawTag)
    {
        if (string.IsNullOrWhiteSpace(rawTag))
        {
            return false;
        }

        var normalized = rawTag.Trim().TrimStart('#');
        return !string.IsNullOrWhiteSpace(normalized) && normalized.Length > MaxLength;
    }
}