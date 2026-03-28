using ResourceRouter.Core.Models;

namespace ResourceRouter.Core.Tests;

public class RawDropDataTests
{
    [Fact]
    public void CanCreateFileDropData()
    {
        var drop = new RawDropData
        {
            Kind = RawDropKind.File,
            FilePaths = new[] { @"C:\a.txt" }
        };

        Assert.Equal(RawDropKind.File, drop.Kind);
        Assert.Single(drop.FilePaths);
    }

    [Fact]
    public void CanCreateTextDropData()
    {
        var drop = new RawDropData
        {
            Kind = RawDropKind.Text,
            Text = "hello"
        };

        Assert.Equal(RawDropKind.Text, drop.Kind);
        Assert.Equal("hello", drop.Text);
    }

    [Fact]
    public void CanCreateHtmlDropData()
    {
        var drop = new RawDropData
        {
            Kind = RawDropKind.Html,
            Html = "<p>hello</p>"
        };

        Assert.Equal(RawDropKind.Html, drop.Kind);
        Assert.Contains("hello", drop.Html!);
    }

    [Fact]
    public void CanCreateBitmapDropData()
    {
        var drop = new RawDropData
        {
            Kind = RawDropKind.Bitmap,
            BitmapBytes = new byte[] { 1, 2, 3 }
        };

        Assert.Equal(RawDropKind.Bitmap, drop.Kind);
        Assert.Equal(3, drop.BitmapBytes!.Length);
    }

    [Fact]
    public void CanCreateUrlDropData()
    {
        var drop = new RawDropData
        {
            Kind = RawDropKind.Url,
            Url = "https://example.com"
        };

        Assert.Equal(RawDropKind.Url, drop.Kind);
        Assert.Equal("https://example.com", drop.Url);
    }
}