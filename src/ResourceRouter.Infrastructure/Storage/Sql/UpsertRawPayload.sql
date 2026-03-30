INSERT INTO resource_raw_payloads (
    resource_id,
    raw_kind,
    source_uri,
    internal_path,
    source_last_modified_at,
    source_app_hint,
    captured_at,
    original_suggested_name
)
VALUES (
    @ResourceId,
    @RawKind,
    @SourceUri,
    @InternalPath,
    @SourceLastModifiedAt,
    @SourceAppHint,
    @CapturedAt,
    @OriginalSuggestedName
)
ON CONFLICT(resource_id) DO UPDATE SET
    raw_kind = excluded.raw_kind,
    source_uri = excluded.source_uri,
    internal_path = excluded.internal_path,
    source_last_modified_at = excluded.source_last_modified_at,
    source_app_hint = excluded.source_app_hint,
    captured_at = excluded.captured_at,
    original_suggested_name = excluded.original_suggested_name;
