using System;
using System.Collections.Generic;
using System.Linq;

namespace ResourceRouter.PluginSdk;

public sealed class ExtractedContent
{
    public IReadOnlyList<ContentParagraph> Paragraphs { get; init; } = Array.Empty<ContentParagraph>();

    public string ToPlainText()
    {
        return string.Join(Environment.NewLine, Paragraphs.Select(p => p.Text));
    }
}