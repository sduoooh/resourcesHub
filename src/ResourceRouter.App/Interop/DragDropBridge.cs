using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Media.Imaging;
using ResourceRouter.Core.Abstractions;
using ResourceRouter.Core.Models;

namespace ResourceRouter.App.Interop;

public static class DragDropBridge
{
    private const string UrlFormatW = "UniformResourceLocatorW";
    private const string UrlFormat = "UniformResourceLocator";

    private delegate void ExportPayloadWriter(DataObject dataObject, IExportablePayload payload);

    private static readonly IReadOnlyList<ExportPayloadWriter> ExportWriters =
    [
        WriteFilePayload,
        WriteUrlPayload,
        WriteHtmlPayload,
        WriteTextPayload,
        WriteImagePayload
    ];

    private static readonly IReadOnlyDictionary<RawDropKind, Func<Resource, ExportablePayload>> RawPayloadBuilders =
        new Dictionary<RawDropKind, Func<Resource, ExportablePayload>>
        {
            [RawDropKind.File] = BuildRawFilePayload,
            [RawDropKind.Text] = BuildRawTextPayload,
            [RawDropKind.Html] = BuildRawHtmlPayload,
            [RawDropKind.Url] = BuildRawUrlPayload,
            [RawDropKind.Bitmap] = BuildRawBitmapPayload
        };

    public readonly record struct ConfigEditorDragPayload(string LaunchFilePath, string EditorUri);

    public static IReadOnlyList<RawDropData> Extract(IDataObject dataObject, string? sourceAppHint = null)
    {
        var capturedAt = DateTimeOffset.UtcNow;

        if (TryGetData(dataObject, DataFormats.FileDrop, out var fileDropData))
        {
            if (fileDropData is string[] filePaths && filePaths.Length > 0)
            {
                return BuildFileDrops(filePaths, capturedAt, sourceAppHint);
            }

            if (fileDropData is StringCollection fileDropCollection && fileDropCollection.Count > 0)
            {
                var paths = fileDropCollection.Cast<string>().ToArray();
                return BuildFileDrops(paths, capturedAt, sourceAppHint);
            }
        }

        var fileNameData = TryExtractFileNames(dataObject);
        if (fileNameData.Count > 0)
        {
            return BuildFileDrops(fileNameData.ToArray(), capturedAt, sourceAppHint);
        }

        if (TryGetData(dataObject, DataFormats.Bitmap, out var bitmapData))
        {
            if (bitmapData is BitmapSource bitmap)
            {
                return new[]
                {
                    new RawDropData
                    {
                        Kind = RawDropKind.Bitmap,
                        BitmapBytes = EncodeBitmapPng(bitmap),
                        CapturedAt = capturedAt,
                        SourceAppHint = sourceAppHint,
                        OriginalSuggestedName = "image.png"
                    }
                };
            }
        }

        if (TryGetData(dataObject, DataFormats.Html, out var htmlData))
        {
            if (htmlData is string html)
            {
                return new[]
                {
                    new RawDropData
                    {
                        Kind = RawDropKind.Html,
                        Html = html,
                        Text = html,
                        CapturedAt = capturedAt,
                        SourceAppHint = sourceAppHint,
                        OriginalSuggestedName = "snippet.html"
                    }
                };
            }
        }

        var extractedUrl = ReadUrl(dataObject);
        if (!string.IsNullOrWhiteSpace(extractedUrl))
        {
            return new[]
            {
                new RawDropData
                {
                    Kind = RawDropKind.Url,
                    Url = extractedUrl,
                    Text = extractedUrl,
                    CapturedAt = capturedAt,
                    SourceAppHint = sourceAppHint,
                    OriginalSuggestedName = "link.url"
                }
            };
        }

        if (TryGetText(dataObject, out var text))
        {
            if (!string.IsNullOrWhiteSpace(text))
            {
                return new[]
                {
                    new RawDropData
                    {
                        Kind = RawDropKind.Text,
                        Text = text,
                        CapturedAt = capturedAt,
                        SourceAppHint = sourceAppHint,
                        OriginalSuggestedName = "content.txt"
                    }
                };
            }
        }

        return Array.Empty<RawDropData>();
    }

