SELECT
    r.id AS Id,
    r.created_at AS CreatedAt,
    r.raw_kind AS RawKind,
    r.source_uri AS SourceUri,
    r.internal_path AS InternalPath,
    r.persistence_policy AS PersistencePolicy,
    r.source_last_modified_at AS SourceLastModifiedAt,
    r.source_app_hint AS SourceAppHint,
    r.captured_at AS CapturedAt,
    r.original_suggested_name AS OriginalSuggestedName,
    r.original_file_name AS OriginalFileName,
    r.mime_type AS MimeType,
    r.file_size AS FileSize,
    r.source AS Source,
    r.processed_file_path AS ProcessedFilePath,
    r.processed_text AS ProcessedText,
    r.processed_route_id AS ProcessedRouteId,
    r.thumbnail_path AS ThumbnailPath,
    r.summary AS Summary,
    r.condition_tags_json AS ConditionTags,
    r.title_override AS TitleOverride,
    r.annotations AS Annotations,
    r.property_tags_json AS PropertyTags,
    r.privacy AS Privacy,
    r.sync_policy AS SyncPolicy,
    r.sync_target_devices_json AS SyncTargetDevices,
    r.processing_model AS ProcessingModel,
    r.permission_preset_id AS PermissionPresetId,
    r.state AS State,
    r.waiting_expires_at AS WaitingExpiresAt,
    r.last_error AS LastError,
    r.feature_hash AS FeatureHash,
    r.health_json AS Health
FROM fts_resources f
INNER JOIN resources r ON r.id = f.resource_id
WHERE fts_resources MATCH @Query
/*TAG_FILTERS*/
ORDER BY r.created_at DESC
LIMIT @Limit OFFSET @Offset;