using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using DbUp;
using Microsoft.Data.Sqlite;
using ResourceRouter.Core.Abstractions;
using ResourceRouter.Core.Models;

namespace ResourceRouter.Infrastructure.Storage;

public sealed class SqliteResourceStore : IResourceStore
{
    private static readonly string SelectProjection = EmbeddedSql.Load("SelectProjection.sql");
    private static readonly string UpsertSql = EmbeddedSql.Load("UpsertResource.sql");
    private static readonly string SearchFtsSql = EmbeddedSql.Load("SearchFts.sql");
    private static readonly string InsertFtsSql = EmbeddedSql.Load("InsertFts.sql");

    private readonly string _connectionString;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _initialized;

    private static readonly object DapperInitLock = new();
    private static bool _dapperConfigured;

    public SqliteResourceStore(string? dbPath = null)
    {
        ConfigureDapper();

        LocalPathProvider.EnsureAll();
        var path = string.IsNullOrWhiteSpace(dbPath)
            ? System.IO.Path.Combine(LocalPathProvider.DbDirectory, "resources.db")
            : dbPath;

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();
    }

    public async Task UpsertAsync(Resource resource, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        var writeModel = ToWriteModel(resource);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await connection.ExecuteAsync(
                new CommandDefinition(
                    UpsertSql,
                    writeModel,
                    transaction,
                    cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        await connection.ExecuteAsync(
                new CommandDefinition(
                    "DELETE FROM fts_resources WHERE resource_id = @Id;",
                    new { writeModel.Id },
                    transaction,
                    cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        await connection.ExecuteAsync(
                new CommandDefinition(
                    InsertFtsSql,
                    new
                    {
                        Id = writeModel.Id,
                        Title = resource.DisplayTitle,
                        Notes = resource.UserNotes ?? string.Empty,
                        ProcessedText = resource.ProcessedText ?? string.Empty,
                        Summary = resource.Summary ?? string.Empty,
                        Tags = BuildTagContent(resource)
                    },
                    transaction,
                    cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<Resource?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var row = await connection.QuerySingleOrDefaultAsync<ResourceRow>(
                new CommandDefinition(
                    SelectProjection + " WHERE id = @Id LIMIT 1;",
                    new { Id = id.ToString("D") },
                    cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        return row is null ? null : ToResource(row);
    }

    public async Task<Resource?> GetByFeatureHashAsync(string featureHash, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(featureHash))
        {
            return null;
        }

        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var row = await connection.QuerySingleOrDefaultAsync<ResourceRow>(
                new CommandDefinition(
                    SelectProjection + " WHERE feature_hash = @FeatureHash ORDER BY created_at DESC LIMIT 1;",
                    new { FeatureHash = featureHash },
                    cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        return row is null ? null : ToResource(row);
    }

    public async Task<IReadOnlyList<Resource>> ListRecentAsync(int limit, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var rows = await connection.QueryAsync<ResourceRow>(
                new CommandDefinition(
                    SelectProjection + " ORDER BY created_at DESC LIMIT @Limit;",
                    new { Limit = limit },
                    cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        return rows.AsList().ConvertAll(ToResource);
    }

    public async Task<IReadOnlyList<Resource>> SearchAsync(string query, int limit, int offset, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        CommandDefinition command;
        if (string.IsNullOrWhiteSpace(query))
        {
            command = new CommandDefinition(
                SelectProjection + " ORDER BY created_at DESC LIMIT @Limit OFFSET @Offset;",
                new { Limit = limit, Offset = offset },
                cancellationToken: cancellationToken);
        }
        else
        {
            command = new CommandDefinition(
                SearchFtsSql,
                new { Query = query, Limit = limit, Offset = offset },
                cancellationToken: cancellationToken);
        }

        var rows = await connection.QueryAsync<ResourceRow>(command).ConfigureAwait(false);
        return rows.AsList().ConvertAll(ToResource);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        var idText = id.ToString("D");

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await connection.ExecuteAsync(
                new CommandDefinition(
                    "DELETE FROM fts_resources WHERE resource_id = @Id;",
                    new { Id = idText },
                    cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        await connection.ExecuteAsync(
                new CommandDefinition(
                    "DELETE FROM resources WHERE id = @Id;",
                    new { Id = idText },
                    cancellationToken: cancellationToken))
            .ConfigureAwait(false);
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_initialized)
        {
            return;
        }

        await _initLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initialized)
            {
                return;
            }

            cancellationToken.ThrowIfCancellationRequested();
            await Task.Run(ApplyMigrations, cancellationToken).ConfigureAwait(false);

            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    private void ApplyMigrations()
    {
        var upgrader = DeployChanges.To
            .SQLiteDatabase(_connectionString)
            .WithScriptsEmbeddedInAssembly(
                typeof(SqliteResourceStore).Assembly,
                scriptName => scriptName.Contains("ResourceRouter.Infrastructure.Storage.Migrations", StringComparison.Ordinal))
            .LogToNowhere()
            .Build();

        var result = upgrader.PerformUpgrade();
        if (!result.Successful)
        {
            throw new InvalidOperationException("数据库迁移执行失败。", result.Error);
        }
    }

    private static void ConfigureDapper()
    {
        lock (DapperInitLock)
        {
            if (_dapperConfigured)
            {
                return;
            }

            DefaultTypeMap.MatchNamesWithUnderscores = true;
            SqlMapper.AddTypeHandler(new StringListTypeHandler());
            SqlMapper.AddTypeHandler(new ResourceHealthStatusTypeHandler());

            _dapperConfigured = true;
        }
    }

    private static Resource ToResource(ResourceRow row)
    {
        var health = row.Health ?? new ResourceHealthStatus();
        if (IsEmptyHealth(health))
        {
            health = new ResourceHealthStatus
            {
                LastCheckAt = ParseOptionalDateTimeOffset(row.LastHealthCheckAt),
                LastCheckPassed = row.LastHealthCheckPassed.HasValue ? row.LastHealthCheckPassed.Value != 0 : null,
                LastCheckMessage = NullIfEmpty(row.LastHealthCheckMessage)
            };
        }

        return new Resource
        {
            Id = Guid.Parse(row.Id),
            CreatedAt = DateTimeOffset.Parse(row.CreatedAt),
            SourceUri = NullIfEmpty(row.SourceUri),
            InternalPath = NullIfEmpty(row.InternalPath),
            PersistencePolicy = (PersistencePolicy)row.PersistencePolicy,
            SourceLastModifiedAt = ParseOptionalDateTimeOffset(row.SourceLastModifiedAt),
            OriginalFileName = row.OriginalFileName,
            MimeType = row.MimeType,
            FileSize = row.FileSize,
            Source = (ResourceSource)row.Source,
            ProcessedFilePath = NullIfEmpty(row.ProcessedFilePath),
            ProcessedText = NullIfEmpty(row.ProcessedText),
            ProcessedRouteId = NullIfEmpty(row.ProcessedRouteId),
            ThumbnailPath = NullIfEmpty(row.ThumbnailPath),
            Summary = NullIfEmpty(row.Summary),
            AutoTags = row.AutoTags ?? Array.Empty<string>(),
            UserTitle = NullIfEmpty(row.UserTitle),
            UserNotes = NullIfEmpty(row.UserNotes),
            UserTags = row.UserTags ?? Array.Empty<string>(),
            Privacy = (PrivacyLevel)row.Privacy,
            SyncPolicy = (SyncPolicy)row.SyncPolicy,
            SyncTargetDevices = row.SyncTargetDevices ?? Array.Empty<string>(),
            ProcessingModel = (ModelType)row.ProcessingModel,
            PermissionPresetId = row.PermissionPresetId,
            State = (ResourceState)row.State,
            WaitingExpiresAt = ParseOptionalDateTimeOffset(row.WaitingExpiresAt),
            LastError = NullIfEmpty(row.LastError),
            FeatureHash = NullIfEmpty(row.FeatureHash),
            Health = health
        };
    }

    private static ResourceWriteModel ToWriteModel(Resource resource)
    {
        var health = resource.Health ?? new ResourceHealthStatus();

        return new ResourceWriteModel
        {
            Id = resource.Id.ToString("D"),
            CreatedAt = resource.CreatedAt.ToString("O"),
            SourceUri = resource.SourceUri ?? string.Empty,
            InternalPath = resource.InternalPath ?? string.Empty,
            PersistencePolicy = (int)resource.PersistencePolicy,
            SourceLastModifiedAt = resource.SourceLastModifiedAt?.ToString("O") ?? string.Empty,
            OriginalFileName = resource.OriginalFileName,
            MimeType = resource.MimeType,
            FileSize = resource.FileSize,
            Source = (int)resource.Source,
            ProcessedFilePath = resource.ProcessedFilePath ?? string.Empty,
            ProcessedText = resource.ProcessedText ?? string.Empty,
            ProcessedRouteId = resource.ProcessedRouteId ?? string.Empty,
            ThumbnailPath = resource.ThumbnailPath ?? string.Empty,
            Summary = resource.Summary ?? string.Empty,
            AutoTags = resource.AutoTags ?? Array.Empty<string>(),
            UserTitle = resource.UserTitle ?? string.Empty,
            UserNotes = resource.UserNotes ?? string.Empty,
            UserTags = resource.UserTags ?? Array.Empty<string>(),
            Privacy = (int)resource.Privacy,
            SyncPolicy = (int)resource.SyncPolicy,
            SyncTargetDevices = resource.SyncTargetDevices ?? Array.Empty<string>(),
            ProcessingModel = (int)resource.ProcessingModel,
            PermissionPresetId = resource.PermissionPresetId,
            State = (int)resource.State,
            WaitingExpiresAt = resource.WaitingExpiresAt?.ToString("O") ?? string.Empty,
            LastError = resource.LastError ?? string.Empty,
            FeatureHash = resource.FeatureHash ?? string.Empty,
            Health = IsEmptyHealth(health) ? null : health,
            LastHealthCheckAt = health.LastCheckAt?.ToString("O") ?? string.Empty,
            LastHealthCheckPassed = health.LastCheckPassed.HasValue ? (health.LastCheckPassed.Value ? 1 : 0) : null,
            LastHealthCheckMessage = health.LastCheckMessage ?? string.Empty
        };
    }

    private static bool IsEmptyHealth(ResourceHealthStatus health)
    {
        return health.LastCheckAt is null
               && health.LastCheckPassed is null
               && string.IsNullOrWhiteSpace(health.LastCheckMessage);
    }

    private static DateTimeOffset? ParseOptionalDateTimeOffset(string? raw)
    {
        return string.IsNullOrWhiteSpace(raw) ? null : DateTimeOffset.Parse(raw);
    }

    private static string BuildTagContent(Resource resource)
    {
        var userTags = string.Join(' ', resource.UserTags ?? Array.Empty<string>());
        var autoTags = string.Join(' ', resource.AutoTags ?? Array.Empty<string>());
        return userTags + " " + autoTags;
    }

    private static string? NullIfEmpty(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private sealed class ResourceWriteModel
    {
        public string Id { get; set; } = string.Empty;

        public string CreatedAt { get; set; } = string.Empty;

        public string SourceUri { get; set; } = string.Empty;

        public string InternalPath { get; set; } = string.Empty;

        public int PersistencePolicy { get; set; }

        public string SourceLastModifiedAt { get; set; } = string.Empty;

        public string OriginalFileName { get; set; } = string.Empty;

        public string MimeType { get; set; } = string.Empty;

        public long FileSize { get; set; }

        public int Source { get; set; }

        public string ProcessedFilePath { get; set; } = string.Empty;

        public string ProcessedText { get; set; } = string.Empty;

        public string ProcessedRouteId { get; set; } = string.Empty;

        public string ThumbnailPath { get; set; } = string.Empty;

        public string Summary { get; set; } = string.Empty;

        public IReadOnlyList<string> AutoTags { get; set; } = Array.Empty<string>();

        public string UserTitle { get; set; } = string.Empty;

        public string UserNotes { get; set; } = string.Empty;

        public IReadOnlyList<string> UserTags { get; set; } = Array.Empty<string>();

        public int Privacy { get; set; }

        public int SyncPolicy { get; set; }

        public IReadOnlyList<string> SyncTargetDevices { get; set; } = Array.Empty<string>();

        public int ProcessingModel { get; set; }

        public string PermissionPresetId { get; set; } = string.Empty;

        public int State { get; set; }

        public string WaitingExpiresAt { get; set; } = string.Empty;

        public string LastError { get; set; } = string.Empty;

        public string FeatureHash { get; set; } = string.Empty;

        public ResourceHealthStatus? Health { get; set; }

        public string LastHealthCheckAt { get; set; } = string.Empty;

        public int? LastHealthCheckPassed { get; set; }

        public string LastHealthCheckMessage { get; set; } = string.Empty;
    }

    private sealed class ResourceRow
    {
        public string Id { get; set; } = string.Empty;

        public string CreatedAt { get; set; } = string.Empty;

        public string? SourceUri { get; set; }

        public string? InternalPath { get; set; }

        public int PersistencePolicy { get; set; }

        public string? SourceLastModifiedAt { get; set; }

        public string OriginalFileName { get; set; } = string.Empty;

        public string MimeType { get; set; } = string.Empty;

        public long FileSize { get; set; }

        public int Source { get; set; }

        public string? ProcessedFilePath { get; set; }

        public string? ProcessedText { get; set; }

        public string? ProcessedRouteId { get; set; }

        public string? ThumbnailPath { get; set; }

        public string? Summary { get; set; }

        public IReadOnlyList<string>? AutoTags { get; set; }

        public string? UserTitle { get; set; }

        public string? UserNotes { get; set; }

        public IReadOnlyList<string>? UserTags { get; set; }

        public int Privacy { get; set; }

        public int SyncPolicy { get; set; }

        public IReadOnlyList<string>? SyncTargetDevices { get; set; }

        public int ProcessingModel { get; set; }

        public string PermissionPresetId { get; set; } = string.Empty;

        public int State { get; set; }

        public string? WaitingExpiresAt { get; set; }

        public string? LastError { get; set; }

        public string? FeatureHash { get; set; }

        public ResourceHealthStatus? Health { get; set; }

        public string? LastHealthCheckAt { get; set; }

        public long? LastHealthCheckPassed { get; set; }

        public string? LastHealthCheckMessage { get; set; }
    }
}
