using System.IO;

namespace ResourceRouter.Core.Abstractions;

/// <summary>
/// A generalized DTO for representing pipeline results and preparing export payloads.
/// By supporting paths, text, and memory streams simultaneously, consumers
/// can decouple from rigid file-based export behaviors.
/// </summary>
public interface IExportablePayload
{
    /// <summary>
    /// Gets a fully resolved local file path if the payload resides on disk.
    /// Can be null if the payload is memory-only.
    /// </summary>
    string? FilePath { get; }

    /// <summary>
    /// Gets the textual representation of the payload.
    /// </summary>
    string? TextContent { get; }

    /// <summary>
    /// Gets raw bytes of the payload, useful for images or other pure-memory artifacts.
    /// </summary>
    byte[]? MemoryBytes { get; }

    /// <summary>
    /// Optional mime type hint to format the payload in precise DataFormats.
    /// </summary>
    string? MimeTypeHint { get; }
}

public class ExportablePayload : IExportablePayload
{
    public string? FilePath { get; init; }

    public string? TextContent { get; init; }

    public byte[]? MemoryBytes { get; init; }

    public string? MimeTypeHint { get; init; }
}
