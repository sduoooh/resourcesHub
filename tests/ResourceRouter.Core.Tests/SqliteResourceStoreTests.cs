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
        var dbPath = Path.Combine(Path.GetTempPath(), $"rr-store-{Guid.NewGuid():N}.db");

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
            AutoTags = new[] { "alpha", "beta" },
            UserTags = new[] { "manual" },
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
    public async Task SqliteStore_MigratesLegacyHealthColumns_ToHealthJson()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"rr-legacy-{Guid.NewGuid():N}.db");
        var id = Guid.NewGuid();

        await CreateLegacyDatabaseAsync(dbPath, id);

        var store = new SqliteResourceStore(dbPath);
        var restored = await store.GetByIdAsync(id);

        Assert.NotNull(restored);
        Assert.Equal("legacy summary", restored!.Summary);
        Assert.True(restored.Health.LastCheckPassed);
        Assert.Equal("legacy health", restored.Health.LastCheckMessage);
        Assert.NotNull(restored.Health.LastCheckAt);
    }

    private static async Task CreateLegacyDatabaseAsync(string dbPath, Guid id)
    {
        await using var connection = new SqliteConnection($"Data Source={dbPath};Mode=ReadWriteCreate;Cache=Shared");
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE resources (
                id TEXT NOT NULL PRIMARY KEY,
                created_at TEXT NOT NULL,
                source_uri TEXT NULL,
                internal_path TEXT NULL,
                persistence_policy INTEGER NOT NULL DEFAULT 0,
                source_last_modified_at TEXT NULL,
                original_file_name TEXT NOT NULL,
                mime_type TEXT NOT NULL,
                file_size INTEGER NOT NULL,
                source INTEGER NOT NULL,
                processed_file_path TEXT NULL,
                processed_text TEXT NULL,
                processed_route_id TEXT NULL,
                thumbnail_path TEXT NULL,
                ai_summary TEXT NULL,
                auto_tags_json TEXT NOT NULL,
                user_title TEXT NULL,
                user_notes TEXT NULL,
                user_tags_json TEXT NOT NULL,
                privacy INTEGER NOT NULL,
                sync_policy INTEGER NOT NULL,
                sync_target_devices_json TEXT NOT NULL,
                processing_model INTEGER NOT NULL,
                permission_preset_id TEXT NOT NULL,
                state INTEGER NOT NULL,
                waiting_expires_at TEXT NULL,
                last_error TEXT NULL,
                feature_hash TEXT NULL,
                last_health_check_at TEXT NULL,
                last_health_check_passed INTEGER NULL,
                last_health_check_message TEXT NULL
            );

            CREATE VIRTUAL TABLE fts_resources USING fts5(
                resource_id UNINDEXED,
                title,
                notes,
                processed_text,
                ai_summary,
                tags
            );
            """;

        await command.ExecuteNonQueryAsync();

        var insert = connection.CreateCommand();
        insert.CommandText =
            """
            INSERT INTO resources (
                id, created_at, source_uri, internal_path, persistence_policy, source_last_modified_at, original_file_name, mime_type, file_size, source,
                processed_file_path, processed_text, processed_route_id, thumbnail_path, ai_summary,
                auto_tags_json, user_title, user_notes, user_tags_json,
                privacy, sync_policy, sync_target_devices_json, processing_model,
                permission_preset_id, state, waiting_expires_at, last_error,
                feature_hash, last_health_check_at, last_health_check_passed, last_health_check_message
            )
            VALUES (
                @id, @created_at, @source_uri, @internal_path, @persistence_policy, @source_last_modified_at, @original_file_name, @mime_type, @file_size, @source,
                @processed_file_path, @processed_text, @processed_route_id, @thumbnail_path, @ai_summary,
                @auto_tags_json, @user_title, @user_notes, @user_tags_json,
                @privacy, @sync_policy, @sync_target_devices_json, @processing_model,
                @permission_preset_id, @state, @waiting_expires_at, @last_error,
                @feature_hash, @last_health_check_at, @last_health_check_passed, @last_health_check_message
            );
            """;

        insert.Parameters.AddWithValue("@id", id.ToString("D"));
        insert.Parameters.AddWithValue("@created_at", DateTimeOffset.UtcNow.ToString("O"));
        insert.Parameters.AddWithValue("@source_uri", @"C:\legacy.txt");
        insert.Parameters.AddWithValue("@internal_path", string.Empty);
        insert.Parameters.AddWithValue("@persistence_policy", 0);
        insert.Parameters.AddWithValue("@source_last_modified_at", string.Empty);
        insert.Parameters.AddWithValue("@original_file_name", "legacy.txt");
        insert.Parameters.AddWithValue("@mime_type", "text/plain");
        insert.Parameters.AddWithValue("@file_size", 10);
        insert.Parameters.AddWithValue("@source", 3);
        insert.Parameters.AddWithValue("@processed_file_path", string.Empty);
        insert.Parameters.AddWithValue("@processed_text", "legacy text");
        insert.Parameters.AddWithValue("@processed_route_id", string.Empty);
        insert.Parameters.AddWithValue("@thumbnail_path", string.Empty);
        insert.Parameters.AddWithValue("@ai_summary", "legacy summary");
        insert.Parameters.AddWithValue("@auto_tags_json", "[]");
        insert.Parameters.AddWithValue("@user_title", string.Empty);
        insert.Parameters.AddWithValue("@user_notes", string.Empty);
        insert.Parameters.AddWithValue("@user_tags_json", "[]");
        insert.Parameters.AddWithValue("@privacy", 0);
        insert.Parameters.AddWithValue("@sync_policy", 0);
        insert.Parameters.AddWithValue("@sync_target_devices_json", "[]");
        insert.Parameters.AddWithValue("@processing_model", 0);
        insert.Parameters.AddWithValue("@permission_preset_id", PermissionPreset.PrivatePresetId);
        insert.Parameters.AddWithValue("@state", 2);
        insert.Parameters.AddWithValue("@waiting_expires_at", string.Empty);
        insert.Parameters.AddWithValue("@last_error", string.Empty);
        insert.Parameters.AddWithValue("@feature_hash", string.Empty);
        insert.Parameters.AddWithValue("@last_health_check_at", DateTimeOffset.UtcNow.ToString("O"));
        insert.Parameters.AddWithValue("@last_health_check_passed", 1);
        insert.Parameters.AddWithValue("@last_health_check_message", "legacy health");

        await insert.ExecuteNonQueryAsync();
    }

}
