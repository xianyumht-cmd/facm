# AGENTS.md — Mandatory AI Repository Rules

> **ChatGPT / Codex / other coding agents: read this before any repository action.** These are standing user instructions; do not wait for the user to repeat them.

## Canonical branch
- Canonical development branch: `main`.
- New code work starts from the latest canonical branch. Read-only diagnosis does not require a branch.

## Branch lifecycle
- One task = one temporary branch.
- Before creating one, check for an active branch/PR for the same task.
- Keep revisions on the same task branch; do not create `v2`, `v3`, `final`, `current`, `backup`, `test`, `build`, `deploy`, `handoff`, `note`, or packaging branches.
- Do not use branches as chat memory, backups, artifacts, or status records. Use Issues/task-state docs instead.
- After tests/CI and PR merge, verify `main`, update the Issue/task record, then delete the temporary branch.

## Builds and history
- Use CI/Actions artifacts for build/deployment packages.
- Use tags for immutable candidates/releases/production snapshots.
- Preserve unique old history with a verified `archive/*` tag before deleting its branch.

## Git safety
Unless the user explicitly authorizes a narrowly scoped recovery operation, do not force push, `git reset --hard`, history-rewrite rebase, `git clean`, `git stash`, destructively move refs, or switch branches as an error-recovery shortcut.

If worktree/origin/branch/history is unexpected or divergent, stop and report exact state instead of rewriting history. Never commit secrets, credentials, local env files, sensitive diagnostics, or unrelated generated files.

## AI takeover protocol
At task start: read all applicable `AGENTS.md`; identify current/canonical branch, origin, and worktree status; inspect referenced Issue/PR; discover project build/deploy docs; make the smallest safe change on one task branch.

Destructive Git operations, branch/tag deletion, production deployment, or service restart require explicit user intent and fresh safety checks immediately before execution.

Persistent remote branches should stay minimal; temporary branches exist only while their task is active.
