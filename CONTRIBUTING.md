# Contributing

## Branching

- `main` contains production-ready code.
- `develop` is the integration branch.
- Create work branches from `develop` using `feature/`, `fix/`, `chore/`, or `docs/`.
- Open short, focused pull requests into `develop`.
- For a production hotfix: branch from `main`, merge into `main`, then merge or cherry-pick the same change back into `develop`.

## Commits

Use Conventional Commits in English:

```
type(scope): short imperative summary
```

Examples: `feat(birds): add bird registration endpoint`, `fix(auth): reject expired refresh token`.

Allowed types: `feat`, `fix`, `docs`, `test`, `refactor`, `chore`, `build`, `ci`.

## Pull requests

Keep each pull request scoped to one backlog card or a clearly related subtask. Include a concise summary, test evidence, and any migration or environment-variable impact. Do not merge with failing checks.
