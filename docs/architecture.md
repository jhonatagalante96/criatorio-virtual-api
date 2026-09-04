# Backend architecture

The backend is a .NET 10 modular monolith organized with Onion Architecture.

## Dependency direction

`Api -> Application <- Infrastructure` and `Application -> Domain`; Infrastructure also depends on Domain. Domain has no dependency on application frameworks or persistence libraries. The API is the composition root and the sole HTTP boundary.

## Conventions

- Application code generates `Guid` identifiers before entities are persisted; database-generated identifiers are not used.
- Calendar-only values use `DateOnly`; instants use `DateTimeOffset` normalized to UTC.
- Domain entities expose private setters and protect mutations through methods.
- Abstractions are introduced only when an existing consumer requires one.
- Application features are placed by use case under `Features/<UseCase>`.

## Project layout

- `src/CriatorioVirtual.Domain`: business rules and primitives.
- `src/CriatorioVirtual.Application`: use cases and application orchestration.
- `src/CriatorioVirtual.Infrastructure`: adapters and persistence implementations.
- `src/CriatorioVirtual.Api`: ASP.NET Core delivery and composition root.
- `tests/*`: focused domain, application, integration, and architecture suites.
