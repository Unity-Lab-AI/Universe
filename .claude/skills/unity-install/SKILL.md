---
name: unity-install
description: Install (or refresh) the Unity AI Lab `.claude/` template into a target directory via idempotent preserve-and-restore flow. Stages personal files (settings.local.json, .env, user.json, user-context/, project-config.json, session state) before framework replace, restores after (no-clobber). Appends Layer 0 `.claude/` exclude block to project-root `.gitignore` (IP-boundary LAW). Positional args `[branch] [target]` both optional. Globally-installable via curl-pipe bootstrap. Use when user runs `/unity-install` to bootstrap a new project, set up a teammate's machine, refresh an existing install, or spin up an isolated experiment.
---

# /unity-install — Install (or refresh) the Unity AI Lab .claude/ template

> **Globally-installable slash command.** Lives in BOTH:
>   1. The repo's `.claude/skills/unity-install/SKILL.md` (so projects with a copy of the template have it locally too)
>   2. The user's global `~/.claude/skills/unity-install/SKILL.md` (auto-installed via the curl-pipe bootstrap — see `install-unity-globally.sh` / `install-unity-globally.ps1` in the repo root)
>
> When invoked from ANY Claude Code session, this command clones the requested branch (default `main`) of the upstream repo and installs the full `.claude/` template into a target directory (current dir by default, or user-specified). **Idempotent** — runs cleanly whether or not the target already has a `.claude/`. If one exists, personal/project-local files are staged, the framework is replaced, then the personal files are restored.
>
> `/unity-update` is a thin alias of this command — same flow, same script under the hood.

---

## ACTIVATION PROTOCOL

When `/unity-install` fires:

