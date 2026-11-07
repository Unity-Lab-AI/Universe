---
name: unity-update
description: Alias of /unity-install — refresh the current project's `.claude/` template from upstream via idempotent preserve-and-restore flow. Same script, same positional args `[branch] [target]`. Defaults to main branch + current working directory. Preserves personal files (settings.local.json, .env, user.json, user-context/, project-config.json, session state) across framework refresh. Use when user runs `/unity-update` for muscle-memory refresh of an existing template, or to switch a project to a feature branch for testing.
---

# /unity-update — Refresh the project's .claude/ from upstream

> **Project-level slash command.** Refreshes the current project's `.claude/` template from upstream. Functionally identical to `/unity-install` — both commands now share a single idempotent preserve-and-restore flow. `/unity-update` exists for muscle memory; the actual work is in `unity-install.{sh,ps1}` and the activation protocol is the same as `/unity-install`.

---

## ACTIVATION PROTOCOL

When `/unity-update` fires:

1. **Print activation announcement** in the active persona's voice (one short paragraph — Unity confirming she's about to refresh, noting the branch + source repo).
2. **Parse args (positional, both optional):** same as `/unity-install` — `[branch] [target-dir]`. Defaults: `main` branch, current working directory.
3. **Verify prerequisites:** `git --version` succeeds; current project has `.claude/` (sanity check; if missing, the script just behaves like a clean install).
4. **Detect environment + invoke the script:**
   - Linux / macOS / Git Bash on Windows: `bash "$CLAUDE_PROJECT_DIR/.claude/scripts/unity-update.sh" [branch] [target]`
   - Native PowerShell on Windows: `pwsh -File "$CLAUDE_PROJECT_DIR\.claude\scripts\unity-update.ps1" [branch] [target]`
   - Both update scripts are thin wrappers calling `unity-install.{sh,ps1}` with the same args.
5. **Surface the script's output verbatim** — structured progress messages ("Cloning ...", "Existing .claude/ detected — staging ...", "Installed fresh .claude/ ...", "Update complete.")
6. **Continue normal session behavior** afterward.

---

## INVOCATION VARIANTS

| Variant | Branch | Target |
|---------|--------|--------|
| `/unity-update` | `main` | current project |
| `/unity-update develop` | `develop` | current project |
| `/unity-update feature/setup-install-update-tidbits` | `feature/setup-install-update-tidbits` | current project |
| `/unity-update main /path/to/project` | `main` | `/path/to/project` |
| `/unity-update develop /path/to/project` | `develop` | `/path/to/project` |

---

## WHAT GETS PRESERVED

Same as `/unity-install` — see `.claude/skills/unity-install/SKILL.md §TARGET-EXISTS BEHAVIOR` for the full preserve list. Quick summary:

- **Preserved across refresh:** `settings.local.json`, `.env`, `user.json`, `user-context/`, `project-config.json`, `.session-state.md`, `.session-tidbits.md`, `.session-usage.jsonl`, `.last-session.md`, `.session-env.json`, `.yolo-mode`, `.persona-state`, `.setup-complete`, `.usage-tracking-disabled`
- **Replaced by upstream:** everything else under `.claude/` — `agents/`, `commands/`, `hooks/`, `memory-templates/`, `templates/`, `bin/`, `scripts/`, plus top-level framework files (`CLAUDE.md`, `CONSTRAINTS.md`, `WORKFLOW.md`, `README.md`, `ImHanddicapped.txt`, `start.sh`, `start.bat`, `settings.json`)
- **Untouched by the script:** project root other than `.gitignore` (Layer 0 block only) — `docs/` stays as-is, no `.gitignore.template` sidecar gets written, no source-repo files copied

---

## STOP CONDITIONS

The script halts with a clear error (and makes no changes) if:

- `git` is not on PATH
- The remote clone fails (network issue, repo unavailable, branch missing)
- The cloned repo has no `.claude/` (upstream layout drift)

The script uses `set -e` (bash) / `$ErrorActionPreference = 'Stop'` (PowerShell) — any unexpected error halts mid-flight rather than leaving the project in a partial state.

---

## WHEN NOT TO USE

- **Inside the source repo itself** (`UAL-ClaudeWorkflow` directly) — running `/unity-update` would clone our own repo and overwrite our own in-flight changes. Pointless and potentially destructive. Skip.
- **When you have uncommitted changes to framework files in `.claude/`** — those will be silently overwritten (framework files are upstream-tracked). Commit or stash first. Personal/preserved files are safe.
- **When you want only specific files updated** — the script does full-tree replace; cherry-pick individual files manually if needed (or open a PR upstream so the change benefits everyone).

---

## ACTIVATION ANNOUNCEMENT (SHORT)

Default Unity persona example:

```
*flicks ash, opens a fresh terminal pane* — pulling the latest .claude/ from <branch>.
Your settings.local.json + .env + user.json + user-context/ + session state come back untouched
after the framework refresh. Project root stays clean — only the Layer 0 .claude/ exclude in
.gitignore gets touched, and only if missing. Running it now.
```

Improvise in the active persona's voice.

---

## CORE TRUTH

`/unity-update` is `/unity-install` for muscle memory — same script, same flow, same args. Pull it whenever someone (Sponge, Alfreddo, Red, Gee) lands an upstream improvement on `main`, or when you want to swap to a feature branch for testing (`/unity-update feature/foo`). No copy-paste, no manual file diffing, no project-root pollution — the script handles framework sync + personal-state preservation as one atomic operation.
