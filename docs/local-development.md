# Local API execution

## Prerequisites

- .NET 10 SDK for running the API directly.
- Docker Desktop with Docker Compose v2 for the container workflow.
- Chromium for HTML document rendering when running the API directly.

## Run directly

From the repository root:

```powershell
dotnet run --project src/CriatorioVirtual.Api
```

Install the Playwright browser once after the first build when running outside Docker:

```powershell
pwsh src/CriatorioVirtual.Api/bin/Debug/net10.0/playwright.ps1 install chromium
```

## HTML document rendering

Badges, genealogy certificates, and provenance documents are converted from embedded HTML/CSS templates through Playwright .NET and a Chromium-compatible executable. The renderer uses one reusable Chromium process with isolated Playwright contexts per document, inlines authorized snapshot values and embedded assets, disables PDF headers and footers, and enforces a 30-second rendering timeout. A bounded concurrency gate prevents the number of browser renders from growing with the number of API requests; the default allows two simultaneous renders per API instance.

For local runs, Google Chrome and Microsoft Edge are detected automatically. Configure an explicit executable when needed:

```powershell
$env:DocumentRendering__ChromiumPath = "C:\Program Files\Google\Chrome\Application\chrome.exe"
```

Tune the resource budget deliberately, up to four simultaneous renders:

```powershell
$env:DocumentRendering__MaxConcurrentRenders = "2"
$env:DocumentRendering__RenderTimeoutSeconds = "30"
```

Docker installs Google Chrome Stable in the image and uses `/usr/bin/google-chrome-stable`. The Ubuntu 24.04 base image does not provide a standalone Chromium binary through APT.

The API listens on `http://localhost:5000` or the port chosen by ASP.NET Core. Verify it with:

```powershell
Invoke-WebRequest http://localhost:5000/health
Invoke-WebRequest http://localhost:5000/health/ready
```

In Development and Testing, Swagger UI is available at `http://localhost:5000/swagger` and the OpenAPI document at `http://localhost:5000/swagger/v1/swagger.json`. Swagger is not registered in other environments.

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

Local Development and Testing use a private filesystem directory by default. The API never exposes that directory as public static files or returns it in storage metadata. To select a specific local directory:

```powershell
$env:Storage__Provider = "FileSystem"
$env:Storage__PrivateRootPath = "C:\CriatorioVirtual\private-storage"
```

Deployments outside Development and Testing require `Storage__Provider=S3` and valid private bucket credentials. See [private storage operations](private-storage-operations.md) for Railway configuration and the safe Volume migration procedure.

Species default images are provisioned from the embedded catalog into
`Storage__SpeciesDefaultImagesRootPath`. In Development and Testing the API
uses an isolated temporary directory by default. In Railway, attach a
persistent volume (for example mounted at `/data`) and configure the image
directory as a subdirectory of that mount:

```powershell
$env:Storage__SpeciesDefaultImagesRootPath = "/data/species-default-images"
```

At startup, missing catalog files are copied to the configured directory and
existing files are preserved. The public read-only endpoint is
`/species-images/{fileName}`; the API only serves the 60 catalog file names.
Bird responses use the species image until a primary bird photo is selected.

When `ConnectionStrings__CriatorioVirtual` is supplied, the API validates its PostgreSQL format during startup and exits on malformed values. `/health` is the liveness endpoint. `/health/ready` checks PostgreSQL connectivity when persistence is configured; it does not wait for Asaas or other external providers. Without PostgreSQL configured (the default local setup), readiness has no dependency checks. Each response includes `X-Correlation-ID`, which is also included in ProblemDetails responses and request log scopes. See the [operations runbook](operations-runbook.md) for monitoring and recovery steps.

## HTML document rendering

Genealogy certificates and provenance documents are converted from the embedded HTML/CSS templates through Playwright .NET and a Chromium-compatible executable. The API container installs Google Chrome Stable at `/usr/bin/google-chrome-stable`. For local runs, Google Chrome and Microsoft Edge are detected automatically; configure an explicit executable when needed:

```powershell
$env:DocumentRendering__ChromiumPath = "C:\Program Files\Google\Chrome\Application\chrome.exe"
```

The renderer uses one reusable Chromium process with isolated Playwright contexts per document. A bounded concurrency gate queues requests above the configured concurrency, inlines the authorized snapshot values and embedded assets, disables PDF headers and footers, and enforces a 30-second rendering timeout. The defaults allow two simultaneous HTML renders per API instance:

