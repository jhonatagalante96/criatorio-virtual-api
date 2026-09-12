# Private attachment storage operations

Attachment removal is logical first. Once the database transaction commits, the attachment is excluded from list and download queries even when the private object still exists temporarily.

## Retry a pending cleanup

If \`DELETE /api/birds/{birdId}/attachments/{attachmentId}\` returns \`503 Service Unavailable\`, the attachment is already hidden and its metadata is retained with \`StorageCleanupPending = true\`. Retry the same authenticated request with explicit confirmation:

    DELETE /api/birds/{birdId}/attachments/{attachmentId}
    Content-Type: application/json

    {"confirmed":true}

The cleanup operation is idempotent. A missing object is treated as already cleaned, so a retry can safely complete the metadata transition.

## Administrative inspection

Use a read-only database query to identify cleanup records that need attention:

    SELECT "Id", "BreedingFarmId", "BirdId", "ObjectKey", "DeletedAtUtc", "UpdatedAtUtc"
    FROM app.bird_attachments
    WHERE "DeletedAtUtc" IS NOT NULL
      AND "StorageCleanupPending" = TRUE
    ORDER BY "UpdatedAtUtc";

Retry each record through the authenticated API while preserving its tenant and bird identifiers. Do not delete database rows manually: retaining the metadata preserves the object key needed for a safe retry and supports investigation of an orphaned object.
