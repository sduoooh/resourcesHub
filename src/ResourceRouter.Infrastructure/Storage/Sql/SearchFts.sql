SELECT
    r.id AS Id,
    r.created_at AS CreatedAt,
    r.source_uri AS SourceUri,
    r.internal_path AS InternalPath,
    r.persistence_policy AS PersistencePolicy,
    r.source_last_modified_at AS SourceLastModifiedAt,
    r.original_file_name AS OriginalFileName,
    r.mime_type AS MimeType,
    r.file_size AS FileSize,
    r.source AS Source,
    r.processed_file_path AS ProcessedFilePath,
    r.processed_text AS ProcessedText,
    r.processed_route_id AS ProcessedRouteId,
    r.thumbnail_path AS ThumbnailPath,
    r.ai_summary AS Summary,
    r.auto_tags_json AS AutoTags,
    r.user_title AS UserTitle,
    r.user_notes AS UserNotes,
    r.user_tags_json AS UserTags,
    r.privacy AS Privacy,
    r.sync_policy AS SyncPolicy,
    r.sync_target_devices_json AS SyncTargetDevices,
    r.processing_model AS ProcessingModel,
    r.permission_preset_id AS PermissionPresetId,
    r.state AS State,
    r.waiting_expires_at AS WaitingExpiresAt,
    r.last_error AS LastError,
    r.feature_hash AS FeatureHash,
    r.health_json AS Health,
    r.last_health_check_at AS LastHealthCheckAt,
    r.last_health_check_passed AS LastHealthCheckPassed,
    r.last_health_check_message AS LastHealthCheckMessage
FROM fts_resources f
INNER JOIN resources r ON r.id = f.resource_id
WHERE fts_resources MATCH @Query
ORDER BY r.created_at DESC
LIMIT @Limit OFFSET @Offset;