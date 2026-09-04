# Codex instructions — Criatório Virtual API

## Scope

Implement only the requested backlog card. Do not add future features, integrations, audit logging, or infrastructure that the card does not require.

## Architecture

V0-002 will establish the .NET Onion Architecture solution. Keep domain rules independent of framework and infrastructure concerns. Preserve explicit tenant boundaries in every data access and authorization path.

## Quality

- Add automated tests for business rules and authorization.
- Test concurrency and idempotency when the feature has either concern.
- Use PostgreSQL migrations and database constraints for persisted invariants.
- Include negative cross-tenant tests whenever data access changes.
- Keep APIs explicit, validated, and documented through code as implemented.

## Workflow

Read `README.md` and `CONTRIBUTING.md` before changing code. Work from `develop` in a focused branch. Use English Conventional Commits and keep pull requests limited to one backlog card or subtask.
