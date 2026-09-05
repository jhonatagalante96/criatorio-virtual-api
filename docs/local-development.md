# Local API execution

## Prerequisites

- .NET 10 SDK for running the API directly.
- Docker Desktop with Docker Compose v2 for the container workflow.

## Run directly

From the repository root:

```powershell
dotnet run --project src/CriatorioVirtual.Api
```

The API listens on `http://localhost:5000` or the port chosen by ASP.NET Core. Verify it with:

```powershell
Invoke-WebRequest http://localhost:5000/health
Invoke-WebRequest http://localhost:5000/health/ready
```

## Run in a container

```powershell
docker compose up --build
```

The API is exposed at `http://localhost:8080` by default. Set `API_PORT` only to change the host port; it is not copied into the image.

```powershell
Invoke-WebRequest http://localhost:8080/health
docker compose ps
```

Stop the stack with `docker compose down`.

## PostgreSQL and migrations

Persistence uses PostgreSQL when `ConnectionStrings__CriatorioVirtual` is configured. Supply the connection string through your shell, .NET User Secrets, or your secret store; do not commit credentials. A `.env` file is ignored by Git, but .NET does not load it automatically without an explicit configuration provider.

Apply migrations to a local database with:

```powershell
dotnet tool restore
$env:ConnectionStrings__CriatorioVirtual = "Host=localhost;Port=5432;Database=criatorio_virtual;Username=postgres;Password=<local-password>"
dotnet ef database update --project src/CriatorioVirtual.Infrastructure --startup-project src/CriatorioVirtual.Api
```

The integration suite validates migrations with an ephemeral PostgreSQL container. It intentionally does not use EF Core InMemory as a substitute for relational integrity:

```powershell
dotnet test tests/CriatorioVirtual.IntegrationTests
```

## Configuration and secrets

Runtime settings must be supplied as environment variables or secret stores, never committed to the repository or baked into an image. The `.dockerignore` excludes `.env` files and build artefacts from the image context.

When `ConnectionStrings__CriatorioVirtual` is supplied, the API validates its PostgreSQL format during startup and exits on malformed values. `/health` is the liveness endpoint; `/health/ready` checks only the API's internal readiness and does not wait for external providers. Each response includes `X-Correlation-ID`, which is also included in ProblemDetails responses and request log scopes.

## HTTP security

Identity users, roles, and Data Protection keys are stored in PostgreSQL when `ConnectionStrings__CriatorioVirtual` is configured. The same database and application name must be retained across deployments so authentication cookies remain valid. Protect the database with the deployment's encrypted storage, TLS, and least-privilege credentials.

Authentication uses an HttpOnly, Secure, SameSite=Lax cookie. Never place session tokens in browser storage. Configure each permitted browser origin explicitly; wildcard origins are rejected because the API allows credentials:

```powershell
$env:Security__AllowedOrigins__0 = "https://app.example.com"
$env:Security__TrustedProxyAddresses__0 = "10.0.0.10"
```

`Security__TrustedProxyAddresses` is optional and must list only the IP addresses of reverse proxies that are allowed to supply forwarded protocol and client-address headers. To obtain the request antiforgery token, call `GET /antiforgery/token` over HTTPS and send its `X-XSRF-TOKEN` response header on state-changing browser requests.
