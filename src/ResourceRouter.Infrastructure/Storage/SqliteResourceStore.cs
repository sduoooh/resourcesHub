using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
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
    private static readonly string UpsertRawPayloadSql = EmbeddedSql.Load("UpsertRawPayload.sql");
    private static readonly string UpsertProcessedPayloadSql = EmbeddedSql.Load("UpsertProcessedPayload.sql");
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

        var resourceWriteModel = ToResourceWriteModel(resource);
        var rawPayloadWriteModel = ToRawPayloadWriteModel(resource);
        var processedPayloadWriteModel = ToProcessedPayloadWriteModel(resource);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await connection.ExecuteAsync(
                new CommandDefinition(
                    UpsertSql,
                    resourceWriteModel,
                    transaction,
                    cancellationToken: cancellationToken))
                .ConfigureAwait(false);

            await connection.ExecuteAsync(
                new CommandDefinition(
                    UpsertRawPayloadSql,
                    rawPayloadWriteModel,
                    transaction,
                    cancellationToken: cancellationToken))
            .ConfigureAwait(false);

            if (processedPayloadWriteModel.HasAnyPayload)
            {
                await connection.ExecuteAsync(
                    new CommandDefinition(
                    UpsertProcessedPayloadSql,
                    processedPayloadWriteModel,
                    transaction,
                    cancellationToken: cancellationToken))
                .ConfigureAwait(false);
            }
            else
            {
                await connection.ExecuteAsync(
                    new CommandDefinition(
                    "DELETE FROM resource_processed_payloads WHERE resource_id = @ResourceId;",
                    new { ResourceId = resourceWriteModel.Id },
                    transaction,
                    cancellationToken: cancellationToken))
                .ConfigureAwait(false);
            }

        await connection.ExecuteAsync(
                new CommandDefinition(
                    "DELETE FROM fts_resources WHERE resource_id = @Id;",
                    new { resourceWriteModel.Id },
                    transaction,
                    cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        await connection.ExecuteAsync(
                new CommandDefinition(
                    InsertFtsSql,
                    new
                    {
                        Id = resourceWriteModel.Id,
                        Title = resource.DisplayTitle,
                        Notes = resource.Annotations ?? string.Empty,
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
                SelectProjection + " WHERE r.id = @Id LIMIT 1;",
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
                SelectProjection + " WHERE r.feature_hash = @FeatureHash ORDER BY r.created_at DESC LIMIT 1;",
                    new { FeatureHash = featureHash },
                    cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        return row is null ? null : ToResource(row);
    }

    public async Task<IReadOnlyList<Resource>> ListRecentAsync(
        int limit,
        IReadOnlyList<string>? tagFilters = null,
        bool applyConditionVisibility = true,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var parameters = BuildTagQueryParameters(
            query: null,
            limit,
            offset: 0,
            tagFilters,
            applyConditionVisibility);

        var sql = SelectProjection +
                  " WHERE " + BuildTagVisibilityPredicate("r.") +
                  " ORDER BY r.created_at DESC LIMIT @Limit;";

        var rows = await connection.QueryAsync<ResourceRow>(
                new CommandDefinition(
                    sql,
                    parameters,
                    cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        return rows.AsList().ConvertAll(ToResource);
    }

    public async Task<IReadOnlyList<Resource>> SearchAsync(
        string query,
        int limit,
        int offset,
        IReadOnlyList<string>? tagFilters = null,
        bool applyConditionVisibility = true,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var parameters = BuildTagQueryParameters(
            query,
            limit,
            offset,
            tagFilters,
            applyConditionVisibility);

        CommandDefinition command;
        if (string.IsNullOrWhiteSpace(query))
        {
            var sql = SelectProjection +
                      " WHERE " + BuildTagVisibilityPredicate("r.") +
                      " ORDER BY r.created_at DESC LIMIT @Limit OFFSET @Offset;";

            command = new CommandDefinition(
                sql,
                parameters,
                cancellationToken: cancellationToken);
        }
        else
        {
            var sql = SearchFtsSql.Replace(
                "/*TAG_FILTERS*/",
                "AND " + BuildTagVisibilityPredicate("r."));

            command = new CommandDefinition(
                sql,
                parameters,
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
                "DELETE FROM resource_processed_payloads WHERE resource_id = @Id;",
                new { Id = idText },
                cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        await connection.ExecuteAsync(
            new CommandDefinition(
                "DELETE FROM resource_raw_payloads WHERE resource_id = @Id;",
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

        var rawKindValue = row.RawKind ?? (int)RawDropKind.File;
        var rawKind = Enum.IsDefined(typeof(RawDropKind), rawKindValue)
            ? (RawDropKind)rawKindValue
            : RawDropKind.File;

        return new Resource
        {
            Id = Guid.Parse(row.Id),
            CreatedAt = DateTimeOffset.Parse(row.CreatedAt),
            RawKind = rawKind,
            SourceUri = NullIfEmpty(row.SourceUri),
            InternalPath = NullIfEmpty(row.InternalPath),
            PersistencePolicy = (PersistencePolicy)row.PersistencePolicy,
            SourceLastModifiedAt = ParseOptionalDateTimeOffset(row.SourceLastModifiedAt),
            SourceAppHint = NullIfEmpty(row.SourceAppHint),
            CapturedAt = ParseOptionalDateTimeOffset(row.CapturedAt),
            OriginalSuggestedName = NullIfEmpty(row.OriginalSuggestedName),
            OriginalFileName = row.OriginalFileName,
            MimeType = row.MimeType,
            FileSize = row.FileSize,
            Source = (ResourceSource)row.Source,
            ProcessedFilePath = NullIfEmpty(row.ProcessedFilePath),
            ProcessedText = NullIfEmpty(row.ProcessedText),
            ProcessedRouteId = NullIfEmpty(row.ProcessedRouteId),
            ThumbnailPath = NullIfEmpty(row.ThumbnailPath),
            Summary = NullIfEmpty(row.Summary),
            ConditionTags = row.ConditionTags ?? Array.Empty<string>(),
            TitleOverride = NullIfEmpty(row.TitleOverride),
            Annotations = NullIfEmpty(row.Annotations),
            PropertyTags = row.PropertyTags ?? Array.Empty<string>(),
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

    private static ResourceWriteModel ToResourceWriteModel(Resource resource)
    {
        var health = resource.Health ?? new ResourceHealthStatus();

        return new ResourceWriteModel
        {
            Id = resource.Id.ToString("D"),
            CreatedAt = resource.CreatedAt.ToString("O"),
            PersistencePolicy = (int)resource.PersistencePolicy,
            OriginalFileName = resource.OriginalFileName,
            MimeType = resource.MimeType,
            FileSize = resource.FileSize,
            Source = (int)resource.Source,
            ThumbnailPath = resource.ThumbnailPath ?? string.Empty,
            Summary = resource.Summary ?? string.Empty,
            ConditionTags = NormalizeTags(resource.ConditionTags),
            TitleOverride = resource.TitleOverride ?? string.Empty,
            Annotations = resource.Annotations ?? string.Empty,
            PropertyTags = NormalizeTags(resource.PropertyTags),
            Privacy = (int)resource.Privacy,
            SyncPolicy = (int)resource.SyncPolicy,
            SyncTargetDevices = resource.SyncTargetDevices ?? Array.Empty<string>(),
            ProcessingModel = (int)resource.ProcessingModel,
            PermissionPresetId = resource.PermissionPresetId,
            State = (int)resource.State,
            WaitingExpiresAt = resource.WaitingExpiresAt?.ToString("O") ?? string.Empty,
            LastError = resource.LastError ?? string.Empty,
            FeatureHash = resource.FeatureHash ?? string.Empty,
            Health = IsEmptyHealth(health) ? null : health
        };
    }

    private static RawPayloadWriteModel ToRawPayloadWriteModel(Resource resource)
    {
        return new RawPayloadWriteModel
        {
            ResourceId = resource.Id.ToString("D"),
            RawKind = (int)resource.RawKind,
            SourceUri = resource.SourceUri ?? string.Empty,
            InternalPath = resource.InternalPath ?? string.Empty,
            SourceLastModifiedAt = resource.SourceLastModifiedAt?.ToString("O") ?? string.Empty,
            SourceAppHint = resource.SourceAppHint ?? string.Empty,
            CapturedAt = resource.CapturedAt?.ToString("O") ?? string.Empty,
            OriginalSuggestedName = resource.OriginalSuggestedName ?? string.Empty
        };
    }

    private static ProcessedPayloadWriteModel ToProcessedPayloadWriteModel(Resource resource)
    {
        return new ProcessedPayloadWriteModel
        {
            ResourceId = resource.Id.ToString("D"),
            RouteId = resource.ProcessedRouteId ?? string.Empty,
            ProcessedFilePath = resource.ProcessedFilePath ?? string.Empty,
            ProcessedText = resource.ProcessedText ?? string.Empty,
            UpdatedAt = DateTimeOffset.UtcNow.ToString("O")
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
        var conditionTags = string.Join(' ', NormalizeTags(resource.ConditionTags));
        var propertyTags = string.Join(' ', NormalizeTags(resource.PropertyTags));
        return (conditionTags + " " + propertyTags).Trim();
    }

    private static DynamicParameters BuildTagQueryParameters(
        string? query,
        int limit,
        int offset,
        IReadOnlyList<string>? tagFilters,
        bool applyConditionVisibility)
    {
        var normalizedFilters = NormalizeTags(tagFilters);
        var parameters = new DynamicParameters();
        parameters.Add("Query", query);
        parameters.Add("Limit", limit);
        parameters.Add("Offset", offset);
        parameters.Add("ApplyConditionVisibility", applyConditionVisibility ? 1 : 0);
        parameters.Add("TagFiltersJson", JsonSerializer.Serialize(normalizedFilters));
        return parameters;
    }

    private static string BuildTagVisibilityPredicate(string tableAlias = "")
    {
        var conditionColumn = tableAlias + "condition_tags_json";
        var propertyColumn = tableAlias + "property_tags_json";

        return $@"(
    @ApplyConditionVisibility = 0
    OR json_array_length(COALESCE({conditionColumn}, '[]')) = 0
    OR EXISTS (
        SELECT 1
        FROM json_each(COALESCE({conditionColumn}, '[]')) ct
        INNER JOIN json_each(@TagFiltersJson) tf
            ON lower(trim(ltrim(CAST(ct.value AS TEXT), '#'))) = lower(trim(ltrim(CAST(tf.value AS TEXT), '#')))
    )
)
AND (
    json_array_length(@TagFiltersJson) = 0
    OR NOT EXISTS (
        SELECT 1
        FROM json_each(@TagFiltersJson) tf
        WHERE NOT EXISTS (
            SELECT 1
            FROM json_each(COALESCE({conditionColumn}, '[]')) ct
            WHERE lower(trim(ltrim(CAST(ct.value AS TEXT), '#'))) = lower(trim(ltrim(CAST(tf.value AS TEXT), '#')))
        )
        AND NOT EXISTS (
            SELECT 1
            FROM json_each(COALESCE({propertyColumn}, '[]')) pt
            WHERE lower(trim(ltrim(CAST(pt.value AS TEXT), '#'))) = lower(trim(ltrim(CAST(tf.value AS TEXT), '#')))
        )
    )
)";
    }

    private static IReadOnlyList<string> NormalizeTags(IReadOnlyList<string>? tags)
    {
        if (tags is null || tags.Count == 0)
        {
            return Array.Empty<string>();
        }

        return tags
            .Where(static tag => !string.IsNullOrWhiteSpace(tag))
            .Select(static tag => tag.Trim().TrimStart('#'))
            .Where(static tag => !string.IsNullOrWhiteSpace(tag))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static tag => tag, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string? NullIfEmpty(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private sealed class ResourceWriteModel
    {
        public string Id { get; set; } = string.Empty;

        public string CreatedAt { get; set; } = string.Empty;

        public int PersistencePolicy { get; set; }

        public string OriginalFileName { get; set; } = string.Empty;

        public string MimeType { get; set; } = string.Empty;

        public long FileSize { get; set; }

        public int Source { get; set; }

        public string ThumbnailPath { get; set; } = string.Empty;

        public string Summary { get; set; } = string.Empty;

        public IReadOnlyList<string> ConditionTags { get; set; } = Array.Empty<string>();

        public string TitleOverride { get; set; } = string.Empty;

        public string Annotations { get; set; } = string.Empty;

        public IReadOnlyList<string> PropertyTags { get; set; } = Array.Empty<string>();

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
    }

    private sealed class RawPayloadWriteModel
    {
        public string ResourceId { get; set; } = string.Empty;

        public int RawKind { get; set; }

        public string SourceUri { get; set; } = string.Empty;

        public string InternalPath { get; set; } = string.Empty;

        public string SourceLastModifiedAt { get; set; } = string.Empty;

        public string SourceAppHint { get; set; } = string.Empty;

        public string CapturedAt { get; set; } = string.Empty;

        public string OriginalSuggestedName { get; set; } = string.Empty;
    }

    private sealed class ProcessedPayloadWriteModel
    {
        public string ResourceId { get; set; } = string.Empty;

        public string RouteId { get; set; } = string.Empty;

        public string ProcessedFilePath { get; set; } = string.Empty;

        public string ProcessedText { get; set; } = string.Empty;

        public string UpdatedAt { get; set; } = string.Empty;

        public bool HasAnyPayload =>
            !string.IsNullOrWhiteSpace(RouteId)
            || !string.IsNullOrWhiteSpace(ProcessedFilePath)
            || !string.IsNullOrWhiteSpace(ProcessedText);
    }

    private sealed class ResourceRow
    {
        public string Id { get; set; } = string.Empty;

        public string CreatedAt { get; set; } = string.Empty;

        public int? RawKind { get; set; }

        public string? SourceUri { get; set; }

        public string? InternalPath { get; set; }

        public int PersistencePolicy { get; set; }

        public string? SourceLastModifiedAt { get; set; }

        public string? SourceAppHint { get; set; }

        public string? CapturedAt { get; set; }

        public string? OriginalSuggestedName { get; set; }

        public string OriginalFileName { get; set; } = string.Empty;

        public string MimeType { get; set; } = string.Empty;

        public long FileSize { get; set; }

        public int Source { get; set; }

        public string? ProcessedFilePath { get; set; }

        public string? ProcessedText { get; set; }

        public string? ProcessedRouteId { get; set; }

        public string? ThumbnailPath { get; set; }

        public string? Summary { get; set; }

        public IReadOnlyList<string>? ConditionTags { get; set; }

        public string? TitleOverride { get; set; }

        public string? Annotations { get; set; }

        public IReadOnlyList<string>? PropertyTags { get; set; }

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
    }
}
