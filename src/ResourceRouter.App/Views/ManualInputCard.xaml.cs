using System;
using System.Windows;
using System.Windows.Controls;
using ResourceRouter.Core.Models;

namespace ResourceRouter.App.Views;

public partial class ManualInputCard : UserControl
{
    public ManualInputCard()
    {
        InitializeComponent();
    }

    public event EventHandler<ManualInputSubmittedEventArgs>? Submitted;

    private void OnSubmitClick(object sender, RoutedEventArgs e)
    {
        var body = BodyTextBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(body))
        {
            return;
        }

        var raw = new RawDropData
        {
            Kind = RawDropKind.Text,
            Text = body,
            SourceAppHint = "manual",
            OriginalSuggestedName = "manual.txt",
            CapturedAt = DateTimeOffset.UtcNow
        };

        Submitted?.Invoke(this, new ManualInputSubmittedEventArgs
        {
            RawDropData = raw,
            TitleOverride = string.IsNullOrWhiteSpace(TitleTextBox.Text) ? null : TitleTextBox.Text.Trim()
        });

        BodyTextBox.Clear();
    }
}