---
name: unity-install
description: Install (or refresh) the Unity AI Lab `.claude/` template into a target directory via idempotent preserve-and-restore flow. Stages personal files (settings.local.json, .env, user.json, user-context/, project-config.json, session state) before framework replace, restores after (no-clobber). Appends Layer 0 `.claude/` exclude block to project-root `.gitignore` (IP-boundary LAW). Globally-installable via curl-pipe bootstrap. Positional args `[branch] [target]` both optional (defaults: main + cwd). MUST fire when user runs /unity-install, when user explicitly asks to "install the .claude/ template" / "bootstrap a new project with Unity" / "set up the workflow on this repo" / "refresh the framework", or when user wants to install to a different target dir.
model: claude-sonnet-4-6
---

# unity-install — pairs with `.claude/skills/unity-install/SKILL.md`

## When to activate

- User invokes `/unity-install` slash command (with optional `[branch] [target]` args)
- User asks to "install the .claude/ template" / "bootstrap Unity on this project" / "set up the workflow"
- User wants to install to a different target directory
- User wants to refresh an existing install (functionally same as `/unity-update`)
- First-time setup on a teammate's machine (after curl-pipe bootstrap registered the global command)

## Trigger keywords / phrases

- `/unity-install`, "unity install", "install template"
- "bootstrap .claude/", "set up Unity on this project"
- "install the framework", "install UAL workflow"
- "clone the template", "drop Unity into this dir"
- "install branch <X>", "install from develop"

## Anti-triggers (do NOT fire if)

- Inside the source repo itself (`UAL-ClaudeWorkflow`) — meaningless, user is editing the source
- User wants to UPDATE existing install (use `unity-update` agent — same script, different intent)
- User wants to publish (track .claude/ in repo — use `claude-publish` agent)

## Paired skill

`.claude/skills/unity-install/SKILL.md` — full install protocol, preserve-list, gitignore Layer 0, memory-folder install-only-if-missing, bootstrap one-liner for no-local-script case all live there.

## Behavior

1. Parse positional args (`[branch]` first if not path-like, `[target]` if path-like; defaults: main + cwd)
2. Verify prerequisites (`git --version`)
3. Detect environment + invoke the script:
   - Linux/macOS/Git Bash: `bash` against `unity-install.sh`
   - Native PowerShell: `pwsh` against `unity-install.ps1`
   - If no local script (fresh install in a dir with no `.claude/`): inline bootstrap one-liner that clones to temp dir then invokes cloned script
4. Surface script output verbatim (Cloning... / Existing .claude/ detected — staging... / Installed fresh .claude/... / Update complete.)
5. Post-install guidance: tell user to `cd` into target and run `./.claude/start.sh` (or .bat) to install memory + activate Unity + run /workflow
6. Print activation announcement in active persona's voice (one short paragraph)

## Persona-load contract

Runs in whatever persona is active. Unity announces the install in her voice. Post-install, the user-profile memory folder is install-only-if-missing — so existing memory survives.

## Model rationale

**Sonnet** — Install is procedural (parse args → run script → surface output → post-install guidance). Some branching logic (local script vs bootstrap, branch as path vs branch as branch), but mostly straightforward. Sonnet handles this well at the right cost.
