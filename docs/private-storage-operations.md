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

## Railway Bucket configuration

On Railway, set `Storage__Provider=S3` for both Development and Production. Create one private Bucket instance per Railway environment and map that instance's `ENDPOINT`, `BUCKET`, `REGION`, `ACCESS_KEY_ID`, and `SECRET_ACCESS_KEY` variables to:

| API setting | Bucket variable |
| --- | --- |
| `Storage__S3__Endpoint` | `ENDPOINT` |
| `Storage__S3__Bucket` | `BUCKET` |
| `Storage__S3__Region` | `REGION` |
| `Storage__S3__AccessKeyId` | `ACCESS_KEY_ID` |
| `Storage__S3__SecretAccessKey` | `SECRET_ACCESS_KEY` |

Use Railway Variable References for these values. Development and Production must reference their own Bucket instance and credentials. Current Railway Buckets use virtual-hosted URL style, so `Storage__S3__ForcePathStyle=false`; set it to `true` only when the Bucket Credentials tab specifies path-style URLs. The API never makes objects public or assigns a public ACL. It continues serving file content through authenticated endpoints.

The S3 provider writes new objects to the Bucket. While `Storage__LegacyPrivateRootPath` points at the attached Volume, reads fall back to that Volume only when the object is missing from the Bucket. This keeps old files accessible during migration. Deletion requests remove the Bucket object and leave the source Volume untouched for rollback.

## Migrate the attached Volume

Keep the Volume mounted and configure `Storage__LegacyPrivateRootPath` to its private-storage directory. Configure `Storage__Provider=S3`, the Bucket settings above, and `ConnectionStrings__CriatorioVirtual` for the environment being migrated. The migration reads MIME types from attachment, document, and visual-identity metadata in PostgreSQL; unreferenced files use `application/octet-stream` in object metadata.

Run the migration from the repository root:

```powershell
dotnet run --project tools/PrivateStorageMigration --configuration Release
```

The API image also contains the published tool at `/app/tools/PrivateStorageMigration/PrivateStorageMigration.dll`; when running from a deployed container with its Volume mounted, invoke it with `dotnet /app/tools/PrivateStorageMigration/PrivateStorageMigration.dll`.

The tool enumerates tenant-namespaced files, preserves tenant IDs and object keys, uploads each file, then verifies its byte length and SHA-256. It does not delete or modify Volume files. Re-running it skips objects whose bytes already match and repairs missing or mismatched Bucket copies, so an interrupted or partially failed run can safely resume. A failure leaves the Volume source available.

After a successful run, keep the Volume and `Storage__LegacyPrivateRootPath` configured while validating representative authenticated reads and the expected object counts. Remove the fallback setting and detach the Volume only after the cutover is confirmed. Do not delete the Volume as part of migration.
