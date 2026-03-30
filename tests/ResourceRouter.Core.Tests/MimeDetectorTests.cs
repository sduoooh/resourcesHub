using System;
using System.IO;
using ResourceRouter.Infrastructure.Format;
using ResourceRouter.Infrastructure.Storage;

namespace ResourceRouter.Core.Tests;

public class MimeDetectorTests
{
    [Fact]
    public void DetectFromFilePath_UsesExtensionWhenAvailable()
    {
        var mime = MimeDetector.DetectFromFilePath("note.md");

        Assert.Equal("text/markdown", mime);
    }

    [Fact]
    public void DetectFromFilePath_FallsBackToPdfSignature()
    {
        var path = CreateTempFileWithoutExtension(new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x37 });
        try
        {
            var mime = MimeDetector.DetectFromFilePath(path);

            Assert.Equal("application/pdf", mime);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void DetectFromFilePath_FallsBackToWavSignature()
    {
        var wavHeader = new byte[]
        {
            0x52, 0x49, 0x46, 0x46,
            0x24, 0x08, 0x00, 0x00,
            0x57, 0x41, 0x56, 0x45,
            0x66, 0x6D, 0x74, 0x20
        };

        var path = CreateTempFileWithoutExtension(wavHeader);
        try
        {
            var mime = MimeDetector.DetectFromFilePath(path);

            Assert.Equal("audio/wav", mime);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string CreateTempFileWithoutExtension(byte[] data)
    {
        var path = Path.Combine(LocalPathProvider.TestTempDirectory, Guid.NewGuid().ToString("N"));
        File.WriteAllBytes(path, data);
        return path;
    }
}