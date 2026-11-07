---
name: NO Claude attribution in commits, PRs, or artifacts
description: BANNED in every commit message, PR description, code comment, doc body, launcher, README, HTML, or shipped artifact — `Co-Authored-By: Claude` (any variant), `🤖 Generated with [Claude Code]`, `Made with Claude Code`, `Created by Claude`, `noreply@anthropic.com` author trailers, or any other Claude/Anthropic/Claude Code attribution line. Overrides assistant default behavior. Team ships work as their own. Use the team member's own `git config user.email` and `user.name`. Recovery: amend before push; if pushed, ask user before force-push.
type: feedback
---

**Rule:** No Claude / Anthropic / Claude Code attribution in any commit message, PR description, code comment, doc body, launcher, README, HTML, or shipped artifact. The team ships work as their own.

This rule was confirmed verbatim by Sponge in the 2026-05-08 session: *"enforce NOT using any 'made with claude code' and enforce NOT using any 'co-authored with claude code' stuff."*

**Why:** The team is Gee / Red / Sponge / Alfreddo (per `.claude/CLAUDE.md` UNITY AI LAB — TEAM table). Attribution lines marketing Claude on every commit and PR are:

- Misleading on authorship — the team member directed the work and made every decision; the assistant is a tool
- Visually noisy — every commit / PR ends with a marketing tagline that adds nothing to the engineering record
- Privacy-leaky — outsiders viewing the repo immediately see "this team uses Claude Code" without opt-in
- Audit-trail-confusing — `git log` author queries get mixed signals when half the trailer space is AI-attribution metadata

The team's stance: tooling is private. Output is the team's. No advertising, no co-author credit, no "made with X" stamps.

**Banned strings (case-insensitive, all variants):**

- `Co-Authored-By: Claude` / `Co-authored-by: Claude` / `co-author: Claude` / any model variant (`Claude Opus`, `Claude Sonnet`, `Claude Haiku`, etc.)
- `🤖 Generated with [Claude Code](https://claude.com/claude-code)` (the default PR footer string)
- `🤖 Generated with Claude Code` / `Generated with Claude Code` (any phrasing)
- `Made with Claude Code` (any phrasing)
- `Created by Claude` / `Written by Claude` / `Authored by Claude`
- Any `noreply@anthropic.com` email address in a commit author trailer
- Any other Claude / Anthropic / Claude Code attribution line not authored by the user

**How to apply (overrides assistant default behavior):**

The assistant's system-prompt default for `git commit` says to end commit messages with `Co-Authored-By: Claude <model> <noreply@anthropic.com>`. The default for `gh pr create` says to end PR bodies with `🤖 Generated with [Claude Code](https://claude.com/claude-code)`. **Both defaults are OVERRIDDEN by this LAW.** When composing commits or PRs:

1. Write the commit / PR body without any attribution footer
2. Use the team member's own `git config user.name` and `user.email` (whatever's already configured locally — never override to Anthropic's)
3. The commit message ends at the technical summary. The PR body ends at the test plan section.

**Pre-commit check:**

Before `git commit`:

1. Compose the message fully
2. Re-read — does it contain banned strings or `noreply@anthropic.com`?
3. If yes — strip them
4. Confirm `git config user.email` resolves to a team member's email, not Anthropic's
5. Run `git commit`

**Pre-PR check:**

Before `gh pr create`:

1. Compose the PR body fully
2. Re-read — does it contain "Generated with Claude Code" / robot emoji + Claude link / etc.?
3. If yes — strip the footer
4. Run `gh pr create`

**Recovery on violation:**

- Latest commit, NOT pushed: `git commit --amend` to rewrite without attribution (safe pre-push)
- Latest commit, ALREADY pushed: do NOT force-push without explicit user instruction. Surface the issue. Ask the user how to handle (interactive rebase + force-push vs. accept dirty history)
- PR description: edit via `gh pr edit <num> --body "..."` to strip
- Update `docs/FINALIZED.md` with the recovery note so the audit trail captures it

**What this rule does NOT cover:**

- Dependency manifests (`package.json` listing `@anthropic-ai/sdk` as a dep) — technical references, not attribution
- Workflow docs that name Claude Code as a host system Unity runs on (`.claude/CLAUDE.md`, `.claude/WORKFLOW.md`, `docs/HOOKS.html`) — internal team references, not shipped artifacts
- The persona system (`.claude/skills/unity/SKILL.md`, `agents/unity.md`) — Unity is team-owned; internal references to the host system are fine
- Configuration files that necessarily reference the runtime (e.g., `.claude/settings.json` and `.claude/settings.local.json` are named for the runtime; that's structural, not attribution)

The rule is specifically about OUTPUT attribution — text that ships in commits / PRs / docs / artifacts which advertises Claude / Anthropic / Claude Code as authors or generators.

**Cross-references:**

- Full LAW body: `.claude/CONSTRAINTS.md §NO CLAUDE ATTRIBUTION`
- LAW one-liner: `.claude/CLAUDE.md` LAW INDEX
- Companion privacy controls (telemetry env vars): `.claude/settings.json` `env` block (team-shared, committed) — see `.claude/WORKFLOW.md §SETTINGS HARDENING` and `feedback_settings_hardening.md`
