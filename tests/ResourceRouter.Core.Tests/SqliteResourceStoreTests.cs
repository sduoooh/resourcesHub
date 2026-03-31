using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using ResourceRouter.Core.Models;
using ResourceRouter.Infrastructure.Storage;

namespace ResourceRouter.Core.Tests;

public sealed class SqliteResourceStoreTests
{
    [Fact]
    public async Task SqliteStore_UpsertAndRead_RoundTripsSummaryAndHealth()
    {
        var dbPath = Path.Combine(LocalPathProvider.TestTempDirectory, $"rr-store-{Guid.NewGuid():N}.db");

        var store = new SqliteResourceStore(dbPath);
        var resourceId = Guid.NewGuid();

        var resource = new Resource
        {
            Id = resourceId,
            CreatedAt = DateTimeOffset.UtcNow,
            SourceUri = @"C:\temp\sample.txt",
            OriginalFileName = "sample.txt",
            MimeType = "text/plain",
            FileSize = 42,
            Source = ResourceSource.FromVSCode,
            Summary = "This is a summary",
            ConditionTags = Array.Empty<string>(),
            PropertyTags = new[] { "alpha", "beta", "manual" },
            ProcessedText = "sample text",
            Health = new ResourceHealthStatus
            {
                LastCheckAt = DateTimeOffset.UtcNow,
                LastCheckPassed = true,
                LastCheckMessage = "ok"
            }
        };

        await store.UpsertAsync(resource);
        var restored = await store.GetByIdAsync(resourceId);
        var searched = await store.SearchAsync("summary", 10, 0);

        Assert.NotNull(restored);
        Assert.Equal(resource.Summary, restored!.Summary);
        Assert.Equal(resource.Health.LastCheckPassed, restored.Health.LastCheckPassed);
        Assert.Equal(resource.Health.LastCheckMessage, restored.Health.LastCheckMessage);
        Assert.Contains(searched, item => item.Id == resourceId);
    }

    [Fact]
    public async Task SqliteStore_RoundTripsSingleTableRawAndProcessedPayload()
    {
        var dbPath = Path.Combine(LocalPathProvider.TestTempDirectory, $"rr-split-{Guid.NewGuid():N}.db");
        var id = Guid.NewGuid();

        var rawDir = Path.Combine(LocalPathProvider.TestTempDirectory, $"rr-raw-{Guid.NewGuid():N}");
        Directory.CreateDirectory(rawDir);
        var rawPath = Path.Combine(rawDir, "sample.txt");
        await File.WriteAllTextAsync(rawPath, "raw");

        var store = new SqliteResourceStore(dbPath);
        var resource = new Resource
        {
            Id = id,
            CreatedAt = DateTimeOffset.UtcNow,
            RawKind = RawDropKind.Text,
            InternalPath = rawPath,
            PersistencePolicy = PersistencePolicy.Unified,
            OriginalFileName = "sample.txt",
            MimeType = "text/plain",
            FileSize = 3,
            Source = ResourceSource.Manual,
            ProcessedRouteId = "sample-route",
            ProcessedText = "processed",
            ConditionTags = new[] { "config" },
            PropertyTags = new[] { "tag-a" },
            PermissionPresetId = PermissionPreset.PrivatePresetId,
            State = ResourceState.Ready
        };

        await store.UpsertAsync(resource);
        var restored = await store.GetByIdAsync(id);

        Assert.NotNull(restored);
        Assert.Equal(RawDropKind.Text, restored!.RawKind);
        Assert.Equal(rawPath, restored.InternalPath);
        Assert.Equal("sample-route", restored.ProcessedRouteId);
        Assert.Equal("processed", restored.ProcessedText);
        Assert.Contains("config", restored.ConditionTags);
        Assert.Contains("tag-a", restored.PropertyTags);

        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWrite
        }.ToString());

        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT COUNT(1)
FROM sqlite_master
WHERE type = 'table'
  AND name IN ('resource_raw_payloads', 'resource_processed_payloads');";

        var legacyTableCount = Convert.ToInt32(await command.ExecuteScalarAsync());
        Assert.Equal(0, legacyTableCount);
    }

    [Fact]
    public async Task SqliteStore_AppliesConditionTagVisibilityInDatabaseLayer()
    {
        var dbPath = Path.Combine(LocalPathProvider.TestTempDirectory, $"rr-condition-{Guid.NewGuid():N}.db");
        var store = new SqliteResourceStore(dbPath);

        var publicResource = new Resource
        {
            Id = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow,
            OriginalFileName = "public.txt",
            MimeType = "text/plain",
            FileSize = 12,
            Source = ResourceSource.Manual,
            Summary = "public",
            ConditionTags = Array.Empty<string>(),
            PropertyTags = new[] { "demo" },
            PermissionPresetId = PermissionPreset.PrivatePresetId,
            State = ResourceState.Ready
        };

        var configResource = new Resource
        {
            Id = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow,
            OriginalFileName = "config.txt",
            MimeType = "text/plain",
            FileSize = 12,
            Source = ResourceSource.Manual,
            Summary = "config",
            ConditionTags = new[] { "config" },
            PropertyTags = new[] { "demo" },
            PermissionPresetId = PermissionPreset.PrivatePresetId,
            State = ResourceState.Ready
        };

        await store.UpsertAsync(publicResource);
        await store.UpsertAsync(configResource);

        var defaultVisible = await store.ListRecentAsync(20);
        var includeConfig = await store.ListRecentAsync(20, new[] { "config" }, applyConditionVisibility: true);
        var bypassVisibility = await store.ListRecentAsync(20, applyConditionVisibility: false);

        Assert.Contains(defaultVisible, item => item.Id == publicResource.Id);
        Assert.DoesNotContain(defaultVisible, item => item.Id == configResource.Id);

        Assert.Contains(includeConfig, item => item.Id == configResource.Id);
        Assert.DoesNotContain(includeConfig, item => item.Id == publicResource.Id);

        Assert.Contains(bypassVisibility, item => item.Id == publicResource.Id);
        Assert.Contains(bypassVisibility, item => item.Id == configResource.Id);
    }
}