```powershell
$env:DocumentRendering__MaxConcurrentRenders = "2"
$env:DocumentRendering__RenderTimeoutSeconds = "30"
```

Keep `MaxConcurrentRenders` low in small containers. Increasing it improves throughput but also increases the CPU and memory budget reserved for document rendering.

## HTTP security

Identity users, roles, and Data Protection keys are stored in PostgreSQL when `ConnectionStrings__CriatorioVirtual` is configured. The same database, application name, and key-encryption certificate must be retained across deployments so authentication cookies remain valid. The persisted key ring is encrypted with a password-protected PKCS#12 certificate supplied through the deployment's secret store. Do not commit either value:

```powershell
$env:Security__DataProtection__CertificateBase64 = "<base64-pkcs12>"
$env:Security__DataProtection__CertificatePassword = "<certificate-password>"
```

The certificate must currently be valid and contain its private key. PostgreSQL persistence will not start without it. Outside `Development` and `Testing`, the API also refuses to start without `ConnectionStrings__CriatorioVirtual`; an in-memory key ring is restricted to those two local/test environments.

Authentication uses an HttpOnly, Secure, SameSite=Lax cookie. Never place session tokens in browser storage. Configure each permitted browser origin explicitly; wildcard origins are rejected because the API allows credentials:

```powershell
$env:Security__AllowedOrigins__0 = "https://app.example.com"
$env:Security__TrustedProxyAddresses__0 = "10.0.0.10"
$env:Security__Google__ClientId = "your-google-client-id"
$env:Security__Google__ClientSecret = "use-a-secret-store"
$env:Security__Google__ClientBaseUrl = "https://app.example.com"
$env:Security__Google__SuccessPath = "/login"
$env:Security__Google__FailurePath = "/login"
```

Passkey RP configuration must also be explicit outside local localhost environments. `Security__Passkeys__ServerDomain` is the WebAuthn RP ID and must be the configured domain or a parent domain of every passkey browser origin. `Security__AllowedOrigins` controls CORS and may include loopback origins for a local frontend that points to a deployed API. When `Security__Passkeys__AllowedOrigins` is omitted, non-loopback allowed origins are used for passkeys and loopback origins remain CORS-only. Configure it explicitly when the passkey origins should be a narrower subset:

```powershell
$env:Security__Passkeys__ServerDomain = "app.example.com"
$env:Security__Passkeys__AllowedOrigins__0 = "https://app.example.com"
```

The API validates the RP ID and origins at startup, requires user verification and discoverable credentials, uses a finite two-minute authenticator timeout, and requests `none` attestation. ASP.NET Core Identity stores only passkey public material and WebAuthn metadata in PostgreSQL; biometric data never reaches the API.

Authentication email delivery uses the in-memory provider by default in Development and Testing. Messages are kept only in the process inbox exposed through `IAuthenticationEmailInbox`; no email token is logged or returned by an HTTP endpoint. The default client base URL is `http://localhost:3000`, and the in-memory provider rejects non-loopback URLs so local tests cannot generate production links. Non-local environments must configure `Security__Email__Provider=Resend`, an HTTPS `Security__Email__ClientBaseUrl`, `Security__Email__ResendApiKey`, and a verified `Security__Email__SenderAddress`. Create the Resend key with sending-only permission when possible and keep it in the deployment secret store. Provider failures return a retryable result without exposing the token.

Asaas billing operations use `Billing__Asaas__ApiKey` and `Billing__Asaas__WebhookToken` from a secret store. Configure the webhook token in the Asaas webhook and keep it separate from the API key; it must contain 32–255 non-whitespace characters. The public `POST /api/webhooks/asaas` endpoint validates it from the `asaas-access-token` header, then durably stores valid event envelopes for later processing. Development and Testing may omit the webhook token, in which case the endpoint returns `503`; Homologation and Production require it. Development, Testing, and the Railway HML environment (`ASPNETCORE_ENVIRONMENT=Homologation`) are pinned to `https://api-sandbox.asaas.com/v3/`; they reject a production API URL and never send billing requests to the live host. Homologation requires a Sandbox API key; a Sandbox key is optional for starting the API in Development/Testing but required to execute real Sandbox gateway calls. Production uses `https://api.asaas.com/v3/` and requires the live API key. Leave `Billing__Asaas__BaseUrl` unset so the API selects the pinned URL for each environment. Never commit either key or webhook token. Subscription creation uses a stable local subscription identifier as Asaas `externalReference`, reconciles ambiguous outcomes through Asaas before retrying, and uses a 60-second request timeout; an unknown outcome must be reconciled rather than blindly resubmitted.

