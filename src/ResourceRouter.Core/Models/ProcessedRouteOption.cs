namespace ResourceRouter.Core.Models;

public enum RouteSourceMatchLevel
{
    None = 0,
    Any = 1,
    Fuzzy = 2,
    Exact = 3
}

public enum RouteFormatMatchLevel
{
    None = 0,
    FallbackText = 1,
    Wildcard = 2,
    Exact = 3
}

public sealed class ProcessedRouteOption
{
    public required string RouteId { get; init; }

    public required string DisplayName { get; init; }

    public required string ConverterName { get; init; }

    public RouteSourceMatchLevel SourceMatchLevel { get; init; }

    public RouteFormatMatchLevel FormatMatchLevel { get; init; }

    public int Rank { get; init; }

    public bool IsExportable { get; init; } = true;

    public string? LockReason { get; init; }
}