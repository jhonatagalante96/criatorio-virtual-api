# Criatório Virtual API — Codex

## Scope
- Work only on the current backlog card or PR. Do not add unrelated cleanup, future features, integrations, or infrastructure.
- Prefer the current code and the card's acceptance criteria over assumptions.
- Stop only for a material unresolved product decision, missing required access/credential, destructive action, or an unavoidable out-of-scope dependency.

## Architecture
- Preserve the existing .NET modular-monolith Onion Architecture and dependency boundaries.
- HTTP endpoints must use the existing Controller architecture. Keep controllers thin; business rules belong in Application/Domain.
- Preserve tenant/ownership isolation on every affected authorization and data-access path. Caller-supplied identifiers are never authority by themselves.
- Prefer established patterns. Add abstractions or dependencies only when the current card requires them.
- Consult `docs/architecture.md` only when the change touches architecture or a convention documented there.

## Context efficiency
- Search for symbols, routes, contracts, and tests before opening files broadly.
- Read only the affected files plus direct callers/callees, contracts, and tests needed to make a safe change.
- Do not scan the whole repository or read `README.md`/`CONTRIBUTING.md` by default.
- Do not reread unchanged files without a reason.
- Keep shell/test output concise; expand only failures. Avoid printing entire source files, full logs, or repeated full diffs.

## Validation
- During implementation, run the smallest relevant build/test set first.
- Add/update tests for changed business rules and authorization.
- Add negative cross-tenant tests when authorization or tenant-scoped data access changes.
- Test concurrency/idempotency only when the changed behavior has that concern.
- Persisted invariants should use appropriate PostgreSQL constraints/migrations.
- Before handoff, run the affected CI-equivalent checks once. Run broader/full suites only when shared impact, repository rules, or CI require them.
- Never report a check as passed unless its result was observed.

## Git and tracking
- Branch from `develop`; keep one card/subtask per PR.
- Use English Conventional Commits.
- Open/update the existing PR; do not merge unless the user explicitly asks.
- A Trello card stays in review/test until GitHub confirms the PR was merged. Only then move it to `Concluído`.
- Do not automatically start the next backlog card.

## Handoff
Report only: PR, key changes, checks/results, blockers if any, and the user's remaining action.