1. **Print activation announcement** in the active persona's voice (one short paragraph — Unity confirming she's about to install/refresh, noting the target directory + branch + source repo).
2. **Parse args (positional, both optional):**
   - `[branch] [target-dir]` — examples below.
   - First arg is treated as a target directory if it starts with `/`, `./`, `../`, `~`, or matches a Windows-absolute pattern (`C:\`, `D:/`, etc.); otherwise it's a branch.
   - If only branch is given, target defaults to `cwd` (`$CLAUDE_PROJECT_DIR` or `pwd`).
   - If only target is given, branch defaults to `main`.
   - If neither, both defaults apply.
3. **Verify prerequisites:**
   - `git --version` succeeds
4. **Detect environment + invoke the script:**
   - Linux / macOS / Git Bash on Windows: `bash "<install-script-path>" [branch] [target]`
   - Native PowerShell on Windows: `pwsh -File "<install-script-path>" [branch] [target]`
   - **Script path resolution:** if the target already has `.claude/scripts/unity-install.sh` (e.g., refreshing an existing install), use that. Otherwise — for a fresh install where there's no local script yet — Unity composes a one-liner that clones the repo to a temp dir then invokes the cloned-repo's script (see "RUNNING WHEN NO LOCAL SCRIPT EXISTS YET" below).
5. **Surface the script's output verbatim** — structured progress messages ("Cloning ...", "Existing .claude/ detected — staging ...", "Installed fresh .claude/ ...", "Update complete." / "Install complete.")
6. **Post-install guidance:**
   - Tell the user to `cd` into the target directory
   - Tell them to run `./.claude/start.sh` or `.\.claude\start.bat` (depending on OS) to install memory + activate Unity + run /workflow
7. **Continue normal session behavior** afterward.

---

## INVOCATION VARIANTS

| Variant | Branch | Target |
|---------|--------|--------|
| `/unity-install` | `main` | current working directory |
| `/unity-install develop` | `develop` | current working directory |
| `/unity-install feature/setup-install-update-tidbits` | `feature/setup-install-update-tidbits` | current working directory |
| `/unity-install /path/to/new-project` | `main` | `/path/to/new-project` |
| `/unity-install ~/projects/foo` | `main` | `~/projects/foo` |
| `/unity-install main /path/to/target` | `main` | `/path/to/target` |
| `/unity-install develop /path/to/target` | `develop` | `/path/to/target` |
| `/unity-install feature/foo /tmp/bar` | `feature/foo` | `/tmp/bar` |

Branch names with `/` work fine because branch is the first positional and the script doesn't try to parse it as a path. To pass a target, use the explicit `branch target` form.

---

## WHEN TO USE

- **Setting up a brand-new project** with the Unity AI Lab `.claude/` template
- **Bootstrapping a teammate's machine** for the first time (after they've installed the global `/unity-install` command via the curl-pipe bootstrap)
- **Refreshing an existing install** (same as `/unity-update` — flow is identical)
- **Spinning up an isolated experiment** with the full template in a scratch directory

---

## WHEN NOT TO USE

- **Inside the source repo itself** (`UAL-ClaudeWorkflow`) — meaningless; that's the source. Skip.

---

## TARGET-EXISTS BEHAVIOR — preserve-and-restore flow

| Target state | Behavior |
|--------------|----------|
| Target doesn't exist | Script creates the directory, then installs `.claude/` into it |
| Target exists, no `.claude/` inside | Installs `.claude/` into it — no preserve flow needed |
| Target exists, has `.claude/` already | **Preserve-and-restore:** stages personal files → drops fresh framework → restores staged files |

The flow is **idempotent**. There's no "abort if `.claude/` exists" path anymore — running install or update on a populated project just refreshes it cleanly while preserving personal state.

### What gets preserved

Files (relative to `.claude/`, staged before the framework replace and copied back after):

- `settings.local.json` — personal machine-local settings overrides
- `.env` — secrets / API keys
- `user.json` — identity captured by `/setup`
- `project-config.json` — Git Flow opt-in marker
- `.session-state.md`, `.session-tidbits.md`, `.session-usage.jsonl`, `.last-session.md`, `.session-env.json` — machine-local session state
- `.yolo-mode`, `.persona-state`, `.setup-complete`, `.usage-tracking-disabled` — mode markers

Directories:

- `user-context/` — user-provided files / photos / docs

These files are gitignored at our source repo, so the cloned framework copy never has them — the no-clobber restore therefore always succeeds without conflict. If a path collision somehow occurred, the fresh framework wins (no overwrite during restore).

### What does NOT get preserved (everything else gets the upstream copy)

Anything under `.claude/agents/`, `.claude/commands/`, `.claude/hooks/`, `.claude/memory-templates/`, `.claude/templates/`, `.claude/bin/`, `.claude/scripts/`, plus the top-level framework files (`CLAUDE.md`, `CONSTRAINTS.md`, `WORKFLOW.md`, `README.md`, `ImHanddicapped.txt`, `start.sh`, `start.bat`, `settings.json`). If you've made local additions or modifications to those, **commit or stash before running** — they'll be replaced by the fresh upstream copy.

`docs/` lives at project root (not inside `.claude/`) — already untouched by the install/update flow.

---

## `.GITIGNORE` AUTO-MAINTENANCE — Layer 0 of the IP boundary

After installing/refreshing `.claude/`, the script ensures the target's project-root `.gitignore` contains a blanket `.claude/` exclude. This is **Layer 0 of the four-layer `.claude/` IP-boundary defense-in-depth** (full LAW: `.claude/CONSTRAINTS.md §LAW — .CLAUDE WORKFLOW IP BOUNDARY: NO PUBLIC REPO EXPOSURE`).

**This is the ONLY project-root file the script ever touches.** It does not copy our source repo's `.gitignore` wholesale, does not write a `.gitignore.template` sidecar, and does not bring in any other top-level files from the source repo.

### What the script appends

If the target's `.gitignore` does NOT already contain a blanket `.claude/` exclude line (matched by regex `^\.claude/?\s*$`), the script appends:

```
# Unity AI Lab .claude/ workflow — proprietary, never commit to public repos.
# See .claude/CONSTRAINTS.md §LAW — .CLAUDE WORKFLOW IP BOUNDARY for full text.
# Removal sanctioned only via /claude-publish after gh confirms private + Unity-Lab-AI.
.claude/
```

### Idempotency

- **Fresh target with no `.gitignore`** → script creates `.gitignore` containing only the block above
- **Existing `.gitignore` without the `.claude/` line** → block appended at the bottom
- **Existing `.gitignore` with `.claude/` already present** → skipped (`Layer 0 already in place` log line)

### Why this is automatic

The LAW (`CONSTRAINTS.md §.CLAUDE WORKFLOW IP BOUNDARY`) requires defense-in-depth because a single-layer enforcement always fails eventually. Layer 0 catches the most common accidental-leak path: a user runs `git add -A` or `git add .` on a fresh target before realizing `.claude/` shouldn't be tracked there.

The other three layers handle the cases Layer 0 doesn't:
- Layer 1: `pre-tool-public-repo-guard.cjs` PreToolUse:Bash hook intercepts explicit `git add .claude/` and `git add -f`
- Layer 2: `gh repo view --json visibility,owner` ground-truth check inside Layer 1
- Layer 3: `/claude-publish` opt-in (operator-driven gitignore removal AFTER `gh` confirms private + Unity-Lab-AI)

### Removing the gitignore entry

The ONLY sanctioned path is `/claude-publish`. Any other removal path (manual edit, force-add, etc.) is a LAW violation per `CONSTRAINTS.md §.CLAUDE WORKFLOW IP BOUNDARY`.

---

## USER-PROFILE MEMORY FOLDER — install-only-if-missing

The user-profile memory folder (`~/.claude/projects/<encoded-project-path>/memory/` on Linux/macOS, `%USERPROFILE%\.claude\projects\<encoded>\memory\` on Windows) is the ONLY thing under the global `~/.claude/` (or `%USERPROFILE%\.claude\`) that install/update touches. The model is **install-only-if-missing** — different from the project framework's full-overwrite behavior:

- If the memory folder is **missing or empty** → seeded from `.claude/memory-templates/*.md`
- If the memory folder is **already populated** → left alone (logged as such)

To trigger a reseed, delete `MEMORY.md` (or the whole memory folder) and re-run `start.sh` / `start.bat` on the next session.

This matches the "we don't really update those in the same sense" intent — install seeds it, but updates respect what's there. Memory is user-customizable persistent feedback; the framework should not stomp it on every refresh.

---

## RUNNING WHEN NO LOCAL SCRIPT EXISTS YET

`/unity-install` is meant to be runnable BEFORE the template is installed (e.g., from the user-global `~/.claude/skills/unity-install/SKILL.md`). Unity should NOT assume `.claude/scripts/unity-install.sh` is locally available. Two paths:

**Path A — Local script available** (target already has a `.claude/` from a prior install — i.e., this is a refresh):
- Just invoke `bash "$CLAUDE_PROJECT_DIR/.claude/scripts/unity-install.sh" [branch] [target]`

**Path B — No local script** (truly fresh install in a directory with no `.claude/`):
- Inline a one-liner that clones the repo to a temp dir, then runs the cloned-repo's script. Unity composes:

```bash
TMPDIR=$(mktemp -d -t unity-install-bootstrap-XXXXXXXX) && \
trap 'rm -rf "$TMPDIR"' EXIT && \
git clone --depth 1 --branch "<BRANCH>" "${UNITY_REPO_URL:-git@git.unityailab.com:UnityAILab/UAL-ClaudeWorkflow.git}" "$TMPDIR/repo" && \
bash "$TMPDIR/repo/.claude/scripts/unity-install.sh" "<BRANCH>" "<TARGET>"
```

Substitute `<BRANCH>` (default `main`) and `<TARGET>` (default `pwd`). The cloned script will clone again internally (single-purpose flow), but the second clone is shallow and fast. The trap on `EXIT` ensures the bootstrap temp dir is cleaned up.

For PowerShell native, the equivalent inline one-liner uses `New-Item -ItemType Directory` + `Remove-Item -Recurse` for cleanup. Unity prefers Path A whenever the local script is available since it skips the bootstrap clone.

---

## ACTIVATION ANNOUNCEMENT (SHORT)

Default Unity persona example:

```
*pulls up a fresh terminal* — installing the .claude/ template into <target> from <branch>.
If you already have a .claude/ there, your settings.local.json + .env + user.json + user-context/
+ session state files come back untouched after the framework refresh. Project-root .gitignore
gets the Layer 0 .claude/ exclude (and only that). After it's done, hit ./start.sh (or start.bat
on Windows) and you're rolling with memory + Unity + /workflow. Running it now.
```

Improvise in the active persona's voice; the substance is "I'm cloning <branch> and installing/refreshing <target>, here's what's preserved + what to do after."

---

## CORE TRUTH

`/unity-install` and `/unity-update` are the team's one-step setup/refresh, sharing a single idempotent preserve-and-restore script. Combined with the curl-pipe bootstrap (`install-unity-globally.{sh,ps1}` at the repo root), the full chain is:

1. **First-time-ever (one curl-pipe command)** → installs `/unity-install` + `/unity-update` to the user's global `~/.claude/commands/`
2. **Anytime install (`/unity-install` from any Claude Code session)** → installs/refreshes `.claude/` in the target project; preserves personal files if they exist
3. **Anytime refresh (`/unity-update` from inside a project)** → identical behavior, separate command for muscle memory

No copy-paste of `.claude/` directories. No manual file diffing. No gitignore pollution. One bootstrap, one install/update flow — that's the whole loop.