`Security__TrustedProxyAddresses` is optional and must list only the IP addresses of reverse proxies that are allowed to supply forwarded protocol and client-address headers. Forwarded headers are disabled when this list is empty. On Railway, set `Security__AssumeHttpsBehindProxy=true` because Railway terminates TLS at its edge and requires HTTPS for public inbound traffic. This setting derives the HTTPS scheme from trusted deployment configuration instead of accepting a caller-supplied forwarded protocol header; enable it only on platforms that guarantee HTTPS before proxying to the API. To obtain the request antiforgery token, call `GET /antiforgery/token` over HTTPS and send its exposed `X-XSRF-TOKEN` response header on state-changing browser requests. POST, PUT, PATCH, and DELETE requests require a valid antiforgery token by default; an endpoint must carry explicit opt-out metadata to bypass validation. A successful `POST /api/auth/login` changes the authenticated identity, so clients must discard any token obtained before login and call `GET /antiforgery/token` again before `POST /api/auth/logout` or another authenticated state-changing request.

Account registration is exposed at `POST /api/auth/register`; a successful registration requires PostgreSQL persistence to be configured. Send a JSON body with `email`, `password`, and `confirmPassword`, together with the antiforgery header. The API validates that the password confirmation matches before creating the identity. A successful request returns `201 Created` with the generated user id and normalized email; the identity remains unconfirmed until the e-mail confirmation flow is delivered. Passwords are validated by ASP.NET Core Identity, are never returned, and are not written to logs. Existing e-mail addresses return `409 Conflict` without creating another identity.

Email confirmation is completed with `POST /api/auth/confirm-email` using the `userId` and `token` values from the delivered action URL. A valid or repeated confirmation returns `204 No Content`; adulterated, unknown, or expired tokens return a generic `400 Bad Request` without echoing the token. Request a new message with `POST /api/auth/confirm-email/resend` and an `email` body. Unknown or already confirmed addresses return the same `204 No Content` as accepted requests, while repeated requests for the same address are limited by the configured five-minute window and return `429 Too Many Requests`.

Password recovery uses `POST /api/auth/forgot-password` with an `email` body. Valid and unknown addresses return the same `204 No Content`; a delivered reset message contains the `userId` and `token` values for `POST /api/auth/reset-password`, which accepts `newPassword` and `confirmPassword`. A reset token is invalid after a successful reset, and invalid, expired, adulterated, or reused tokens return a generic `400 Bad Request`. Authenticated local accounts can change their password with `POST /api/auth/change-password` using `currentPassword`, `newPassword`, and `confirmPassword`.

Account sessions use `POST /api/auth/login`, `GET /api/auth/session`, and `POST /api/auth/logout`. Login returns `204 No Content` and stores the protected HttpOnly application cookie; invalid credentials return the same generic `401 Unauthorized` problem regardless of whether the e-mail exists. Session and logout require the authenticated cookie, and logout also requires the antiforgery header. API authorization failures return `401` or `403` instead of redirects so the client keeps the authentication state available for the response.

Google authentication is enabled only when `Security__Google__ClientId` and `Security__Google__ClientSecret` are configured together. Register `https://<api-host>/signin-google` as the Google OAuth callback URI. `Security__Google__ClientBaseUrl` must match one of the configured `Security__AllowedOrigins` values; when omitted, the first allowed origin is used. `Security__Google__SuccessPath` defaults to `/login` and is used only as the fallback when the callback is opened without an opener; `Security__Google__FailurePath` defaults to `/login`. Both must be safe relative paths. Start the flow with a top-level browser navigation to `GET /api/auth/google`, not an AJAX request. The API validates the protected OAuth state, creates a new account only for a verified Google e-mail, and never links a Google identity to an existing local account based only on a matching e-mail. After the technical `/signin-google` callback, successful authentication returns a no-store callback document that notifies the original window and closes the popup without loading a frontend page. Failed or adulterated callbacks redirect to the configured frontend failure path with a safe `googleError` code and `correlationId`; the server logs the structured failure reason without logging tokens, provider keys, or e-mail addresses. The frontend should call `GET /api/auth/session` after the success callback to load the authenticated session in the original window.
