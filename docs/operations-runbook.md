# API operations runbook

## Health signals

- `GET /health` is liveness. It returns success while the API process is serving HTTP and does not call PostgreSQL or external providers.
- `GET /health/ready` checks that PostgreSQL accepts a connection when `ConnectionStrings__CriatorioVirtual` is configured. It returns `503` when the check fails. Local runs without PostgreSQL configured have no readiness dependency checks.
- Readiness deliberately excludes Asaas, S3, email, and Google. An outage at one of these providers must not stop the API deployment from becoming active or turn PostgreSQL readiness unhealthy.
- Railway's `healthcheckPath` is used to gate deployment activation. Railway does not keep polling that path after activation, so configure an ongoing HTTP monitor for `/health/ready` if database outages must page an operator.

The Railway configuration in `railway.json` uses `/health/ready` during deployment activation. A database outage keeps a new API deployment from replacing the previous deployment; an Asaas outage does not affect the check.

## Alert setup

Configure the following in the Railway project for each environment:

1. Add a project webhook in **Settings → Webhooks** and select an approved notification destination for deployment status changes and platform alerts. Send a test event and verify it reaches the intended responders.
2. In the Observability dashboard, add monitors for the resource metrics the operator wants to track, such as memory, CPU, disk, or network egress. Set thresholds with the service owner; keep alert destinations in Railway settings rather than repository files.
3. Configure an ongoing external HTTP monitor for `/health/ready` and notify the same operator destination when it stops returning `200`. The Railway deployment health check is not an ongoing monitor.

Do not commit webhook URLs, provider credentials, database connection strings, or notification addresses to this repository.

## Triage and recovery

### Deployment failed or API process crashed

1. Open the affected Railway deployment and inspect build and runtime logs around the failure time.
2. Check for startup configuration errors, especially missing production connection strings, Data Protection certificate settings, or invalid provider configuration.
3. If the failure began with a new API deployment, roll back to the last healthy deployment and confirm `/health` and `/health/ready` return `200`.
4. Correct the configuration or code issue, then deploy again and verify both endpoints.

### `/health/ready` returns `503`

1. Confirm the PostgreSQL service is running and reachable from the API's Railway environment.
2. Check that `ConnectionStrings__CriatorioVirtual` references the matching environment's database and that the credentials have not expired or been rotated without updating the API.
3. Check database provider logs and the API deployment logs. The readiness response intentionally omits connection details; use protected platform logs for diagnosis.
4. Restore database connectivity. If the failure began with an API release, roll back that API deployment while investigating.
5. Confirm `/health/ready` returns `200` before treating the incident as recovered.

### Billing requests fail while readiness is healthy

1. Keep the API running; Asaas is not part of readiness, so its outage does not make the API unready.
2. Check the configured Asaas environment, API key, webhook token, and Asaas service status using the protected Railway and Asaas dashboards.
3. Use API and provider logs to identify the failed operation. Confirm the persisted payment or subscription state before retrying a charge so an ambiguous response does not create a duplicate payment.
4. After Asaas recovers, verify the affected subscription or payment state through the API before closing the incident.

### Private storage requests fail

Use the [private storage operations runbook](private-storage-operations.md) to inspect pending attachment cleanup and recover storage access. Do not make private objects public or remove database metadata by hand.
