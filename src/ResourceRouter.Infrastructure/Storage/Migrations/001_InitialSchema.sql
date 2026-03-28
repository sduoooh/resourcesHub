CREATE TABLE IF NOT EXISTS resources (
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

CREATE VIRTUAL TABLE IF NOT EXISTS fts_resources USING fts5(
    resource_id UNINDEXED,
    title,
    notes,
    processed_text,
    ai_summary,
    tags
);

CREATE INDEX IF NOT EXISTS idx_resources_feature_hash ON resources(feature_hash);
