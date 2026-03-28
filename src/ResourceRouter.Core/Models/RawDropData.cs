using System;
using System.Collections.Generic;

namespace ResourceRouter.Core.Models;

public enum RawDropKind
{
    File,
    Text,
    Html,
    Bitmap,
    Url
}

public sealed class RawDropData
{
    public RawDropKind Kind { get; init; }

    public IReadOnlyList<string> FilePaths { get; init; } = Array.Empty<string>();

    public string? Text { get; init; }

    public string? Html { get; init; }

    public byte[]? BitmapBytes { get; init; }

    public string? Url { get; init; }

    public string? SourceAppHint { get; init; }

    public DateTimeOffset CapturedAt { get; init; } = DateTimeOffset.UtcNow;

    public string? OriginalSuggestedName { get; init; }
}