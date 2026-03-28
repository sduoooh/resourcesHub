using System;
using System.Collections.Generic;

namespace ResourceRouter.Core.Models;

public sealed class AIResult
{
    public static AIResult Empty { get; } = new();

    public string? Summary { get; init; }

    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();

    public string? RawResponse { get; init; }
}