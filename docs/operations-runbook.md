# API operations runbook

## Health signals

- `GET /health` is liveness. It returns success while the API process is serving HTTP and does not call PostgreSQL or external providers.
- `GET /health/ready` checks that PostgreSQL accepts a connection when `ConnectionStrings__CriatorioVirtual` is configured. It returns `503` when the check fails. Local runs without PostgreSQL configured have no readiness dependency checks.
- Readiness deliberately excludes Asaas, S3, email, and Google. An outage at one of these providers must not stop the API deployment from becoming active or turn PostgreSQL readiness unhealthy.
- Railway's `healthcheckPath` is used to gate deployment activation. Railway does not keep polling that path after activation, so configure an ongoing HTTP monitor for `/health/ready` if database outages must page an operator.

The Railway configuration in `railway.json` uses `/health/ready` during deployment activation. A database outage keeps a new API deployment from replacing the previous deployment; an Asaas outage does not affect the check.

## Alert setup

Configure the following for the Railway project:

1. Add a project webhook in **Settings → Webhooks** for deployment status changes and volume usage alerts. Webhooks are project-wide and include every environment; route through a receiver that filters `resource.environment.name` when notifications must be limited to Homologation. Send a test event and verify it reaches the intended destination.
2. Railway resource monitors for CPU, RAM, disk, and network thresholds require Pro. They are deferred on the current plan. Resource graphs remain available for manual inspection, but they do not send threshold alerts.
3. An ongoing external HTTP monitor for `/health/ready` is optional for Homologation and does not require Railway Pro. Railway only calls the health check during deployment activation; without an external monitor, no one is paged for an outage after activation.
4. API and background-job failures are recorded in application logs. Railway does not turn those log entries into alerts by itself; use an external log alert provider if immediate notifications are required.

Do not commit webhook URLs, provider credentials, database connection strings, or notification addresses to this repository.

## Triage and recovery

### Deployment failed or API process crashed

1. Open the affected Railway deployment and inspect build and runtime logs around the failure time.
2. Check for startup configuration errors, especially missing production connection strings, Data Protection certificate settings, or invalid provider configuration.
3. If the failure began with a new API deployment, roll back to the last healthy deployment and confirm `/health` and `/health/ready` return `200`.
4. Correct the configuration or code issue, then deploy again and verify both endpoints.

### Credential exposure or rotation

1. Revoke or rotate the exposed credential at its issuing provider first. Never copy the value into a ticket, chat, shell command, or runbook.
2. Update the matching variable or Railway Variable Reference in the **Homologation** environment and the API service. For database credentials, update the PostgreSQL service and `ConnectionStrings__CriatorioVirtual` together.
3. For `Billing__Asaas__WebhookToken`, update the Homologation API variable and the matching Asaas webhook configuration in a coordinated change; mismatched values cause webhook requests to be rejected.
4. Redeploy the affected Homologation service, verify `/health/ready`, then make one safe provider operation and check runtime logs for authentication failures. Keep Production untouched during this Homologation procedure.

### `/health/ready` returns `503`

1. Confirm the PostgreSQL service is running and reachable from the API's Railway environment.
2. Check that `ConnectionStrings__CriatorioVirtual` references the matching environment's database and that the credentials have not expired or been rotated without updating the API.
3. Check database provider logs and the API deployment logs. The readiness response intentionally omits connection details; use protected platform logs for diagnosis.
4. Restore database connectivity. If the failure began with an API release, roll back that API deployment while investigating.
5. Confirm `/health/ready` returns `200` before treating the incident as recovered.

### Database migration recovery without Railway backups

Railway's Backups/PITR controls are Pro-only in the current account. Do not assume the Homologation database has a Railway restore point. Rolling back an API deployment restores the application image and configuration; it does not undo an EF Core migration or restore changed rows.

Before deploying a release that applies a database migration:

1. Open an authorized connection or Railway CLI tunnel to the Homologation database. Keep the connection string out of command history, logs, and this repository.
2. Open a local tunnel explicitly targeting the Homologation environment:

       railway connect postgres --tunnel-only --environment homologation

3. Use the host/port/user/database shown by the tunnel. Create a custom-format logical dump outside Railway, on storage with restricted access. Let `pg_dump` prompt for the password; do not put it in the command or a connection URL:

       pg_dump --host 127.0.0.1 --port <tunnel-port> --username <database-user> --dbname <database-name> --format=custom --no-owner --file=pre-migration.dump

4. Verify the dump can be read before deployment:

       pg_restore --list pre-migration.dump

If a deployment fails before the migration completes, roll back the API deployment and verify health. If the migration completed and changed data incorrectly, do not assume an application rollback repairs the database. Restore the dump into an isolated database, verify it, then plan a controlled cutover; otherwise prepare a forward-fix migration. Do not run a migration `Down()` manually or edit `__EFMigrationsHistory` to force a rollback.

### Asaas webhook retries

The API stores accepted Asaas events before processing. Failed processing is retried automatically with exponential backoff starting at 5 seconds and capped at 1 hour. The retry worker logs the `EventRecordId`; use that identifier to correlate the failure without opening or copying the stored payload.

List pending records with a read-only query that omits the payload:

    SELECT "Id", "EventType", "ReceivedAtUtc", "ProcessingAttempts", "NextAttemptAtUtc"
    FROM app.asaas_webhook_events
    WHERE "ProcessedAtUtc" IS NULL
    ORDER BY "NextAttemptAtUtc", "ReceivedAtUtc";

Fix the underlying provider, database, or application problem and let the worker retry. Do not mark an event processed or change its retry timestamp directly in SQL. The event log contains no webhook token or raw payload.

### Billing requests fail while readiness is healthy

1. Keep the API running; Asaas is not part of readiness, so its outage does not make the API unready.
2. Check the configured Asaas environment, API key, webhook token, and Asaas service status using the protected Railway and Asaas dashboards.
3. Use API and provider logs to identify the failed operation. Confirm the persisted payment or subscription state before retrying a charge so an ambiguous response does not create a duplicate payment.
4. After Asaas recovers, verify the affected subscription or payment state through the API before closing the incident.

### Private storage requests fail

Use the [private storage operations runbook](private-storage-operations.md) to inspect pending attachment cleanup and recover storage access. Do not make private objects public or remove database metadata by hand.

## Operational event log checks

Search Railway runtime logs for the structured `EventName` values below. Request events include `CorrelationId`; use it to connect an application event to the corresponding API problem log and response header `X-Correlation-ID`.

- `LoginFailed` and `AccountLocked`: failed sign-in outcomes. Neither the email address nor password is logged.
- `InvalidTenantAccess`: the selected breeding farm could not be authorized. Cross-tenant resource lookups intentionally return the same `404` as a missing resource; those are visible in the generic problem log by method, path, status, and correlation ID, without exposing whether another tenant owns the identifier.
- `TransferRejected`: successful rejection of an internal transfer.
- `WebhookDuplicate`: Asaas redelivery for an event already processed. The log records only the internal event record ID.
- `PaymentMismatch`: Asaas payment data did not match the persisted payment. The log records the payment ID, not card or provider credentials.

API requests returned as `ProblemDetails` appear in the generic `API route returned a problem` log with method, path, status, title, and correlation ID. Unhandled request failures use the `HTTP request failed` log. Background Asaas processing failures are logged with `EventRecordId`. Do not include secrets, payment tokens, or raw provider payloads when sharing log evidence.
