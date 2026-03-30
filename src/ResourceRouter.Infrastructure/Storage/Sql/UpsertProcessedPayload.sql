INSERT INTO resource_processed_payloads (
    resource_id,
    route_id,
    processed_file_path,
    processed_text,
    updated_at
)
VALUES (
    @ResourceId,
    @RouteId,
    @ProcessedFilePath,
    @ProcessedText,
    @UpdatedAt
)
ON CONFLICT(resource_id) DO UPDATE SET
    route_id = excluded.route_id,
    processed_file_path = excluded.processed_file_path,
    processed_text = excluded.processed_text,
    updated_at = excluded.updated_at;
