ALTER TABLE resources RENAME TO resources_legacy;

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
    health_json TEXT NULL,
    last_health_check_at TEXT NULL,
    last_health_check_passed INTEGER NULL,
    last_health_check_message TEXT NULL
);

INSERT INTO resources (
    id,
    created_at,
    source_uri,
    internal_path,
    persistence_policy,
    source_last_modified_at,
    original_file_name,
    mime_type,
    file_size,
    source,
    processed_file_path,
    processed_text,
    processed_route_id,
    thumbnail_path,
    ai_summary,
    auto_tags_json,
    user_title,
    user_notes,
    user_tags_json,
    privacy,
    sync_policy,
    sync_target_devices_json,
    processing_model,
    permission_preset_id,
    state,
    waiting_expires_at,
    last_error,
    feature_hash,
    health_json,
    last_health_check_at,
    last_health_check_passed,
    last_health_check_message
)
SELECT
    id,
    created_at,
    source_uri,
    internal_path,
    persistence_policy,
    source_last_modified_at,
    original_file_name,
    mime_type,
    file_size,
    source,
    processed_file_path,
    processed_text,
    processed_route_id,
    thumbnail_path,
    ai_summary,
    auto_tags_json,
    user_title,
    user_notes,
    user_tags_json,
    privacy,
    sync_policy,
    sync_target_devices_json,
    processing_model,
    permission_preset_id,
    state,
    waiting_expires_at,
    last_error,
    feature_hash,
    CASE
        WHEN COALESCE(NULLIF(last_health_check_at, ''), NULLIF(last_health_check_message, '')) IS NULL
             AND last_health_check_passed IS NULL
        THEN NULL
        ELSE json_object(
            'LastCheckAt', NULLIF(last_health_check_at, ''),
            'LastCheckPassed', CASE
                WHEN last_health_check_passed IS NULL THEN NULL
                WHEN last_health_check_passed = 0 THEN json('false')
                ELSE json('true')
            END,
            'LastCheckMessage', NULLIF(last_health_check_message, '')
        )
    END,
    last_health_check_at,
    last_health_check_passed,
    last_health_check_message
FROM resources_legacy;

DROP TABLE resources_legacy;

CREATE INDEX IF NOT EXISTS idx_resources_feature_hash ON resources(feature_hash);