    private static IReadOnlyList<RawDropData> BuildFileDrops(
        IReadOnlyList<string> filePaths,
        DateTimeOffset capturedAt,
        string? sourceAppHint)
    {
        return filePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => new RawDropData
            {
                Kind = RawDropKind.File,
                FilePaths = new[] { path },
                CapturedAt = capturedAt,
                SourceAppHint = sourceAppHint,
                OriginalSuggestedName = Path.GetFileName(path)
            })
            .ToArray();
    }

    public static DataObject CreateExportData(IExportablePayload payload)
    {
        var dataObject = new DataObject();

        foreach (var writer in ExportWriters)
        {
            writer(dataObject, payload);
        }

        return dataObject;
    }

    public static DataObject CreateDragData(
        Resource resource,
        DragVariant variant)
    {
        var payload = variant == DragVariant.Raw
            ? BuildRawPayload(resource)
            : BuildProcessedPayload(resource);

        return CreateExportData(payload);
    }

    private static ExportablePayload BuildRawPayload(Resource resource)
    {
        if (RawPayloadBuilders.TryGetValue(resource.RawKind, out var builder))
        {
            return builder(resource);
        }

        return BuildRawTextPayload(resource);
    }

    private static ExportablePayload BuildProcessedPayload(Resource resource)
    {
        return new ExportablePayload
        {
            FilePath = NullIfMissing(resource.ProcessedFilePath),
            TextContent = string.IsNullOrWhiteSpace(resource.ProcessedText) ? null : resource.ProcessedText,
            MimeTypeHint = resource.MimeType
        };
    }

    private static ExportablePayload BuildRawFilePayload(Resource resource)
    {
        return new ExportablePayload
        {
            FilePath = NullIfMissing(resource.GetActivePath()),
            MimeTypeHint = resource.MimeType
        };
    }

    private static ExportablePayload BuildRawTextPayload(Resource resource)
    {
        return new ExportablePayload
        {
            TextContent = ReadRawTextContent(resource),
            MimeTypeHint = "text/plain"
        };
    }

    private static ExportablePayload BuildRawHtmlPayload(Resource resource)
    {
        return new ExportablePayload
        {
            TextContent = ReadRawTextContent(resource),
            MimeTypeHint = "text/html"
        };
    }

    private static ExportablePayload BuildRawUrlPayload(Resource resource)
    {
        return new ExportablePayload
        {
            TextContent = ReadRawUrl(resource),
            MimeTypeHint = "text/uri-list"
        };
    }

    private static ExportablePayload BuildRawBitmapPayload(Resource resource)
    {
        return new ExportablePayload
        {
            MemoryBytes = ReadRawBytes(resource),
            MimeTypeHint = resource.MimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
                ? resource.MimeType
                : "image/png"
        };
    }

    private static void WriteFilePayload(DataObject dataObject, IExportablePayload payload)
    {
        if (string.IsNullOrWhiteSpace(payload.FilePath) || !File.Exists(payload.FilePath))
        {
            return;
        }

        var fileList = new StringCollection { payload.FilePath };
        dataObject.SetFileDropList(fileList);

        if (string.IsNullOrWhiteSpace(payload.TextContent) &&
            payload.MimeTypeHint?.StartsWith("text/", StringComparison.OrdinalIgnoreCase) == true)
        {
            try
            {
                dataObject.SetText(File.ReadAllText(payload.FilePath), TextDataFormat.UnicodeText);
            }
            catch
            {
            }
        }

        if (payload.MemoryBytes is null &&
            payload.MimeTypeHint?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true)
        {
            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.UriSource = new Uri(payload.FilePath, UriKind.Absolute);
                image.EndInit();
                image.Freeze();
                dataObject.SetImage(image);
            }
            catch
            {
            }
        }
    }

    private static void WriteUrlPayload(DataObject dataObject, IExportablePayload payload)
    {
        if (!string.Equals(payload.MimeTypeHint, "text/uri-list", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(payload.TextContent))
        {
            return;
        }

        var url = payload.TextContent.Trim();
        dataObject.SetData(UrlFormatW, url);
        dataObject.SetData(UrlFormat, url);
        dataObject.SetText(url, TextDataFormat.UnicodeText);
    }

    private static void WriteHtmlPayload(DataObject dataObject, IExportablePayload payload)
    {
        if (!string.Equals(payload.MimeTypeHint, "text/html", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(payload.TextContent))
        {
            return;
        }

        dataObject.SetData(DataFormats.Html, payload.TextContent);
    }

    private static void WriteTextPayload(DataObject dataObject, IExportablePayload payload)
    {
        if (string.IsNullOrWhiteSpace(payload.TextContent))
        {
            return;
        }

        dataObject.SetText(payload.TextContent, TextDataFormat.UnicodeText);
    }

    private static void WriteImagePayload(DataObject dataObject, IExportablePayload payload)
    {
        if (payload.MemoryBytes is null || payload.MemoryBytes.Length == 0)
        {
            return;
        }

        try
        {
            using var ms = new MemoryStream(payload.MemoryBytes);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = ms;
            image.EndInit();
            image.Freeze();
            dataObject.SetImage(image);
        }
        catch
        {
        }
    }

    private static string? ReadRawTextContent(Resource resource)
    {
        var path = ResolveRawStoragePath(resource);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            return File.ReadAllText(path, Encoding.UTF8);
        }
        catch
        {
            return null;
        }
    }

    private static string? ReadRawUrl(Resource resource)
    {
        var raw = ReadRawTextContent(resource);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        foreach (var line in raw.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (line.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))
            {
                return line[4..].Trim();
            }
        }

        return raw.Trim();
    }

    private static byte[]? ReadRawBytes(Resource resource)
    {
        var path = ResolveRawStoragePath(resource);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            return File.ReadAllBytes(path);
        }
        catch
        {
            return null;
        }
    }

    private static string? ResolveRawStoragePath(Resource resource)
    {
        return resource.InternalPath
               ?? resource.SourceUri
               ?? resource.GetActivePath();
    }

    private static string? NullIfMissing(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        return File.Exists(path) ? path : null;
    }

    public static DragDropEffects DoDragOut(
        DependencyObject source,
        Resource resource,
        DragVariant variant)
    {
        var dataObject = CreateDragData(resource, variant);
        return DragDrop.DoDragDrop(source, dataObject, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
    }

    private static byte[] EncodeBitmapPng(BitmapSource bitmap)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static string? ReadUrl(IDataObject dataObject)
    {
        return ReadStringByFormat(dataObject, UrlFormatW)
               ?? ReadStringByFormat(dataObject, UrlFormat)
               ?? TryReadTextUrl(dataObject);
    }

    private static string? ReadStringByFormat(IDataObject dataObject, string format)
    {
        if (!TryGetData(dataObject, format, out var data))
        {
            return null;
        }

        if (data is string text)
        {
            return NormalizeUrl(text);
        }

        if (data is MemoryStream memoryStream)
        {
            memoryStream.Position = 0;

            using var unicodeReader = new StreamReader(memoryStream, Encoding.Unicode, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
            var parsed = unicodeReader.ReadToEnd();
            if (string.IsNullOrWhiteSpace(parsed))
            {
                memoryStream.Position = 0;
                using var utf8Reader = new StreamReader(memoryStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
                parsed = utf8Reader.ReadToEnd();
            }

            if (string.IsNullOrWhiteSpace(parsed))
            {
                memoryStream.Position = 0;
                using var ansiReader = new StreamReader(memoryStream, Encoding.Default, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
                parsed = ansiReader.ReadToEnd();
            }

            return NormalizeUrl(parsed);
        }

        return null;
    }

    private static string? TryReadTextUrl(IDataObject dataObject)
    {
        if (!TryGetText(dataObject, out var text))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var normalized = NormalizeUrl(text);
        if (Uri.TryCreate(normalized, UriKind.Absolute, out _))
        {
            return normalized;
        }

        return null;
    }

    private static string NormalizeUrl(string value)
    {
        return value.Replace("\0", string.Empty).Trim();
    }

    private static bool TryGetData(IDataObject dataObject, string format, out object? data)
    {
        data = null;
        try
        {
            if (!dataObject.GetDataPresent(format))
            {
                return false;
            }

            data = dataObject.GetData(format);
            return data is not null;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryGetText(IDataObject dataObject, out string? text)
    {
        text = null;
        if (TryGetData(dataObject, DataFormats.UnicodeText, out var unicode) && unicode is string unicodeText)
        {
            text = unicodeText;
            return true;
        }

        if (TryGetData(dataObject, DataFormats.Text, out var ansi) && ansi is string ansiText)
        {
            text = ansiText;
            return true;
        }

        return false;
    }

    private static List<string> TryExtractFileNames(IDataObject dataObject)
    {
        var result = new List<string>();

        foreach (var format in new[] { "FileNameW", "FileName" })
        {
            if (!TryGetData(dataObject, format, out var data) || data is null)
            {
                continue;
            }

            switch (data)
            {
                case string[] array:
                    result.AddRange(array.Where(path => !string.IsNullOrWhiteSpace(path)));
                    break;
                case string single:
                    var pieces = single
                        .Split('\0', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Where(path => !string.IsNullOrWhiteSpace(path));
                    result.AddRange(pieces);
                    break;
                case StringCollection collection:
                    result.AddRange(collection.Cast<string>().Where(path => !string.IsNullOrWhiteSpace(path)));
                    break;
            }
        }

        return result;
    }
}