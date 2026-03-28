using System;
using System.Collections.Generic;
using System.IO;
using ResourceRouter.Core.Models;

namespace ResourceRouter.Infrastructure.Format;

public static class MimeDetector
{
    private static readonly byte[] PdfSignature = { 0x25, 0x50, 0x44, 0x46 };
    private static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47 };
    private static readonly byte[] JpegSignature = { 0xFF, 0xD8, 0xFF };
    private static readonly byte[] Gif87aSignature = { 0x47, 0x49, 0x46, 0x38, 0x37, 0x61 };
    private static readonly byte[] Gif89aSignature = { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61 };
    private static readonly byte[] RiffSignature = { 0x52, 0x49, 0x46, 0x46 };
    private static readonly byte[] WaveSignature = { 0x57, 0x41, 0x56, 0x45 };
    private static readonly byte[] WebpSignature = { 0x57, 0x45, 0x42, 0x50 };
    private static readonly byte[] ZipLocalSignature = { 0x50, 0x4B, 0x03, 0x04 };
    private static readonly byte[] ZipEmptySignature = { 0x50, 0x4B, 0x05, 0x06 };
    private static readonly byte[] ZipSpannedSignature = { 0x50, 0x4B, 0x07, 0x08 };
    private static readonly byte[] Mp3Id3Signature = { 0x49, 0x44, 0x33 };

    private static readonly IReadOnlyDictionary<string, string> ExtensionToMime =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".txt"] = "text/plain",
            [".md"] = "text/markdown",
            [".markdown"] = "text/markdown",
            [".html"] = "text/html",
            [".htm"] = "text/html",
            [".url"] = "text/uri-list",
            [".json"] = "application/json",
            [".pdf"] = "application/pdf",
            [".zip"] = "application/zip",
            [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            [".png"] = "image/png",
            [".jpg"] = "image/jpeg",
            [".jpeg"] = "image/jpeg",
            [".bmp"] = "image/bmp",
            [".gif"] = "image/gif",
            [".webp"] = "image/webp",
            [".mp3"] = "audio/mpeg",
            [".wav"] = "audio/wav",
            [".m4a"] = "audio/mp4"
        };

    public static string DetectFromDropData(RawDropData data, string? filePath = null)
    {
        return data.Kind switch
        {
            RawDropKind.Text => "text/plain",
            RawDropKind.Html => "text/html",
            RawDropKind.Url => "text/uri-list",
            RawDropKind.Bitmap => "image/png",
            RawDropKind.File when !string.IsNullOrWhiteSpace(filePath) => DetectFromFilePath(filePath),
            _ => "application/octet-stream"
        };
    }

    public static string DetectFromFilePath(string filePath)
    {
        var ext = Path.GetExtension(filePath);
        if (!string.IsNullOrWhiteSpace(ext) && ExtensionToMime.TryGetValue(ext, out var mime))
        {
            return mime;
        }

        var signatureMime = DetectFromFileHeader(filePath);
        if (!string.IsNullOrWhiteSpace(signatureMime))
        {
            return signatureMime;
        }

        return "application/octet-stream";
    }

    public static ResourceSource InferSource(RawDropData data)
    {
        var hint = data.SourceAppHint?.ToLowerInvariant() ?? string.Empty;
        if (hint.Contains("chrome") || hint.Contains("edge") || hint.Contains("firefox"))
        {
            return ResourceSource.FromBrowser;
        }

        if (hint.Contains("code"))
        {
            return ResourceSource.FromVSCode;
        }

        if (hint.Contains("qq"))
        {
            return ResourceSource.FromQQ;
        }

        return data.Kind switch
        {
            RawDropKind.Html => ResourceSource.FromBrowser,
            RawDropKind.Url => ResourceSource.FromBrowser,
            RawDropKind.Text => ResourceSource.FromVSCode,
            _ => ResourceSource.FromDesktop
        };
    }

    private static string? DetectFromFileHeader(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return null;
        }

        try
        {
            Span<byte> buffer = stackalloc byte[16];
            using var stream = File.OpenRead(filePath);
            var bytesRead = stream.Read(buffer);
            if (bytesRead <= 0)
            {
                return null;
            }

            var header = buffer[..bytesRead];

            if (StartsWith(header, PdfSignature))
            {
                return "application/pdf";
            }

            if (StartsWith(header, PngSignature))
            {
                return "image/png";
            }

            if (StartsWith(header, JpegSignature))
            {
                return "image/jpeg";
            }

            if (StartsWith(header, Gif87aSignature) || StartsWith(header, Gif89aSignature))
            {
                return "image/gif";
            }

            if (StartsWith(header, RiffSignature) && header.Length >= 12)
            {
                if (StartsWith(header[8..], WaveSignature))
                {
                    return "audio/wav";
                }

                if (StartsWith(header[8..], WebpSignature))
                {
                    return "image/webp";
                }
            }

            if (StartsWith(header, ZipLocalSignature) || StartsWith(header, ZipEmptySignature) || StartsWith(header, ZipSpannedSignature))
            {
                return "application/zip";
            }

            if (StartsWith(header, Mp3Id3Signature) || LooksLikeMp3FrameHeader(header))
            {
                return "audio/mpeg";
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private static bool StartsWith(ReadOnlySpan<byte> source, ReadOnlySpan<byte> prefix)
    {
        return source.Length >= prefix.Length && source[..prefix.Length].SequenceEqual(prefix);
    }

    private static bool LooksLikeMp3FrameHeader(ReadOnlySpan<byte> header)
    {
        return header.Length >= 2
               && header[0] == 0xFF
               && (header[1] & 0xE0) == 0xE0;
    }
}