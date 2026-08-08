# AGENTS.md — Mandatory AI Repository Rules

> **ChatGPT / Codex / other coding agents: read this file before any repository action.** These are standing user instructions. Do not wait for the user to repeat them in a new chat.

## Canonical branch and task lifecycle

- Canonical development branch: `main`.
- New code work starts from the latest canonical branch. Read-only diagnosis does not require a branch.
- One task = one short-lived task branch. Before creating one, check for an active branch/PR for the same task.
- Keep all revisions for the same task on that branch. Do not create auxiliary `v2`, `v3`, `final`, `current`, `backup`, `test`, `build`, `deploy`, `handoff`, `note`, or packaging branches.
- Do not use branches as chat memory, backups, artifacts, deployment packages, or task-status records.
- Track unfinished work in GitHub Issues or canonical repository task-state documentation.
- Use CI/Actions artifacts for build/deployment packages and immutable tags for releases, accepted candidates, production snapshots, or historical archives.
- Preserve unique old history with a verified `archive/*` tag before branch deletion.
- After tests/CI and PR merge, verify `main`, update Issue/task state and knowledge docs, then delete the temporary task branch.

## Git and repository safety

Unless the user explicitly authorizes a narrowly scoped recovery operation, do not use force push, `git reset --hard`, history-rewriting rebase, `git clean`, `git stash`, destructive ref moves, or automatic branch switching as an error-recovery shortcut.

If worktree/origin/branch/history is unexpected or divergent, stop and report exact state instead of rewriting history. Never commit secrets, tokens, credentials, local environment files, sensitive diagnostics, or unrelated generated files.

Destructive Git operations, branch/tag deletion, production deployment, or service restart require explicit user intent and fresh safety checks immediately before execution.

## AI takeover protocol

At the start of every task:

1. read this file and any more specific nested `AGENTS.md`;
2. identify current branch, canonical branch, origin, and working-tree status;
3. inspect referenced Issue/PR and discover repository-specific build/deploy docs;
4. read the canonical knowledge files listed below when they exist;
5. verify important current-state claims against repository/runtime evidence;
6. make the smallest safe change on one task branch when changes are authorized.

## Self-maintaining AI knowledge system

The user should **not** need to remember or repeat documentation/knowledge-maintenance chores. When the user authorizes a repository-changing task, maintaining relevant repository knowledge is part of that task without a separate reminder. Do not perform repository writes during a purely read-only request.

### Canonical knowledge files

Read and maintain these when relevant:

- `docs/AI_CONTEXT.md` — stable project purpose, boundaries, terminology, major modules, and external dependencies.
- `docs/PROJECT_STATE.md` — current verified state, active Issues/PRs, accepted versions/results, incomplete work, blockers, and next actions.
- `docs/PITFALLS.md` — recurring mistakes, failed approaches, root causes, detection signals, and prevention rules.
- `docs/DECISIONS.md` — durable technical/product/operations decisions and why they were made.
- `docs/ARCHITECTURE.md` — components, ownership boundaries, data flow, contracts, and integration relationships.
- `docs/OPERATIONS.md` — build, release, deployment, verification, recovery, rollback, and maintenance runbooks.
- `docs/AI_WORKSTYLE.md` — stable repository-relevant collaboration preferences that help AI work effectively with the user.

Missing files are not an error. **Do not create empty boilerplate.** Create a canonical file only when real durable information first belongs there.

### Automatic routing and maintenance

- Stable mandatory AI behavior/safety/workflow -> `AGENTS.md`.
- Stable project facts/vocabulary -> `AI_CONTEXT.md`.
- Time-sensitive current state -> `PROJECT_STATE.md`; replace stale facts instead of accumulating chat-style logs.
- Reusable lesson from an AI mistake, failed approach, or user correction -> `PITFALLS.md`, including cause and prevention.
- Durable choice between alternatives -> `DECISIONS.md`, including date, reason, consequences, and superseded decision when relevant.
- Changed components/contracts/data flow -> `ARCHITECTURE.md`.
- Changed build/deploy/rollback/verification procedure -> `OPERATIONS.md`.
- Stable repository-relevant user collaboration preference -> `AI_WORKSTYLE.md`; do not store unrelated personal profile information.

Before declaring a substantive task complete, reconcile these files with actual Git/Issue/PR/runtime evidence and update every materially affected canonical file. A task is **not fully closed** if repository knowledge is materially stale after the change.

Do not create `*-v2.md`, `final-notes.md`, dated handoff duplicates, or parallel knowledge files when a canonical file can be updated. Do not create a separate branch merely for handoff/knowledge updates; include them in the same authorized task branch/PR.

## Evidence and knowledge hygiene

- Current repository/runtime evidence outranks stale documentation.
- Clearly distinguish **verified current state**, **planned work**, and **inference**.
- Prefer exact Issue/PR numbers, tags, commit SHAs, paths, service names, and dates when useful for auditability.
- Never store passwords, API tokens, cookies, private keys, session secrets, or credential-bearing diagnostics in repository knowledge files.
- Do not dump large logs or chat transcripts into the knowledge base; summarize the reusable conclusion and durable evidence.
- If existing knowledge conflicts with reality, correct or mark it superseded as part of the current task instead of silently working around it.
- Update `AGENTS.md` itself only when a new long-lived mandatory rule is discovered.

The goal is that a future AI session can enter the repository, read the canonical files, inspect live repository state, and continue safely **without relying on the user to remember previous instructions or retell project history**.
