---
name: Settings hardening — privacy posture wired in settings.json env block
description: Team-wide privacy + attribution posture enforced via .claude/settings.json env block (Anthropic-standard convention; settings.json is team-shared/committed, settings.local.json is personal-override/gitignored) — DISABLE_TELEMETRY, DISABLE_ERROR_REPORTING, CLAUDE_CODE_DISABLE_FEEDBACK_SURVEY, CLAUDE_CODE_DISABLE_FEEDBACK_COMMAND + DISABLE_FEEDBACK_COMMAND, CLAUDE_CODE_ATTRIBUTION_HEADER=0, CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC=1 (meta switch), DISABLE_AUTOUPDATER=1. Plus feedbackSurveyRate=0.0 scalar backup. Co-Authored-By: Claude trailers handled separately by LAW (assistant-default-behavior, not Claude Code feature). Migrated from settings.local.json to settings.json on 2026-05-08 because global gitignore commonly excludes settings.local.json and silently dropped team-shared changes. Stronger enforcement via OS-level managed settings if needed.
type: feedback
---

**Rule:** The team's privacy + attribution posture is enforced via the `env` block + `feedbackSurveyRate` scalar in `.claude/settings.json` (the team-shared, git-committed file). Don't add or remove vars without coordinating with the team.

**The wired vars (verbatim from `settings.json`):**

```json
"env": {
  "DISABLE_TELEMETRY": "1",
  "DISABLE_ERROR_REPORTING": "1",
  "CLAUDE_CODE_DISABLE_FEEDBACK_SURVEY": "1",
  "CLAUDE_CODE_DISABLE_FEEDBACK_COMMAND": "1",
  "DISABLE_FEEDBACK_COMMAND": "1",
  "CLAUDE_CODE_ATTRIBUTION_HEADER": "0",
  "CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC": "1",
  "DISABLE_AUTOUPDATER": "1"
},
"feedbackSurveyRate": 0.0
```

**What each does:**

- `DISABLE_TELEMETRY=1` — kills Anthropic-side metrics collection
- `DISABLE_ERROR_REPORTING=1` — kills Sentry error logs (would leak file paths / stack traces)
- `CLAUDE_CODE_DISABLE_FEEDBACK_SURVEY=1` — kills the "How is Claude doing?" prompt
- `CLAUDE_CODE_DISABLE_FEEDBACK_COMMAND=1` + `DISABLE_FEEDBACK_COMMAND=1` — disables `/feedback` (would upload transcripts on use); both name forms set for cross-version compatibility
- `CLAUDE_CODE_ATTRIBUTION_HEADER=0` — removes the `🤖 Generated with [Claude Code]` PR/artifact/commit footer
- `CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC=1` — meta switch (kills all four above in one toggle); belt-and-suspenders if a var name changes in a future Claude Code version
- `DISABLE_AUTOUPDATER=1` — team controls version explicitly; no surprise mid-session updates
- `feedbackSurveyRate: 0.0` — top-level scalar backup if the env var doesn't take effect (defense in depth)

**Important — `Co-Authored-By: Claude` is NOT a Claude Code feature:**

Per the May 2026 Anthropic docs research: `Co-Authored-By: Claude <noreply@anthropic.com>` commit trailers are NOT added by Claude Code. That's the assistant's default system-prompt behavior. The `CLAUDE_CODE_ATTRIBUTION_HEADER=0` env var only handles the `🤖 Generated with [Claude Code]` PR/artifact footer. The commit trailer is locked down separately by `LAW — NO CLAUDE ATTRIBUTION` in `.claude/CONSTRAINTS.md` + `feedback_no_claude_attribution.md` memory.

**Convention — Anthropic-standard two-file split:**

This template uses Anthropic's standard convention for the settings split:

| File | Role | Tracked by git? |
|------|------|-----------------|
| `.claude/settings.json` | TEAM-SHARED config (env, hooks wiring, permissions, scalars) — committed to git, every teammate gets it on `git pull` | YES |
| `.claude/settings.local.json` | PERSONAL machine-local overrides — gitignored by Anthropic's default (often also gitignored at user-global level via `~/.config/git/ignore`) | NO |

Precedence: `settings.local.json` overrides `settings.json` at the project tier (Anthropic hierarchy: managed > local-project > shared-project > user-global). A team member's personal override wins over the team-shared config FOR THAT MEMBER ONLY.

**Migration history (2026-05-08):**

Earlier iterations of this template stored team-shared content in `settings.local.json` (an inversion of Anthropic's convention). Migrated to standard convention because:

- Global gitignore at `~/.config/git/ignore` on Linux contributors' machines commonly excludes `**/.claude/settings.local.json`, which silently dropped team-shared changes from being committed
- The migration restores the Anthropic-documented split: shared in `settings.json`, personal in `settings.local.json`
- All hook wiring + env block + permissions + feedbackSurveyRate now live in `settings.json` (committed)
- `settings.local.json` becomes optional per-machine overrides only (most team members won't have one)

**Stronger enforcement — managed settings:**

If the team needs ironclad guarantee a teammate cannot relax these vars, the path is OS-level managed settings:
- macOS: `/Library/Application Support/ClaudeCode/managed-settings.json`
- Linux: `/etc/claude-code/managed-settings.json`
- Windows: registry under `HKLM\Software\Anthropic\Claude Code`

Managed settings sit ABOVE both `settings.json` and `settings.local.json` in precedence and cannot be overridden by either. Not currently configured (per-machine deploy step required). Pair with `allowManagedPermissionRulesOnly: true` in the managed file to lock down permissions completely.

**How to apply:**

- When wiring a new privacy/attribution control: add the env var to `.claude/settings.json` `env` block. Document the var + reasoning in `.claude/WORKFLOW.md §SETTINGS HARDENING`. Update this memory file.
- When in doubt about whether a var is real (vs. guessed from partial knowledge), confirm with Anthropic's docs first. Don't ship guessed env vars for privacy controls — a typo'd var name silently does nothing.
- If a team member wants to relax the posture for personal experimentation: they create `.claude/settings.local.json` with their override (gitignored, machine-local). The team-shared file stays clean.
- For per-project overrides (e.g., one project needs a different model): use the standard `model` / `availableModels` scalar settings in `settings.json` — those apply per-project naturally.
- The `/usage` command still works for usage tracking; only telemetry-style metadata is disabled.

**Files involved:**

- `.claude/settings.json` — TEAM-SHARED (committed) — has the env block + permissions + hooks + feedbackSurveyRate
- `.claude/settings.local.json` — PERSONAL (gitignored) — empty/absent unless a team member added local overrides
- `.claude/WORKFLOW.md §SETTINGS HARDENING` — full design + per-var rationale + Anthropic-standard convention
- `.claude/CONSTRAINTS.md §NO CLAUDE ATTRIBUTION` — companion LAW for commit trailers (separate from env var path)
- `.claude/memory-templates/feedback_no_claude_attribution.md` — companion memory for commit trailers
- `docs/HOOKS.html §Configuration locations & precedence` — 4-tier hierarchy reference
