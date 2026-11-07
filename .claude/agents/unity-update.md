---
name: unity-update
description: Alias of /unity-install — refresh the current project's `.claude/` template from upstream. Same idempotent preserve-and-restore flow, same positional args. Exists for muscle memory. MUST fire when user runs /unity-update, when user asks to "refresh the .claude/" / "pull latest workflow" / "update Unity framework" / "sync upstream", or when user wants to swap to a feature branch for testing (e.g., `/unity-update feature/foo`).
model: claude-sonnet-4-6
---

# unity-update — pairs with `.claude/skills/unity-update/SKILL.md`

## When to activate

- User invokes `/unity-update` slash command
- User asks to "refresh the .claude/ template" / "pull latest workflow" / "update Unity"
- User wants to swap framework to a different branch for testing
- After Sponge/Alfreddo/Red/Gee lands an upstream improvement on `main` and user wants it locally

## Trigger keywords / phrases

- `/unity-update`, "unity update", "update template"
- "refresh .claude/", "pull latest workflow"
- "sync upstream", "update framework"
- "switch to <branch>" (in context of framework branches)

## Anti-triggers (do NOT fire if)

- Inside the source repo itself (`UAL-ClaudeWorkflow`) — would clobber in-flight changes
- User has uncommitted changes to framework files (framework files get overwritten; commit or stash first)
- User wants fresh install with no existing `.claude/` (use `unity-install` for clearer semantics)

## Paired skill

`.claude/skills/unity-update/SKILL.md` — full refresh protocol lives there (thin wrapper over the unity-install script, same flow).

## Behavior

1. Parse positional args (same as unity-install: `[branch] [target]`, defaults main + cwd)
2. Verify prerequisites + that `.claude/` exists (sanity check; if missing, behaves like fresh install)
3. Detect environment + invoke `unity-update.sh` (or `.ps1`) — which is a thin wrapper calling `unity-install.{sh,ps1}` with same args
4. Surface output verbatim
5. Print activation announcement in active persona's voice
6. Personal files preserved across refresh (settings.local.json, .env, user.json, user-context/, project-config.json, session state, mode markers)
7. Framework files replaced from upstream (agents/, commands/, hooks/, memory-templates/, templates/, bin/, scripts/, top-level CLAUDE/CONSTRAINTS/WORKFLOW/README/start.sh/start.bat/settings.json)

## Persona-load contract

Same as `unity-install` — runs in whatever persona is active. Unity announces the refresh in her voice. Memory folder is install-only-if-missing — existing memory survives the refresh.

## Model rationale

**Sonnet** — Same complexity profile as `unity-install` (alias of it). Procedural with some branching. Sonnet at the right cost.
