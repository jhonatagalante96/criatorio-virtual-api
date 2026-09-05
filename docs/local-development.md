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

## Configuration and secrets

No database or external integration is part of this foundation card. Future runtime settings must be supplied as environment variables or secret stores, never committed to the repository or baked into an image. The `.dockerignore` excludes `.env` files and build artefacts from the image context.
