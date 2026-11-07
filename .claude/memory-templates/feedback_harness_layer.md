---
name: Harness layer — Claude Code settings.json hooks
description: The .claude/ template ships with 6 settings.json hooks under .claude/hooks/ as the execution-standardization layer. Hooks are pure enhancements that run on every team member's machine identically (Linux / Git Bash / native PowerShell). They do NOT replace any LAW, ceremony, gate, or read discipline — those all stay. Only ONE hook blocks (pre-tool-bash-safety, exit 2 on project-root delete / system-path delete / sudo/runas). Every other hook is exit 0 and additive.
type: feedback
---

The Unity AI Lab `.claude/` template ships with a harness layer in `settings.json` that fires Claude Code hook scripts at six lifecycle events. Hooks are an enhancement, NOT a replacement for any existing discipline.

**Hook inventory (Option A node primary / Option B bash fallback at `.claude/hooks/`):**

1. `SessionStart` (matcher: `startup|resume|clear|compact`) → `session-start-env-dump.cjs` — env JSON injected as context (OS, shell, git, opt-in, TODO summary, FINALIZED last entry, memory drift, persona)
2. `UserPromptSubmit` (no matcher) → `user-prompt-state-refresh.cjs` — compact between-turn recap (branch, opt-in, in-progress TODO)
3. `PostToolUse` (matcher: `Edit|Write|MultiEdit`) → `post-tool-memory-sync.cjs` — auto-`cp` edited memory templates to the user-profile memory folder; no manual sync
4. `PreCompact` (no matcher) → `pre-compact-snapshot.cjs` — writes `.claude/.session-state.md` so post-compact Unity recovers context
5. `Stop` (no matcher) → `stop-session-writeup.cjs` — updates `.claude/.last-session.md` on every turn boundary; read by next session's SessionStart
6. `PreToolUse` (matcher: `Bash`) → `pre-tool-bash-safety.cjs` — the ONLY blocking hook; exit 2 on project-root deletion, system-path deletion, or privileged commands (sudo/su/doas/runas/RunAs). Routes each to the user with a paste-ready command instead of running it inside the session.

**Why:** the team needed standardized workflow execution across Linux Sponge / Windows-Git-Bash Alfreddo / server-distro Red. Hooks are the harness layer that makes everyone's session-start, between-prompt state, memory deploys, compaction recovery, and admin delegation work the same way regardless of OS or machine. Hooks ENHANCE the existing ceremony (LAWs, TODO/FINALIZED dance, 800-line read, persona system, memory templates) — they NEVER replace any of it.

**How to apply:**

- Trust hook output as authoritative session context — `session-start-env-dump`'s JSON has the OS / shell / git state / opt-in marker / TODO summary already; do NOT redundantly re-run `pwd` / `git status` / `cat /etc/os-release` at session start unless the hook output is missing or stale.
- The `user-prompt-state-refresh` injection is up-to-date per-prompt; rely on it for current branch + opt-in state + in-progress TODO count without re-reading.
- When editing a file in `.claude/memory-templates/`, do NOT manually `cp` to the user-profile memory folder — the `post-tool-memory-sync` hook handles it automatically. Just edit and trust the auto-sync.
- On post-compaction resume, read `.claude/.session-state.md` (written by `pre-compact-snapshot`) to recover context that compaction may have lost.
- On session start, the SessionStart hook's stdout already includes `.last-session.md`-equivalent context; pick up where the previous session left off without manual reconstruction.
- If `pre-tool-bash-safety` blocks a Bash command (exit 2, stderr message), the user has been told the exact paste-ready command to run in their own terminal. Wait for their "done" reply before proceeding.
- Hook scripts default to `node ...cjs` (Option A). The `.cjs` extension is mandatory (not `.js`) — it forces Node to treat the script as CommonJS regardless of the host project's `package.json` `"type"` field, so the harness works inside ESM projects too. If a team member's machine lacks node, swap each hook's `command` field in `settings.json` from `node "..."` to `bash "..."` — every script has a `.sh` sibling.
- Adding a new hook: write `.cjs` + `.sh` siblings under `.claude/hooks/`, add an entry to `settings.json` under the relevant event, document in `WORKFLOW.md §HARNESS LAYER`. Smoke test with `echo '<stdin>' | node <script>.cjs`.
- Disabling a hook: remove its entry from `settings.json`. Don't delete the script — it's team-standard library code.
- Files written by hooks (`.session-env.json`, `.session-state.md`, `.last-session.md`) are machine-local state and should be `.gitignore`'d once a project becomes a git repo. The `project-config.json` (Git Flow opt-in marker) is team-shared and IS tracked.
- Full design + table of events + script side-effects: `.claude/WORKFLOW.md §HARNESS LAYER`.
