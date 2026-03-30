using System;
using System.Text.Json;
using ResourceRouter.Core.Models;

namespace ResourceRouter.Core.Tests;

public class ResourceModelTests
{
    [Fact]
    public void Resource_CanSerializeAndDeserialize()
    {
        var resource = new Resource
        {
            Id = Guid.NewGuid(),
            SourceUri = @"C:\temp\sample.txt",
            OriginalFileName = "sample.txt",
            MimeType = "text/plain",
            FileSize = 128,
            Source = ResourceSource.FromVSCode,
            ProcessedText = "hello",
            TitleOverride = "my title",
            Annotations = "notes",
            ConditionTags = new[] { "config" },
            PropertyTags = new[] { "a", "b", "c" },
            Privacy = PrivacyLevel.Private,
            SyncPolicy = SyncPolicy.LocalOnly,
            ProcessingModel = ModelType.LocalSmall,
            PermissionPresetId = PermissionPreset.PrivatePresetId,
            State = ResourceState.Waiting,
            WaitingExpiresAt = DateTimeOffset.UtcNow.AddMinutes(3)
        };

        var json = JsonSerializer.Serialize(resource);
        var restored = JsonSerializer.Deserialize<Resource>(json);

        Assert.NotNull(restored);
        Assert.Equal(resource.Id, restored!.Id);
        Assert.Equal(resource.SourceUri, restored.SourceUri);
        Assert.Equal(resource.MimeType, restored.MimeType);
        Assert.Equal(resource.TitleOverride, restored.TitleOverride);
        Assert.Equal(resource.State, restored.State);
    }
}