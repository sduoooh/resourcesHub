DROP TABLE IF EXISTS fts_resources;
DROP TABLE IF EXISTS resource_processed_payloads;
DROP TABLE IF EXISTS resource_raw_payloads;
DROP TABLE IF EXISTS resources;

CREATE TABLE resources (
    id TEXT NOT NULL PRIMARY KEY,
    created_at TEXT NOT NULL,
    persistence_policy INTEGER NOT NULL DEFAULT 0,
    original_file_name TEXT NOT NULL,
    mime_type TEXT NOT NULL,
    file_size INTEGER NOT NULL,
    source INTEGER NOT NULL,
    thumbnail_path TEXT NULL,
    summary TEXT NULL,
    condition_tags_json TEXT NOT NULL,
    title_override TEXT NULL,
    annotations TEXT NULL,
    property_tags_json TEXT NOT NULL,
    privacy INTEGER NOT NULL,
    sync_policy INTEGER NOT NULL,
    sync_target_devices_json TEXT NOT NULL,
    processing_model INTEGER NOT NULL,
    permission_preset_id TEXT NOT NULL,
    state INTEGER NOT NULL,
    waiting_expires_at TEXT NULL,
    last_error TEXT NULL,
    feature_hash TEXT NULL,
    health_json TEXT NULL
);

CREATE TABLE resource_raw_payloads (
    resource_id TEXT NOT NULL PRIMARY KEY,
    raw_kind INTEGER NOT NULL,
    source_uri TEXT NULL,
    internal_path TEXT NULL,
    source_last_modified_at TEXT NULL,
    source_app_hint TEXT NULL,
    captured_at TEXT NULL,
    original_suggested_name TEXT NULL,
    FOREIGN KEY(resource_id) REFERENCES resources(id) ON DELETE CASCADE
);

CREATE TABLE resource_processed_payloads (
    resource_id TEXT NOT NULL PRIMARY KEY,
    route_id TEXT NULL,
    processed_file_path TEXT NULL,
    processed_text TEXT NULL,
    updated_at TEXT NOT NULL,
    FOREIGN KEY(resource_id) REFERENCES resources(id) ON DELETE CASCADE
);

CREATE VIRTUAL TABLE fts_resources USING fts5(
    resource_id UNINDEXED,
    title,
    notes,
    processed_text,
    summary,
    tags
);

CREATE INDEX IF NOT EXISTS idx_resources_feature_hash ON resources(feature_hash);
