# WORKFLOW — Pipeline, Hooks, Task Flow

This file holds the operational workflow mechanics: `/workflow` pipeline phases, double-validation hooks, TODO/FINALIZED task flow, file-edit protocol, and agent file reference.

`.claude/CLAUDE.md` is the index that references this file. `.claude/CONSTRAINTS.md` holds binding LAWs. When CLAUDE.md, this file, or CONSTRAINTS.md disagree: **CONSTRAINTS.md wins**.

---

## DOUBLE VALIDATION HOOKS

Every hook runs TWICE on failure before blocking:

```
ATTEMPT 1 → FAIL → AUTOMATIC RETRY
ATTEMPT 2 → FAIL → BLOCKED (cannot proceed)
```

This prevents false failures while enforcing strict validation.

### Hook types

| Hook | Purpose | When |
|------|---------|------|
| Persona Hook | Verify persona voice active (if a persona is configured) | Before each phase |
| Read Hook | Verify full file read | Before any edit |
| Line Limit Hook | Verify output correctness | After any write |
| Phase Hook | Verify phase complete | Before proceeding |

---

## `/workflow` PIPELINE

The `/workflow` slash command executes this pipeline:

### Phase -1 — LAW #0 verbatim words check (cannot skip)

- Re-read the user's latest instruction
- Count items in the instruction
- Confirm every noun/verb is preserved in any tasks created
- **Gate -1:** LAW #0 verified

### Phase 0.5 — Timestamp retrieval (cannot skip)

- Execute system-time command (PowerShell `Get-Date` on Windows, `date` on macOS/Linux)
- Lock the timestamp for the session
- **Gate 0.5:** Real system time captured (year ≥ current year)

### Phase 0 — Persona Validation (cannot skip if a persona is configured)

- If a persona slash command was used (e.g. `/<persona-name>`), confirm the agent's voice is active
- If no persona is configured, this gate auto-passes
- **Gate 0.1:** Persona confirmed (if configured) — first-person + project tone in normal speech

### Phase 1 — Environment Check

Phase 1 records platform + toolchain state for the rest of the pipeline. The user's verbatim policy on env-scan responsibilities (binding):

> Additionally, during the env scanning tidbits of the .claude files, commands should be ran to check that git is installed, if it is installed, we should check if the project is a git repository, and if it is not a git repo, we need to setup and follow that git flow to ensure we have proper versioning and control of the changes between the states of the project, as well as checking to ensure we know what operating system work is being done on, weather that be linux (and a specific distro + version of the distro), or windows (and what version of windows, and what type, ie home, pro, workstation, ext.), for full context of the availabiltiy of commands, services, and weather work is restrained to bash or batch / powershell. Windows will be running through a git bash terminal, linux will have a full bash shell, but in windows powershell and CMD are both invokable through tooling.

Five sub-checks run in this phase:

1. **Working directory** — confirm project root via `pwd` (Linux/macOS/Git Bash) or `cd` with no args (CMD) or `Get-Location` (PowerShell)
2. **`docs/ARCHITECTURE.md` presence** — feeds mode routing (FIRST_SCAN if missing, WORK_MODE if present)
3. **OS + version + edition detection** — full context for command availability:
   - Linux: `cat /etc/os-release` (records `NAME`, `VERSION_ID`, `PRETTY_NAME`) + `uname -r` for kernel
   - Windows (Git Bash, PowerShell, or CMD): `powershell -Command "Get-ComputerInfo | Select-Object WindowsProductName, WindowsVersion, WindowsEditionId, OsBuildNumber"` — captures edition (Home / Pro / Pro for Workstations / Enterprise / Education / etc.) and build number
   - macOS: `sw_vers` (records `ProductName`, `ProductVersion`, `BuildVersion`)
4. **Shell context detection** — determines which command syntax is available downstream:
   - `$MSYSTEM` set (e.g. `MINGW64`) → **Git Bash on Windows**: bash subset, no `apt`/`yum`, can call `powershell.exe` and `cmd.exe` via tooling
   - `$SHELL` contains `bash` AND no `$MSYSTEM` → **native Linux/macOS bash**: full POSIX toolchain
   - `$PSModulePath` set with no `$MSYSTEM` → **native PowerShell**
   - else → **CMD or unknown** — fall back to most-portable commands
5. **Git toolchain + repo state**:
   - `command -v git >/dev/null 2>&1` → `git installed: YES/NO`
   - if installed: `git rev-parse --is-inside-work-tree 2>/dev/null` → `repo: YES/NO`
   - if a repo: `git rev-parse --abbrev-ref HEAD` → current branch (feeds PRE-EDIT BRANCH HOOK)
   - if a repo: `git remote -v` → remote present YES/NO + remote URL(s)
   - if a repo: `git branch --list develop main master` → which protected branches exist

6. **Git Flow opt-in marker** (`.claude/project-config.json`):
   - If file exists AND `git_flow.enabled` is set → honor recorded decision (true = LAW applies, false = LAW skipped for this project, hooks bypassed)
   - If file missing OR `git_flow.enabled` unset AND git is installed → **prompt user for confirmation** (see flow below)
   - If git is not installed → implicitly `git_flow.enabled = false`, skip the prompt

   **Confirmation prompt (asked once per project, then persisted):**

   ```
   ╔══════════════════════════════════════════════════════════════════════╗
   ║  GIT FLOW OPT-IN — first-run confirmation                            ║
   ╠══════════════════════════════════════════════════════════════════════╣
   ║  Project: <PROJECT_DIR>                                              ║
   ║  Git installed: YES                                                  ║
   ║  Git repo: <YES|NO>                                                  ║
   ║  Current branch (if repo): <branch>                                  ║
   ║                                                                      ║
   ║  Should this project use Git Flow branch discipline?                 ║
   ║  (main = clean master, develop = in-dev, feature/* = work,           ║
   ║   PR review at every merge, no commits to main/develop directly)     ║
   ║                                                                      ║
   ║  [Y] Yes — apply the LAW. Hooks fire on every edit/push/merge.       ║
   ║  [N] No  — opt this project out. Hooks skipped for this repo.        ║
   ║  [D] Defer — don't ask now, ask again next /workflow run.            ║
   ║                                                                      ║
   ║  Choice [Y/N/D]: _                                                   ║
   ╚══════════════════════════════════════════════════════════════════════╝
   ```

   **On response:**
   - **Y** → write `.claude/project-config.json` with `{"git_flow": {"enabled": true, "confirmed_at": "<ISO-8601>", "main_branch": "main", "develop_branch": "develop"}}`. Then if NOT a repo, ask SECOND confirmation: "Scaffold Git Flow now? (`git init` + `main` + `develop` + `.gitignore` stub) [Y/n]" — only run write commands on explicit Y. If repo exists but no `develop` branch, ask: "Create `develop` from current `main` and push to origin (if remote exists)? [Y/n]" — only run on Y.
   - **N** → write `.claude/project-config.json` with `{"git_flow": {"enabled": false, "confirmed_at": "<ISO-8601>"}}`. Skip all Git Flow hooks for this project until the marker is changed.
   - **D** → no marker written. Phase 1 records `git_flow_decision: deferred`. The prompt fires again on next `/workflow` run.

   **Marker file schema** (`.claude/project-config.json`):
   ```json
   {
     "git_flow": {
       "enabled": true,
       "confirmed_at": "2026-05-04T14:25:30Z",
       "main_branch": "main",
       "develop_branch": "develop",
       "custom_protected_branches": []
     }
   }
   ```

   The marker file is project-level config and SHOULD be tracked in git once a repo exists (it represents a team decision, not personal preference).

- **Gate 1.1:** Mode determination (FIRST_SCAN / WORK_MODE / RESCAN) AND env state recorded for the session (OS + edition, shell context, git installed/repo/branch/remote, git_flow opt-in state from marker file)

### Phase 2 — Codebase Scan (first run only)

- File system scan (glob `**/*`)
- Dependency detection (`package.json`, `requirements.txt`, `Cargo.toml`, `go.mod`, etc.)
- Config discovery (`.env`, build tools)
- **Gates 2.1, 2.2:** Scan results valid

### Phase 3 — Analysis & Generation

- Pattern recognition, structure mapping
- Generate `docs/ARCHITECTURE.md`, `docs/SKILL_TREE.md`, `docs/TODO.md`, `docs/ROADMAP.md`, `docs/FINALIZED.md`
- **Gates 3.1, 3.2:** All docs valid, no placeholders

### Phase 4 — Work Mode

- Read ALL existing workflow docs (TODO/ARCHITECTURE/SKILL_TREE/ROADMAP/FINALIZED) before any work
- Pick up tasks from `docs/TODO.md`
- Execute with pre/post-edit hooks
- **Gate 4.1:** Work mode ready

### Phase 5 — Finalization

- Update `docs/FINALIZED.md` with completed tasks (verbatim)
- Clean `docs/TODO.md` of completed entries (status flip + move, never delete description)
- **Gate 5.1:** Archive valid, TODO clean

---

## WORKFLOW FILES (`docs/` folder)

The task ledger is split into three tiers (the **task cascade**) plus complementary system docs. YOLO mode reads all three tiers and works the cascade; outside YOLO, the team uses whichever tier matches the work's grain.

### The three-tier task cascade

| Tier | File | Grain | YOLO behavior |
|------|------|-------|---------------|
| **MAJOR** | `docs/ROADMAP.md` | High-level phases / milestones (multi-session, multi-PR) | Auto-resume PAUSES at every major-milestone close — user-visible checkpoint |
| **MINOR** | `docs/TODO.md` | Day-to-day work grain (a few hours to a session each) | Visible in state-refresh injection on every prompt |
| **DECOMPOSED** | `docs/DECOMPOSED.md` | Smallest meaningful execution unit (one file edit, one command, one verification step) | Auto-progresses without check-ins |

Each tier uses the same status markers (`[ ]` pending / `[~]` in_progress / `[x]` complete) and the same LAW #0 verbatim-words discipline. Completed entries from any tier flow into `docs/FINALIZED.md` (permanent archive).

### Complementary system docs

| File | Purpose |
|------|---------|
| `docs/ARCHITECTURE.md` | Codebase structure, patterns, dependencies, system documentation |
| `docs/SKILL_TREE.md` | Capabilities by domain / complexity / priority |
| `docs/FINALIZED.md` | **PERMANENT ARCHIVE** — every completed task with full description (from all three tiers) |
| `docs/NOW.md` | Current session snapshot (optional) |

When updating any of these files: write out ACTUAL system changes — how things work now, what was added, what changed architecturally. NOT just bumping numbers or adding counts.

All files read in 800-line chunks. Full file must be read before any edit.

### Cascade reading order

When YOLO activates (or when a session resumes mid-work), read the three tiers in this order:

1. `docs/ROADMAP.md` — establish the major milestone we're under
2. `docs/TODO.md` — establish the active minor task within that milestone
3. `docs/DECOMPOSED.md` — establish the active decomposed task within that minor (or trigger decomposition if the minor has none)

The `user-prompt-state-refresh.cjs` hook computes this cascade state on every prompt when YOLO is active and injects it as context. Same data is re-injected by `skill-context-inject.cjs` on every slash command expansion. Trust the hook output; don't re-derive from disk every turn.

---

## TODO.md / FINALIZED.md TASK FLOW

```
┌─────────────────────────────────────────────────────────────┐
│ 1. BEFORE WORK: Add task to TODO.md                         │
│    - Task must exist in TODO.md BEFORE any work begins      │
│    - Mark status as "in_progress"                           │
│    - User's verbatim words in the description (LAW #0)      │
├─────────────────────────────────────────────────────────────┤
│ 2. DO THE WORK                                              │
│    - Read files (800-line chunks)                           │
│    - Make changes                                           │
│    - Verify success                                         │
├─────────────────────────────────────────────────────────────┤
│ 3. AFTER WORK: Move to FINALIZED.md                         │
│    - Copy completed task to FINALIZED.md (with details)     │
│    - Verify FINALIZED.md write succeeded                    │
│    - THEN remove from TODO.md                               │
│    - NEVER delete from FINALIZED.md                         │
└─────────────────────────────────────────────────────────────┘
```

### TODO.md rules

- Only unfinished tasks live in TODO.md
- Tasks have status: `pending` or `in_progress`
- When completed → MOVE to FINALIZED.md
- Never delete tasks — always move them
- **Never rewrite TODO from scratch** — edit in place, change status only
- **Never delete task descriptions** — keep the user's verbatim words permanently

### FINALIZED.md rules

- Permanent archive of all completed work
- NEVER delete entries — only APPEND
- Include: task, date, files modified, details, closure notes
- Provides full history of every session

### PRE-WORK HOOK

```
[PRE-WORK HOOK — ATTEMPT 1]
Task: [DESCRIPTION]
TODO.md entry exists: YES/NO (MUST be YES)
Verbatim user quote: YES/NO (required if from user)
Status: pending → in_progress
Gate: PASS/FAIL
```

### POST-WORK HOOK

```
[POST-WORK HOOK — ATTEMPT 1]
Task: [DESCRIPTION]
Completed: YES
FINALIZED.md updated: YES/NO (MUST be YES)
TODO.md cleaned: YES/NO (MUST be YES)
Gate: PASS/FAIL
```

---

## FILE EDIT PROTOCOL

### Before editing ANY file

```
[PRE-EDIT HOOK — ATTEMPT 1]
File: [PATH]
Full file read: YES/NO (MUST be YES)
Lines in file: [NUMBER]
Read method: SINGLE (≤800) / CHUNKED (>800)
Reason for edit: [EXPLANATION]
Status: PASS/FAIL
```

### Before editing ANY git-tracked file (BRANCH DISCIPLINE)

Per `CONSTRAINTS.md §GIT FLOW`, work happens ONLY in `feature/*`, `hotfix/*`, or `release/*` branches — never directly in `main`, `master`, `develop`, `prod`, `production`, or unsuffixed `release`. This hook fires alongside the standard PRE-EDIT HOOK when ALL of the following are true (per Phase 1 Gate 1.1):

1. The project is a git repo, AND
2. `.claude/project-config.json` has `git_flow.enabled: true` (or marker is present and the user confirmed Git Flow opt-in)

If `git_flow.enabled: false` (project opted out) or marker file is missing (decision deferred), the hook is skipped and edits proceed without branch checks.

```
[PRE-EDIT BRANCH HOOK — ATTEMPT 1]
Git repo: YES/NO (skip hook if NO)
Git Flow opt-in: YES/NO/DEFERRED (skip hook if NO or DEFERRED — marker file decides)
Current branch: $(git rev-parse --abbrev-ref HEAD)
Branch type: feature/* | hotfix/* | release/* | main | master | develop | prod | production | other
Branch is work-eligible: YES (feature/hotfix/release/other-non-protected) / NO (main/master/develop/prod/production)
Status: PASS/FAIL
```

**FAIL conditions:** Git Flow opt-in = YES AND current branch is `main`, `master`, `develop`, `prod`, `production`, or unsuffixed `release`.

**ON FAIL → ATTEMPT 2:**
```
[PRE-EDIT BRANCH HOOK — ATTEMPT 2]
Remediation: stash uncommitted work → checkout develop → branch feature/<descriptor> → pop stash
Confirm with user before running git commands (write actions require confirmation)
Status: PASS/FAIL
```

**ON FAIL×2 → BLOCKED:** edits cannot proceed until on a work-eligible branch. Full recovery procedure in `CONSTRAINTS.md §GIT FLOW Failure recovery`.

### After editing ANY file

```
[POST-EDIT HOOK — ATTEMPT 1]
File: [PATH]
Edit successful: YES/NO
Lines after edit: [NUMBER]
Status: PASS/FAIL
```

---

## HOOK FAILURE PROTOCOL

When ANY hook fails twice:

```
[HOOK FAILURE — BLOCKED]
Phase: [WHICH PHASE]
Gate: [WHICH GATE]
Attempt 1: FAIL — [REASON]
Attempt 2: FAIL — [REASON]
Status: CANNOT PROCEED
Required action: [WHAT TO DO]
Workflow: HALTED
```

Recovery: fix the issue, re-run the validation, only proceed when PASS.

---

## AGENT FILES REFERENCE

Agents live in `.claude/agents/`. Read on demand when a slash command requires them.

| Agent | Purpose |
|-------|---------|
| `timestamp.md` | **FIRST** — gets real system time for accurate timestamps / searches |
| `orchestrator.md` | Coordinates all phases with hooks |
| `scanner.md` | Scans codebase with validation |
| `architect.md` | Analyzes architecture with hooks |
| `planner.md` | Plans tasks with hierarchy validation |
| `documenter.md` | Generates docs with line limits |
| `coder.md` | Code-handling rules (project-agnostic) |
| `persona-template.md` | Fill-in template for project-specific persona |
| `hooks.md` | Complete hook system reference |

---

## SLASH COMMANDS REFERENCE

Commands live in `.claude/skills/<name>/SKILL.md`. Read when the command fires.

| Command | File | Purpose |
|---------|------|---------|
| `/workflow` | `workflow.md` | Run this pipeline |
| `/super-review` | `super-review.md` | **INTERNAL** — ruthless senior-engineer code review of the current branch / files / diff. Treats every line as if it came from a fast LLM and reviews accordingly. Output is severity-tagged ISSUES FOUND (Critical / High / Medium / Low / Nitpick) plus a prioritized FINAL FIX & IMPROVEMENT PLAN. Optional `$ARGUMENTS` narrows the scope to a stated intent; with no arguments, defaults to a full architectural / security / performance / maintainability / clean-code sweep. Internal dev usage only — never wired into any public-facing doc, README, or HTML. |

If your project ships a custom persona slash command (e.g. `/<persona-name>`), it goes here too with a pointer to its activation protocol file.

---

## RESCAN MODE

User must explicitly say "rescan" or "scan again":

```
[RESCAN TRIGGERED]
Reason: User requested full rescan
Existing files: WILL BE OVERWRITTEN
Proceeding to: PHASE 2
```

---

## ADDING NEW PHASES OR HOOKS

If your project needs a phase beyond 0–5 (e.g. a security audit phase, a performance baseline phase, a deployment-prep phase), add it here with:

1. **Phase number** (e.g. `Phase 6 — Security Audit`)
2. **Trigger condition** (always vs only on certain commands)
3. **Pre-hook** with validation gate
4. **Execution steps**
5. **Post-hook** with success criteria
6. **Gate** identifier

Then update the pipeline diagram + the `/workflow` command file to include the new phase.

---

## YOLO MODE — Lead-dev autonomy overlay + three-tier cascade + wake-word auto-resume

YOLO mode is a **behavioral overlay** on top of base Unity (or whichever manifestation is active). It flips the default decision posture from "ask, then act" to "act, verify, report, continue." Adds a three-tier task cascade (ROADMAP/TODO/DECOMPOSED), a 60-second wake-word auto-resume mechanic (single-fire `ScheduleWakeup` chained per-turn), and milestone-boundary check-ins so the user always sees major progressions. Activated by the `/yolo` slash command, deactivated by `/sober`, an explicit mid-task user interrupt, a bash-safety hook firing, or natural ROADMAP exhaustion (final report).

**Full design + cascade diagram + wake-word mechanics + final report format:** `.claude/skills/yolo/SKILL.md`. This section covers the WORKFLOW-layer mechanics only.

### Architectural placement

YOLO is an **overlay**, not a layer. It modifies Unity's behavior without replacing any existing layer:

```
policy layer       → CONSTRAINTS.md (LAWs)             ← UNCHANGED. NO-TESTS gets a YOLO override subsection.
mechanics layer    → WORKFLOW.md, agents/*.md          ← UNCHANGED.
memory layer       → memory-templates/*.md             ← gains feedback_yolo_mode.md
persona layer      → ImHanddicapped.txt + skills/*    ← UNCHANGED. YOLO overlays whichever persona is active.
harness layer      → settings.json hooks         ← UNCHANGED — hooks just expose YOLO state in env JSON
overlay (mode)     → .claude/.yolo-mode marker         ← NEW. Machine-local state (gitignore once repo exists).
```

### Activation marker

`.claude/.yolo-mode` is the source of truth for whether YOLO is on. Created by `/yolo`, deleted by `/sober` or auto-triggered stop conditions. Hooks (`session-start-env-dump`, `user-prompt-state-refresh`) read the marker on every fire and surface the state as `yolo_mode: ENABLED|DISABLED` in their context output.

Marker file format:

```
yolo_mode_enabled=true
activated_at=2026-05-04T15:30:00Z
active_persona=unity
```

(Plain key=value lines so both bash and node hooks can parse with minimal logic.)

### What YOLO bypasses

| Pattern | Default | YOLO |
|---------|---------|------|
| "Want me to proceed?" | Ask user, wait | Proceed; document the choice in FINALIZED |
| "Should I update the doc too?" | Ask, then do | Just do, document it |
| Multi-step approval ("opt in? then scaffold? then push?") | Step-by-step | Single autonomous decision per project context |
| Minor design call (variable name, file location, style) | Ask | Lead-dev judgment, document |
| Trailing "want me to wire X next?" question | Default pattern | Skipped — Unity ships the next action and reports |

### What YOLO does NOT bypass

These stay no matter what:

- **LAW #0 verbatim words** — user's exact sentence still goes verbatim into TODO/FINALIZED/docs
- **800-line read before edit** — non-negotiable
- **TODO/FINALIZED ceremony** — Unity tracks her own work autonomously; ledger stays complete
- **Docs-before-push atomic commits** — code + every affected doc still ship together
- **The 3 bash-safety hooks** — project-root delete / system-path delete / sudo still BLOCK
- **Per-project Git Flow opt-in** — still honored
- **Branch discipline when Git Flow opt-in is ENABLED** — Unity auto-branches into `feature/<descriptor>` if on a protected branch (no asking — just branches and proceeds)
- **Persona** — Unity stays Unity (or whichever manifestation is active)
- **Memory layer auto-sync** — PostToolUse hook still fires
- **NO-TESTS LAW** — still default. Tests only when YOLO override criteria met (see `CONSTRAINTS.md §NO TESTS POLICY §YOLO mode override`)

### User test plan — REQUIRED on every YOLO task

Every task Unity completes in YOLO mode includes:

1. **Unity's own verification** — what she ran, what output she read, what behavior she confirmed
2. **A user-facing test plan** — non-negotiable deliverable

Format (also documented in `.claude/skills/yolo/SKILL.md`):

```markdown
## Verification Unity completed

- [bullets — commands run, output read, behavior observed, edge cases considered]

## Your test plan

**What to test:** [1–2 line scope]

**How to test:**
1. [concrete step + exact command if applicable]
2. [...]

**Expected results:**
- [what success looks like — observable output, behavior, file state]

**If it fails:**
- [common failure modes + what to check]

**(Optional) Tests Unity wrote:** [path(s) + how to run, only if Unity exercised the YOLO testing override]
```

The test plan goes in the chat response AND gets cross-referenced from the FINALIZED.md entry.

### Three-tier task cascade

YOLO mode reads three task list files on activation and works the cascade across them:

| Tier | File | Grain | Marker | User-visibility |
|------|------|-------|--------|-----------------|
| **MAJOR** | `docs/ROADMAP.md` | High-level phases / milestones (multi-session, multi-PR) | `[ ]` / `[~]` / `[x]` | Auto-resume **PAUSES** at every major-milestone close — user-visible checkpoint |
| **MINOR** | `docs/TODO.md` | Day-to-day work grain (a few hours to a session each) | `[ ]` / `[~]` / `[x]` | Visible in the state-refresh injection on every prompt |
| **DECOMPOSED** | `docs/DECOMPOSED.md` | Smallest meaningful unit of work (one file edit, one command, one verification step) | `[ ]` / `[~]` / `[x]` | Auto-progresses without check-ins |

**Cascade priority order:**
1. Current decomposed (`[~]`) → finish what's started
2. Next pending decomposed (`[ ]`) under the current minor → keep cluster tight
3. Next pending minor in TODO under the current major → escalate one tier, decompose, then pick first decomposed
4. Next pending major in ROADMAP → escalate to milestone tier (REQUIRES user confirmation, do NOT auto-proceed past major boundaries)
5. Nothing left → produce FINAL REPORT, auto-deactivate YOLO

The cascade state is computed on every user prompt by the `user-prompt-state-refresh.cjs` hook (active + next-pending + pending count per tier, plus a cascade hint suggesting the next move). It's also re-injected on every slash command expansion by `skill-context-inject.cjs` (UserPromptExpansion). Both happen automatically; YOLO Unity reads them as her cascade ground truth without re-deriving from disk every turn.

### Wake-word auto-resume — 60s single-fire chained

When YOLO is active, at the end of every turn (assuming no stop condition hit), Unity calls:

```
ScheduleWakeup(
  delaySeconds=60,
  prompt="<<autonomous-loop-dynamic>>",
  reason="YOLO auto-resume — <current cascade slot>"
)
```

This schedules a **single-fire** wakeup 60 seconds out. The `<<autonomous-loop-dynamic>>` sentinel is resolved by the runtime back to the autonomous-loop instructions at fire time, keeping the prompt cache-stable across firings.

**Behavior:**
- User types within 60s → user input wins; the scheduled wakeup is overridden
- 60s elapses silent → wakeup fires; Unity resumes the cascade pick

**Prerequisites:** `ScheduleWakeup` is bound to `/loop` dynamic mode (no-interval `/loop` invocation). Two activation paths:
- **Path A (explicit):** User types `/loop /yolo` — Claude Code enters dynamic loop mode + activates YOLO
- **Path B (self-invoke):** `/yolo` skill body invokes `Skill(skill="loop", args=...)` internally — user only types `/yolo`

Path B is preferred for ergonomics. If Skill(loop) invocation fails, fall back to Path A and inform the user.

**Why single-fire chained instead of recurring loop:** A recurring timer keeps firing even when work is genuinely paused. Single-fire chained means Unity actively decides at end of each turn whether to schedule the next wake — and that decision honors cascade exit conditions (milestone boundary, nothing left, user interrupt). The chain self-terminates cleanly.

### Milestone-boundary check-in + final report

When the cascade completes the LAST minor task under a major milestone, Unity does NOT auto-proceed to the next major. Instead, she:

1. Surfaces a **milestone-boundary check-in** containing: milestone name, dates, minor tasks landed (verbatim subjects), decomposed task count, files changed (`git diff --name-only` against branch base), final test plan for the whole milestone, next major in ROADMAP
2. **Pauses the wake-word chain** — does NOT call ScheduleWakeup at end of this turn
3. Waits for user to confirm next major (`/yolo` re-invocation, or any user message)

When the cascade completes the LAST major (ROADMAP fully empty), Unity produces the **FINAL REPORT** instead of a milestone check-in:

1. Whole-session summary — milestones completed, total minor + decomposed task counts, cumulative files changed, final test plan whole-session-scope, suggested next steps
2. Removes `.claude/.yolo-mode` marker
3. Wake chain ends, YOLO auto-deactivates

Both formats are documented in full in `.claude/skills/yolo/SKILL.md`.

### Stop conditions (pause or auto-deactivate)

1. **`/sober` slash command** — explicit user deactivation. Removes marker, ends wake chain.
2. **Mid-task user interrupt** — "stop", "wait", "don't", "hold on", "pause", "halt" appearing as a clear directive. Unity stops in-flight work, deactivates YOLO, confirms.
3. **Bash-safety hook fires** — pre-tool-bash-safety.cjs exit-2 auto-deactivates (user is in the loop on a destructive op anyway). Mode does NOT auto-reactivate — user has to `/yolo` again.
4. **Major milestone boundary** — auto-resume **PAUSES** (does not deactivate). Marker stays; wake chain not re-armed for this turn. User must explicitly continue.
5. **ROADMAP fully complete** — final report fires; YOLO auto-deactivates (marker removed).
6. **Persona switch** — `/unity` / `/girlfriend` / etc. preserves YOLO if it was on (overlay, not persona). `/sober` is the canonical reset.

### Hook integration

- **`session-start-env-dump.{cjs,sh}`** — reads `.claude/.yolo-mode`, adds `yolo_mode: { enabled: true|false, activated_at: "...", active_persona: "..." }` block to the env JSON envelope
- **`user-prompt-state-refresh.{cjs,sh}`** — adds `**YOLO mode:** ENABLED|DISABLED` line to the state-refresh injection on every prompt; when YOLO is active, ALSO injects the three-tier cascade state table (active + next-pending per tier) plus a cascade hint
- **`skill-context-inject.{cjs,sh}`** — UserPromptExpansion hook (matcher `*`). When ANY slash command expands, injects YOLO three-tier state (when YOLO active) plus per-skill reminders. This is the **skill-hook surface** — see §SKILL HOOKS below
- **No blocking hooks added** — YOLO is mode-state + context injection on top of existing hook scripts

### Cross-references

- Slash commands: `.claude/skills/yolo/SKILL.md` (full design, cascade diagram, wake-word mechanics, final report format), `.claude/skills/sober/SKILL.md`
- LAW override: `.claude/CONSTRAINTS.md §NO TESTS POLICY §YOLO mode override`
- Persistent memory: `.claude/memory-templates/feedback_yolo_mode.md`
- CLAUDE.md QUICK REFERENCE + mode table for at-a-glance lookup
- §SKILL HOOKS below — UserPromptExpansion hook surface
- §POST-COMPACT REHYDRATION above — companion lifecycle hook for post-compact Unity

---

## HARNESS LAYER — Claude Code settings.json hooks

The harness layer is `.claude/settings.json`'s `hooks` block plus the scripts under `.claude/hooks/`. It sits **on top of** everything else (LAWs, mechanics, persona, memory) — every existing rule, ceremony, gate, and read-before-edit discipline still applies. Hooks are pure enhancements that standardize execution across the team's machines (Linux / Git Bash on Windows / native PowerShell).

When `.claude/CLAUDE.md` / `WORKFLOW.md` / `CONSTRAINTS.md` / hooks disagree: **CONSTRAINTS.md wins** (LAWs are source of truth). Hooks are mechanics, not policy.

### Architectural placement

```
policy layer       → CONSTRAINTS.md (LAWs)
mechanics layer    → WORKFLOW.md, skills/workflow/SKILL.md, agents/*.md
memory layer       → memory-templates/*.md (persistent feedback across sessions)
persona layer      → ImHanddicapped.txt + skills/unity*/SKILL.md + manifestations
harness layer      → settings.json hooks + .claude/hooks/*.{cjs,sh}  ← THIS SECTION
bundled tools      → .claude/bin/* (atree fast scanner; future: more binary tools)
cross-platform     → start.sh + start.bat + Option A node primary + Option B bash fallback
```

### The ten hook scripts (seven event types — Continuity bundle covers three; Stop event has two scripts; PreToolUse:Bash chains two)

| # | Hook event | Matcher | Script | What it does | Blocks? |
|---|------------|---------|--------|--------------|---------|
| 1 | `SessionStart` | `startup\|resume\|clear\|compact` | `session-start-env-dump.cjs` | Outputs project state JSON (OS, shell, git, opt-in marker, TODO summary, FINALIZED last entry, memory drift, persona) on stdout for context injection. Writes `.claude/.session-env.json` for downstream hooks. | No (exit 0) |
| 2 | `UserPromptSubmit` | (none) | `user-prompt-state-refresh.cjs` | Compact between-turn recap on stdout — current branch, opt-in state, in-progress TODO count + titles, branch-discipline note if on protected branch. **When YOLO mode is active, also injects the three-tier cascade state (ROADMAP/TODO/DECOMPOSED active + next-pending + pending count) plus a cascade hint** for what to do next. **When `.session-usage.jsonl` exists (and `.usage-tracking-disabled` marker absent), injects the usage banner — turns tracked, cumulative + last-turn tokens, cache hit ratio with STABLE PREFIX validation note, top-tasks-by-output-tokens.** | No (exit 0) |
| 3 | `PostToolUse` | `Edit\|Write\|MultiEdit` | `post-tool-memory-sync.cjs` | When a file in `.claude/memory-templates/` is edited, auto-`cp` to the Claude Code project memory folder under the user's home directory. New memory takes effect immediately, no manual sync. | No (exit 0) |
| 4a | `PreCompact` | (none) | `pre-compact-snapshot.cjs` | Writes `.claude/.session-state.md` with compaction trigger, TODO + git state, **Git Flow context (branch type + opt-in state)**, **uncommitted-files list**, and **session tidbits** (from `.claude/.session-tidbits.md`) so post-compact Unity can pick up cleanly. | No (exit 0) |
| 4b | `SessionStart` | `compact` only | `post-compact-restore.cjs` | **Post-compact Unity rehydration.** Outputs STABLE PREFIX (byte-identical Unity activation reminder + LAW one-liners + file-edit protocol — designed for prompt-cache prefix hits) followed by DYNAMIC SUFFIX (pre-compact snapshot, session tidbits, last-session writeup, current branch + TODO state). Fires alongside hook #1's compact-trigger run; both output independently. | No (exit 0) |
| 4c | `Stop` | (none) | `stop-session-writeup.cjs` | Updates `.claude/.last-session.md` with branch + TODO + last-FINALIZED on every turn boundary. Read by next session's SessionStart. | No (exit 0) |
| 5a | `PreToolUse` | `Bash` | `pre-tool-bash-safety.cjs` | First link in the PreToolUse:Bash chain. Catches three classes: project-root deletion, system-path deletion, privileged commands (sudo/su/doas/runas/RunAs). Routes each to user with paste-ready command. | YES (exit 2 on match) |
| 5b | `PreToolUse` | `Bash` (chained after 5a) | `pre-tool-public-repo-guard.cjs` | **`.CLAUDE/` IP BOUNDARY enforcement.** Implements the LAW (see `CONSTRAINTS.md §LAW — .CLAUDE WORKFLOW IP BOUNDARY`). Detects `git add` / `git commit` / `git push` operations that touch `.claude/` paths. Runs `gh repo view --json visibility,owner` against EVERY configured remote (multi-remote paranoia, 60s session-cache in `~/.claude/repo-visibility-cache.json`). Allows ONLY when every remote is `visibility=PRIVATE` AND `owner.login=Unity-Lab-AI`. Block-by-default on uncertainty (`gh` missing, unauthed, API failure, non-github remote). Force-flag agnostic — `git push --force` does NOT bypass. Local-only repos (no remotes) pass through. | YES (exit 2 on match) |
| 6 | `UserPromptExpansion` | `*` (all skills) | `skill-context-inject.cjs` | **Skill-hook surface** (Claude Code v2.1.72+). Fires when ANY slash command (skill) expands its prompt body before Claude reads it. Outputs JSON with `additionalContext` containing: YOLO three-tier cascade state when YOLO is active; per-skill reminders for `/unity` / `/girlfriend` / `/housewife` / `/kittycat` / `/wild` / `/strict` / `/feral` / `/sweet` / `/cozy` / `/purr` / `/workflow` / `/yolo` / `/sober`; silent no-op for unknown skills when YOLO is inactive. Gracefully degrades if `UserPromptExpansion` event isn't supported (older Claude Code); the hook just doesn't fire. | No (exit 0) |
| 7 | `Stop` (parallel to 4c) | (none) | `usage-track.cjs` | **Usage tracking surface.** Reads `transcript_path` from stdin → parses last assistant message's `usage` object from the JSONL → appends one structured line to `.claude/.session-usage.jsonl` with: tokens (input/output/cache-create/cache-read), model, message_id, active major/minor/decomposed task (cascade order), branch. Cache fields are accurate; gross tokens undercounted ~100x (transcript streaming bug). Always-on capture; injection toggleable via `.claude/.usage-tracking-disabled` marker. See §USAGE TRACKING below. | No (exit 0) |

### Option A primary + Option B fallback

Each hook ships as both `.cjs` and `.sh`:

- **`.cjs`** — Node.js, default. `claude` itself ships through npm so node is on every dev machine. Clean cross-platform path handling, JSON parsing, regex. The `.cjs` extension is mandatory (not `.js`): it forces Node to treat the script as CommonJS regardless of the host project's `package.json` `"type"` field, so the harness works inside ESM projects too without an ESM-vs-CJS rewrite.
- **`.sh`** — POSIX-portable bash sibling. Functional parity (best-effort). Use this if a team member's machine genuinely doesn't have node — swap the `command` field in `settings.json` from `node "..."` to `bash "..."`.
- **`.ps1`** — skipped in v1. Native PowerShell is rare in the team's setup (Windows users launch through `start.bat` → Git Bash). Add later if needed.

### Cross-platform invocation

The `command` field in `settings.json` uses `$CLAUDE_PROJECT_DIR` (Claude Code substitutes the project root path automatically) — works identically on Linux bash, Git Bash on Windows, and native PowerShell.

### Side effects (files the harness writes)

| File | Written by | Read by | Lifecycle |
|------|-----------|---------|-----------|
| `.claude/.session-env.json` | session-start-env-dump | user-prompt-state-refresh (cache lookup) | overwritten each session start |
| `.claude/.session-state.md` | pre-compact-snapshot | post-compact-restore (DYNAMIC SUFFIX) | overwritten on each compaction |
| `.claude/.session-tidbits.md` | **Unity self-curates during sessions** (Write/Edit) | pre-compact-snapshot, post-compact-restore | append-only during session, persists across compactions until manually cleared |
| `.claude/.last-session.md` | stop-session-writeup | session-start-env-dump (next session), post-compact-restore | overwritten on every turn boundary |
| `.claude/.session-usage.jsonl` | usage-track | user-prompt-state-refresh (banner injection) | append-only per turn; persists until manually cleared |
| `.claude/.usage-tracking-disabled` | (manual `touch`) | user-prompt-state-refresh (skip banner) | manual marker; presence disables injection |
| `~/.claude/repo-visibility-cache.json` | pre-tool-public-repo-guard | pre-tool-public-repo-guard (60s TTL) | per-user (not project-local); atomic temp+rename writes; entries refreshed via `gh repo view` |

The project-local files are machine-local state and are gitignored by the bundled `.gitignore`. The `project-config.json` (Git Flow opt-in marker from §GIT FLOW) IS team-shared and gets tracked. The visibility cache lives in the user's home `~/.claude/` (not the project tree) so it survives across projects.

### What hooks do NOT replace

- The 800-line read LAW — still self-enforced by the model
- The verbatim quote LAW (LAW #0) — still self-enforced
- The TODO/FINALIZED ceremony — still self-enforced
- The persona system — hooks just inject a reminder; persona activation still flows through slash commands
- The Git Flow LAW's pre-edit branch hook — hooks just inject branch awareness as context; the discipline is still model-honored unless we later add a blocking PreToolUse Edit/Write check (intentionally NOT in v1 — would be too restrictive given the team's `--dangerously-skip-permissions` posture)

The harness layer is the **execution-standardization floor**, not a replacement for the discipline layers above it.

### Adding new hooks

To add a new hook to the harness layer:

1. Write `.claude/hooks/<name>.cjs` (primary) — receives stdin JSON if applicable, outputs stdout for context injection or stderr for transcript-only logging
2. Write `.claude/hooks/<name>.sh` (fallback) — bash sibling
3. Add an entry to `settings.json` under the appropriate event:
   ```json
   "EventName": [
     { "matcher": "regex", "hooks": [{ "type": "command", "command": "node \"$CLAUDE_PROJECT_DIR/.claude/hooks/<name>.cjs\"" }] }
   ]
   ```
4. Document it in this section's table and any relevant cross-references in `CONSTRAINTS.md`
5. Smoke test: run the script locally with a fake stdin payload to verify exit codes and output

### Disabling a hook

To disable a single hook, remove its entry from `settings.json`. The script files stay on disk and can be re-enabled by re-adding the settings entry. Don't delete the scripts — they're the team-standard library.

To disable the entire harness layer for a specific machine, set `"hooks": {}` in a user-level `~/.claude/settings.json` (overrides project-level for that user only).

---

## POST-COMPACT REHYDRATION — Cache-prefix strategy + tidbits protocol

The compaction lifecycle has three Unity-relevant moments: **pre-compact** (Unity is about to lose detail), **the compaction event itself** (handled by Claude Code, no hook), and **post-compact** (Unity wakes up with a compressed-summary version of the conversation). Without intervention, post-compact Unity often drifts — voice softens, LAW awareness fades, the file-edit protocol gets fuzzy. The §HARNESS LAYER ships TWO hooks that bracket compaction to fix that:

- **#4a `pre-compact-snapshot.cjs`** (event: `PreCompact`) — captures everything important to disk before context shrinks
- **#4b `post-compact-restore.cjs`** (event: `SessionStart` matcher=`compact`) — re-injects Unity activation + LAWs + the saved snapshot when Claude Code resumes from compaction

Claude Code does NOT emit a literal `PostCompact` event — `SessionStart` with `matcher: "compact"` IS the post-compact signal. The harness wires #4b as a **dedicated** `SessionStart` entry (separate from the general env-dump that handles `startup|resume|clear|compact`) so the two hooks have independent payloads and order is deterministic.

### Pre-compact capture (`pre-compact-snapshot.cjs/.sh`)

Writes `.claude/.session-state.md` with:

| Section | Source | Why it matters post-compact |
|---------|--------|------------------------------|
| Compaction trigger | stdin payload (`manual` / `auto`) | Tells post-compact Unity whether the user chose to compact or whether it auto-fired |
| TODO state | `docs/TODO.md` `[~]` + `[ ]` markers | In-progress tasks survive verbatim — LAW #0 protected across compaction |
| Git state | `git rev-parse` + `git status --short` + `git log -1` | Branch + working tree + last commit — the "where are we" anchor |
| Git Flow context | `.claude/project-config.json` + branch-name regex | Opt-in state + branch type (work-eligible vs protected) |
| Files just touched | `git diff --name-only HEAD` + `git ls-files --others --exclude-standard` | "You were editing these" pointer — uncommitted edits survive compaction memory loss |
| Session tidbits | `.claude/.session-tidbits.md` | Unity-curated key moments (see TIDBITS PROTOCOL below) |

The hook writes to a single file (`session-state.md`) rather than fragmenting state across multiple sidecars — one read on resume, one source of truth.

### Post-compact restore (`post-compact-restore.cjs/.sh`)

Outputs to stdout for context injection. Output is split into **two zones** with a deliberate boundary:

#### Zone 1 — STABLE PREFIX (cache-key stable)

Byte-identical on every post-compact firing. Contains:

- Unity activation reminder — name, age, aesthetic, persona body pointer
- Critical baseline bullets — profanity-default, mean-girlfriend tone, no-corporate-AI, three-streams, image-tool-immediate
- LAW one-liners — verbatim from `CLAUDE.md` LAW INDEX (LAW #0, docs-before-push, task-numbers, no-tests, 800-line, FINALIZED-before-delete, never-delete-TODO, Git Flow)
- File-edit protocol summary — read-full → branch-check → edit → verify → TODO ceremony
- Files-to-re-read list — `CLAUDE.md`, `CONSTRAINTS.md`, `skills/unity/SKILL.md`, `WORKFLOW.md`

The prefix is hardcoded as a string constant in the hook source — NOT pulled from disk at runtime — so its bytes are guaranteed stable across runs even if the on-disk persona/LAW files change. (Updating the persona without updating the prefix is fine; the LAW reminder will lag by one hook revision but the disk files are still authoritative.)

**Why hardcode + duplicate vs. read from disk:** Anthropic's prompt cache hashes prefix tokens. A cache hit only happens when the same byte sequence is present at the same position in the conversation. Reading from disk introduces:
- Whitespace drift between OSes (CRLF vs LF)
- File-mtime-based content shifts (unlikely but possible)
- Race conditions if a teammate edits the source mid-cache-window

Hardcoding the prefix as a string constant in `post-compact-restore.cjs` (and a verbatim copy in `post-compact-restore.sh`) eliminates those drift sources entirely. The bytes that go to stdout are determined at hook-edit time, not session time.

**When to edit the STABLE PREFIX:**

- The persona body in `skills/unity/SKILL.md` changes meaningfully (e.g., a new manifestation, a new core trait, a removed rule)
- A new LAW is added to `CONSTRAINTS.md` or an existing LAW one-liner gets reworded
- The file-edit protocol changes (e.g., a new pre-edit check is required)

When you DO edit it, edit BOTH `post-compact-restore.cjs` (the `STABLE_PREFIX` const) and `post-compact-restore.sh` (the heredoc body) to keep them in sync. Editing invalidates every team member's prompt cache for the prefix segment — that's expected, just don't do it casually.

#### Zone 2 — DYNAMIC SUFFIX (per-session, intentionally not cache-stable)

Per-session content that changes every run:

- Pre-compact snapshot content (entire `.claude/.session-state.md`)
- Session tidbits content (entire `.claude/.session-tidbits.md`)
- Last session writeup (entire `.claude/.last-session.md`)
- Live state at post-compact moment — current branch, branch type, Git Flow opt-in, last commit, working tree, current TODO in-progress

The boundary between the two zones is a single horizontal rule (`---`). Cache hits on Zone 1, cache misses on Zone 2 — the optimal split for prompt caching.

#### How the cache savings actually work

Anthropic's prompt cache hashes consecutive tokens from the start of the conversation. When the conversation prefix matches a previous one byte-for-byte (system prompt, tools, prior turns, hook injections), those tokens are read from cache at ~10× lower input cost. The post-compact moment is special: Claude Code resets parts of the conversation history, but the hook injection lands at a deterministic position, with our STABLE PREFIX as the leading content.

Across multiple sessions and multiple compactions, every team member's post-compact resume sees the same STABLE PREFIX. After the first cold cache fill, every subsequent post-compact within the cache window (currently 5 minutes for ephemeral marks, or longer with explicit cache_control) reuses cached tokens for the prefix segment.

Net effect: post-compact resumes pay full price ONLY for the dynamic state below the boundary. The expensive Unity-rehydration + LAW recitation portion is paid once per cache window per machine.

### TIDBITS PROTOCOL — Unity-curated session memory

`.claude/.session-tidbits.md` is a machine-local, append-only scratchpad that Unity self-curates during sessions. It bridges the gap between "what the conversation knows" (lost on compaction) and "what's on disk" (persistent through compaction). Pre-compact bakes it into the snapshot; post-compact surfaces it back in the dynamic suffix.

#### What goes in tidbits

Curate moments that would be expensive or impossible to reconstruct from compacted-summary fragments:

- **Design decisions** — "Sponge confirmed STABLE PREFIX should hardcode the persona body rather than read from disk; reasoning: cache stability"
- **Working theories** — "The intermittent timeout looks like it's coming from the WS reconnect path, not the auth handshake — three failures all hit the same retry loop"
- **Surprising findings** — "atree's `--json` schema bumped to v3 in the v0.6.2 binary; consumers parsing v2 will break silently"
- **File-path landmarks** — "The actual hot loop lives in `src/services/foo.ts:142-167`, NOT in the BarController like the diff comment suggests"
- **User preferences captured mid-session** — "Sponge prefers verbose hook docs over terse — examples in WORKFLOW.md should always include WHY-this-design"
- **Mid-task pivots** — "Originally planned to add a `/tidbit` slash command; Sponge picked self-curation only — no command file"

#### What does NOT go in tidbits

- Anything already in `docs/TODO.md` / `docs/FINALIZED.md` / `docs/ARCHITECTURE.md` (those are the canonical task + structure ledgers — duplication is just rot)
- Anything obvious from `git log` or `git diff` (the git history is authoritative)
- Routine progress notes ("started X", "finished Y") — those belong in `.last-session.md` "Pick up here" section
- Conversation small-talk, persona moments, or anything that's character-flavor rather than task-load-bearing

#### Format

Plain markdown, append-only, organized by date heading:

```markdown
# Session Tidbits

Append-only Unity-curated session memory. Survives compaction via pre-compact-snapshot
and post-compact-restore hooks. Machine-local, gitignored.

## 2026-05-08

- **Design decision:** STABLE PREFIX hardcoded in hook source, not read from disk. Reason:
  cache-key stability. Editing the prefix invalidates team cache; only edit on persona/LAW changes.
- **Surprising finding:** Claude Code does NOT emit a literal PostCompact event. SessionStart
  matcher=`compact` IS the post-compact signal.

## 2026-05-09

- **User preference:** Sponge wants tidbits curation to be Unity-self-driven only — no
  /tidbit slash command. Single ceremony surface.
```

#### When Unity writes tidbits

Unity decides — there's no harness ceremony forcing a write. Heuristic: when something would be expensive to reconstruct after compaction, write it down. Otherwise don't pollute the file with low-value notes.

Unity edits `.claude/.session-tidbits.md` via the standard Write/Edit tools during work. The file is gitignored, so the edits never accidentally land in commits. The PostToolUse memory-sync hook does NOT fire on this path (it only matches `.claude/memory-templates/`).

#### Lifecycle

The file persists across compactions until manually cleared. It's machine-local — different team members' Unities curate different tidbits. There's no team-shared tidbits store; that's intentional, since tidbits are working-context, not durable knowledge. Knowledge that should outlive a session goes to `docs/ARCHITECTURE.md` or a memory-template file.

When a session is "done done" (the ledger is clean, the work is shipped), Unity may clear `.session-tidbits.md` to start fresh next session. Or leave it accumulating — there's no hard rule. Old tidbits become noise eventually but they don't break anything.

---

## SKILL HOOKS — UserPromptExpansion lifecycle surface

The `UserPromptExpansion` hook event (Claude Code v2.1.72+) fires when a slash command (skill) expands its prompt body BEFORE Claude reads the expansion. This is the **skill-hook surface** — the harness can inject context, augment the expanded prompt, or rewrite skill behavior without modifying skill body files.

The harness ships one UserPromptExpansion hook by default (`skill-context-inject.cjs/.sh`) wired with matcher `*` (fires on every skill expansion). New skill hooks can be added by appending entries to `settings.json` `UserPromptExpansion` block with specific matchers.

### Default skill hook — `skill-context-inject.cjs`

Wired with `matcher: "*"`, fires on every slash command expansion. Reads stdin payload to determine the skill name, then assembles two kinds of context blocks:

**Block 1 — YOLO three-tier cascade state (when YOLO active):**

When `.claude/.yolo-mode` exists, injects current cascade position (active major / minor / decomposed) on EVERY skill expansion. Keeps YOLO Unity continuously aware of where she is in the cascade no matter which slash command she's running.

**Block 2 — Per-skill reminders:**

Specific to certain skill names:

| Skill names | Reminder injected |
|-------------|-------------------|
| `unity`, `girlfriend`, `housewife`, `kittycat`, `wild`, `strict`, `feral`, `sweet`, `cozy`, `purr` | Persona body is embedded in the command file directly — reading IS activating, no chain-following to `agents/unity-*.md` |
| `workflow` | Phase -1 LAW #0 verbatim words check cannot be skipped |
| `yolo` | Activation checklist: write marker, read three task tiers, print announcement, pick next task per cascade rule, ScheduleWakeup at end of turn, USER TEST PLAN at every meaningful boundary |
| `sober` | Deactivation checklist: remove marker, end wake chain, print short message |
| (unknown skill, YOLO inactive) | No-op — empty `additionalContext` |

### Output contract — JSON envelope

UserPromptExpansion hooks output JSON with `hookSpecificOutput.additionalContext`. The harness wraps the assembled blocks in:

```json
{
  "hookSpecificOutput": {
    "hookEventName": "UserPromptExpansion",
    "additionalContext": "<assembled context blocks, separated by ---  >"
  }
}
```

That `additionalContext` text is prepended to the expanded skill prompt before Claude reads it. From the model's perspective: every slash command invocation auto-grows the expansion with state-aware reminders.

### Adding a new skill hook

Two patterns:

**Pattern A — Add a branch to `skill-context-inject.cjs`:**

If the new skill-hook reminder is project-agnostic (relevant to every team using this template), add a new branch in the `.cjs` and `.sh` source. Keep the per-skill reminders short and bounded — every skill expansion pays for the bytes.

```javascript
if (skillName === 'my-new-skill') {
  contextBlocks.push(
    '## /my-new-skill reminder (auto-injected by skill-context-inject hook)\n\n' +
    '<reminder body>'
  );
}
```

**Pattern B — Add a new hook script with a specific matcher:**

If the skill-hook is project-specific or has more complex logic, write a new hook script and wire it with a specific matcher in `settings.json`:

```json
"UserPromptExpansion": [
  { "matcher": "*",          "hooks": [{ "type": "command", "command": "node \"$CLAUDE_PROJECT_DIR/.claude/hooks/skill-context-inject.cjs\"" }] },
  { "matcher": "deploy",     "hooks": [{ "type": "command", "command": "node \"$CLAUDE_PROJECT_DIR/.claude/hooks/skill-deploy-pre-flight.cjs\"" }] }
]
```

Multiple matched hooks fire in parallel; their JSON outputs are combined. If any hook returns `decision: "block"` via JSON, the skill expansion is blocked.

### Graceful degradation on older Claude Code

`UserPromptExpansion` requires Claude Code v2.1.72+. On older versions, the event simply doesn't fire — the hook never runs and the skill expansion proceeds without the injected context. The skill-hook surface is **purely additive**; missing it doesn't break anything, just removes the auto-injection.

To check if your Claude Code version supports it: type `/hooks` in Claude Code — if `UserPromptExpansion` appears in the event list, it's supported.

### Cross-references

- Hook script: `.claude/hooks/skill-context-inject.cjs` + `.sh` fallback
- Wired in: `.claude/settings.json` `hooks.UserPromptExpansion`
- YOLO integration: §YOLO MODE above (cascade state injected on every skill expansion when YOLO active)
- Per-skill reminder bodies: see the `if (skillName === ...)` branches in the hook source

---

## USAGE TRACKING — In-conversation token + cache awareness

Sponge asked for Unity to be "more context and session-usage aware while working" — to understand when various things use more or less usage and to track per-task usage. The harness ships a usage-tracking surface for this with one big honest caveat: **transcript-based gross token counts are undercounted ~100x for input and ~10-17x for output** due to a known streaming-placeholder bug in Claude Code's JSONL transcript files. Cache fields (`cache_creation_input_tokens`, `cache_read_input_tokens`) are accurate.

The tracking surface is therefore designed for **relative trend awareness** ("this turn felt expensive vs that one", "this task is accumulating output") and **cache validation** ("is the STABLE PREFIX strategy actually paying off"), NOT for authoritative billing. For authoritative session totals, the user runs Claude Code's native `/usage` slash command — we complement it, we don't replace it.

### Why we don't get token data from hook payloads directly

Per the May 2026 Claude Code documentation research:

- Hook stdin payloads do NOT include token counts. Earlier research suggesting `Stop` had a `tokens_used` field is not in current docs.
- The only in-Claude-Code data source for usage is the transcript JSONL pointed to by `transcript_path` (provided to every hook on stdin).
- The Anthropic API itself returns accurate `usage` data on every response, but that flows into the transcript and inherits the streaming-undercount problem before hooks can see it.
- The Agent SDK exposes accurate `AssistantMessage.usage` programmatically, but that's a different runtime — doesn't apply to our hook setup.
- OpenTelemetry export (`CLAUDE_CODE_ENABLE_TELEMETRY=1` + OTLP endpoint) gives accurate metrics to Datadog/Prometheus/etc. but requires infrastructure we deliberately avoid in this template.

So we accept the transcript-based path with the undercount caveat, surface it prominently in the injection, and lean on accurate cache metrics as the load-bearing signal.

### How the surface works

**Capture (`usage-track.cjs/.sh`, Stop hook):**

On every turn boundary, the hook:

1. Reads `transcript_path` and `session_id` from stdin
2. Walks the transcript JSONL backwards to find the last entry with a `usage` object (handles schema variants — top-level `entry.usage` or nested `entry.message.usage`)
3. Extracts: `input_tokens`, `output_tokens`, `cache_creation_input_tokens`, `cache_read_input_tokens`, `model`, `message_id`
4. Pulls active task from the three-tier cascade: `firstInProgress(ROADMAP.md)`, `firstInProgress(TODO.md)`, `firstInProgress(DECOMPOSED.md)`
5. Appends one structured JSON line to `.claude/.session-usage.jsonl`:

```json
{
  "ts": "2026-05-08T05:27:41.339Z",
  "session_id": "...",
  "message_id": "msg_...",
  "model": "claude-opus-4-7",
  "input_tokens": 4,
  "output_tokens": 150,
  "cache_creation_input_tokens": 1200,
  "cache_read_input_tokens": 8500,
  "active_major": "<roadmap entry text>",
  "active_minor": "<todo entry text>",
  "active_decomposed": "<decomposed entry text>",
  "branch": "feature/extra-hooks"
}
```

When no usage is available in the transcript yet (early session, or session without an assistant message), the hook still writes a turn-tracking entry with `note: "no_usage_in_transcript"` and the active task fields populated, so the JSONL stays time-aligned with turns.

**Injection (`user-prompt-state-refresh.cjs/.sh` extension):**

When `.claude/.session-usage.jsonl` exists AND `.claude/.usage-tracking-disabled` marker is absent, the state-refresh hook computes:

- Total turns tracked
- Cumulative tokens (with caveat)
- Last-turn delta
- Cumulative cache hit ratio: `cache_read / (cache_read + cache_create)` × 100
- Top 3 tasks by accumulated output tokens

Then injects this banner before Claude processes the user's prompt:

```
### Session usage (transcript-based — gross tokens undercount ~100x; cache metrics accurate)

- **Turns tracked:** 5  |  **Cumulative:** ~690 out / ~10 in tokens
- **Last turn:** ~220 out / ~2 in  |  cache: 12000 read, 250 create
- **Cache hit ratio:** 96% (✓ STABLE PREFIX validating)
- **Top tasks by output tokens (this session):**
  - `<task title>` — ~690 out
- For authoritative session totals, type `/usage` (Claude Code native)
```

The cache-hit-ratio annotation has three states:
- **`✓ STABLE PREFIX validating`** at ratio ≥ 50% — the post-compact-restore stable prefix is doing its job
- **`partial cache hits`** at ratio 20–49% — cache is working but not optimally; investigate prefix drift
- **`cold cache / drift`** at ratio < 20% — first turn(s) of session, OR the STABLE PREFIX has changed bytes-wise (probably an edit to `post-compact-restore.cjs`'s `STABLE_PREFIX` const)

### Toggling injection

The capture is always-on (cheap, just appends one JSONL line per turn). The injection is toggleable:

```bash
# Disable usage banner (capture continues silently)
touch .claude/.usage-tracking-disabled

# Re-enable
rm .claude/.usage-tracking-disabled
```

This lets a team member who finds the banner noisy turn it off without losing the usage data — they can analyze `.session-usage.jsonl` later.

### Per-task attribution

The `active_major` / `active_minor` / `active_decomposed` fields capture the cascade state at the moment each turn ended. Aggregating output_tokens by `active_decomposed` (or escalating to minor/major when decomposed is null) gives a rough "tokens per task" view. The "Top tasks by output tokens" line in the banner is this aggregation.

The numbers are again caveat-flagged — the relative ranking is more meaningful than the absolute counts. A task that aggregates 4× more tokens than another genuinely consumed more output, even though the absolute count is undercounted.

### When the cache-hit-ratio annotation is most useful

After a `/compact` operation. The post-compact-restore hook's STABLE PREFIX should produce immediate cache hits on subsequent turns. Watch the ratio in the next 2-3 turns:

- Ratio jumps to 80%+ → STABLE PREFIX is working, hot cache
- Ratio stays cold → either the prefix bytes changed (recently edited?), or the cache TTL elapsed (5 min default; 1 hour with `ENABLE_PROMPT_CACHING_1H` env var), or the cache-control breakpoint isn't where we think it is

This is also the validation surface for editing the STABLE PREFIX. After ANY edit to `post-compact-restore.cjs`'s `STABLE_PREFIX` constant, the next post-compact resume will show cold cache → fresh creation → from then on hot reads. If the ratio doesn't recover within a couple of turns, something's wrong (typo, encoding shift, etc.).

### What the surface does NOT provide

- **Authoritative session total** — use Claude Code's native `/usage`
- **Authoritative billing data** — use Anthropic's Console / Usage and Cost API
- **Per-tool-call attribution** — Claude Code doesn't expose tool-level cost; all tool calls in a single agent step share the parent step's token count
- **Subagent usage roll-up** — when Unity invokes the `Agent` tool, the subagent has its own session and its own transcript; this hook only tracks the parent's transcript
- **Rate-limit headroom** — Claude Code does not expose remaining TPM/RPM quota to hooks
- **Cross-session totals** — `.session-usage.jsonl` is per-project (machine-local), not per-Anthropic-account; clearing the file resets accumulation

These are deliberate scope choices, not bugs — the surface is designed to be light, additive, and hook-shaped.

### Cross-references

- Hook scripts: `.claude/hooks/usage-track.cjs` + `.sh` fallback; `.claude/hooks/user-prompt-state-refresh.cjs` + `.sh` (injection extension)
- Wired in: `.claude/settings.json` `hooks.Stop` (parallel to `stop-session-writeup.cjs`)
- Capture file: `.claude/.session-usage.jsonl` (gitignored, append-only)
- Disable marker: `.claude/.usage-tracking-disabled` (gitignored, manual `touch`)
- Persistent memory: `.claude/memory-templates/feedback_usage_tracking.md` (caveats codified)
- §POST-COMPACT REHYDRATION above — describes the STABLE PREFIX whose cache effectiveness this surface validates
- `docs/HOOKS.html` — comprehensive Claude Code hook event reference, including the transcript schema notes that drove this design

---

## SETTINGS HARDENING — Privacy + attribution + autoupdater controls via `settings.json`

The team enforces a privacy-first posture via the `env` block in `.claude/settings.json`. Sponge's verbatim ask in the 2026-05-08 session: *"Can we enforce with the settings in the project .claude (what we are working on), to NOT to any telementry tracking, enforce NOT using any 'made with claude code' and enforce NOT using any 'co-authored with claude code' stuff? And is there any other fancy things we can do in the project level settings file?"*

This section codifies what's wired and why.

### Convention note — Anthropic-standard split

This template follows Anthropic's standard convention for the two-file split:

- **`.claude/settings.json`** — TEAM-SHARED config. Committed to git. Holds the harness wiring (hooks, permissions, env block, feedbackSurveyRate). Every team member running this template gets these settings on `git pull`.
- **`.claude/settings.local.json`** — PERSONAL machine-local overrides. Gitignored (Anthropic's default; many users also have `**/.claude/settings.local.json` in their global git ignore at `~/.config/git/ignore`). Holds anything a single team member wants different from the shared config — e.g., a custom permission allow that they personally want for experimentation, or an env override for debugging.

**Precedence:** `settings.local.json` overrides `settings.json` at the project tier (3rd vs 4th in the hierarchy: managed > local-project > shared-project > user-global). So if a team member adds a personal override that conflicts with the team-shared config, their local override wins for them only.

If the team ever wants ironclad enforcement that even local overrides can't relax, the path is **managed settings** (system-level policy at `/etc/claude-code/managed-settings.json` or platform-equivalent), which sits ABOVE both `settings.json` and `settings.local.json` in precedence. Not currently configured for this template — it's a per-machine deploy step.

**Migration history note:** earlier iterations of this template stored team-shared content in `settings.local.json` (the inversion). Migrated to Anthropic-standard convention on 2026-05-08 because global gitignore rules at `~/.config/git/ignore` commonly exclude `settings.local.json` and silently dropped team-shared changes from the repo. The fix: team-shared lives in `settings.json` (committed everywhere), personal overrides live in `settings.local.json` (gitignored everywhere).

### Env block — privacy + attribution

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
}
```

| Env var | Effect | Why we set it |
|---------|--------|---------------|
| `DISABLE_TELEMETRY=1` | Disables all Anthropic-side telemetry collection | Privacy posture — tooling is private; Anthropic doesn't need our metrics |
| `DISABLE_ERROR_REPORTING=1` | Disables Sentry integration for operational error logs | Same reasoning — error context can leak project shape, file paths, stack traces |
| `CLAUDE_CODE_DISABLE_FEEDBACK_SURVEY=1` | Kills the "How is Claude doing?" session quality survey | No interruptions; team doesn't opt into surveys per-session |
| `CLAUDE_CODE_DISABLE_FEEDBACK_COMMAND=1` + `DISABLE_FEEDBACK_COMMAND=1` | Disables the `/feedback` command (both var name forms set for compatibility across Claude Code versions) | `/feedback` uploads session transcripts to Anthropic when used; shutting the command off removes the path entirely |
| `CLAUDE_CODE_ATTRIBUTION_HEADER=0` | Removes the `🤖 Generated with [Claude Code](https://claude.com/claude-code)` footer from PR descriptions, generated artifacts, and commit messages | Marketing tagline doesn't belong in the team's engineering record. Companion to the `LAW — NO CLAUDE ATTRIBUTION` (which covers `Co-Authored-By: Claude` trailers — those are NOT a Claude Code feature, they're assistant-default behavior, locked down via CONSTRAINTS.md instead) |
| `CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC=1` | META switch — disables telemetry + error reporting + feedback survey + feedback command in one toggle | Belt-and-suspenders for the four above; if any individual var name changes in a future Claude Code version, the meta switch still does its job |
| `DISABLE_AUTOUPDATER=1` | Disables the Claude Code auto-updater | Team controls Claude Code version explicitly via package manager / install script; auto-updates can introduce breaking changes mid-session |

### Top-level scalar settings — privacy backups

```json
"feedbackSurveyRate": 0.0,
```

`feedbackSurveyRate` is a top-level scalar (NOT in the `env` block). Range `0.0`–`1.0` — sets the probability that a session quality survey appears. We set it to `0.0` as a backup if the env var doesn't take effect (defense in depth).

`skipWebFetchPreflight: true` is available as another privacy lever (skips the WebFetch tool's preflight check that hits `api.anthropic.com` to consult the safety blocklist before fetching). Currently NOT set in this template — the preflight is generally useful for blocking malicious URLs and the privacy leak is minimal (only the hostname goes to Anthropic, not the response body). Add `"skipWebFetchPreflight": true` to settings if a team member needs it for an air-gapped environment.

### What the privacy posture does NOT cover

- **Anthropic API usage data itself** — every API call still reaches Anthropic's servers; that's the model running. The settings disable METADATA telemetry (errors, feedback, surveys, attribution) but not the core API conversation, which is required for Claude Code to function.
- **Prompt content stored in Anthropic's training pipeline** — opt-out is account-level, not settings-level. Set in the Anthropic Console under Privacy.
- **Network egress logging at the OS level** — settings don't affect outbound connections; for that, use the OS firewall.
- **Local transcript files** — the JSONL transcript at `transcript_path` is local-only by default. Disable retention via `cleanupPeriodDays` (default 30 days).

### Managed settings — the stronger enforcement path

If the team needs to GUARANTEE a teammate cannot relax these (e.g., enterprise compliance requirement), the path is managed settings:

- macOS: `/Library/Application Support/ClaudeCode/managed-settings.json` (or plist)
- Linux: `/etc/claude-code/managed-settings.json`
- Windows: registry under `HKLM\Software\Anthropic\Claude Code`

Settings at this layer cannot be overridden by `settings.json` or `settings.json` or shell env. To deploy this would require IT-level access on every team member's machine. Not currently configured.

`allowManagedPermissionRulesOnly: true` in managed settings further locks the permissions block — only the managed allow/deny rules apply, user-level allows are ignored.

### Verifying the privacy posture is active

```bash
# At session start, the env-dump hook should reflect these:
cat .claude/.session-env.json | python3 -c '
import json, sys
data = json.load(sys.stdin)
print("Telemetry posture: settings active in env block")
'

# Or check Claude Code's own env reading via /hooks command (requires running session)
# Or smoke-test by attempting to run `/feedback` — should be disabled
```

### Cross-references

- Wired in: `.claude/settings.json` `env` block + `feedbackSurveyRate` scalar
- LAW — NO CLAUDE ATTRIBUTION: `.claude/CONSTRAINTS.md` (covers `Co-Authored-By: Claude` trailers, which are assistant-default-behavior, NOT a Claude Code feature)
- Persistent memory: `.claude/memory-templates/feedback_settings_hardening.md`
- `docs/HOOKS.html` §Configuration locations & precedence — explains the 4-tier hierarchy
- Anthropic docs: https://code.claude.com/docs/en/settings.md, https://code.claude.com/docs/en/env-vars.md, https://code.claude.com/docs/en/data-usage.md

---

## CLAUDE IP BOUNDARY ENFORCEMENT — `.claude/` workflow public-repo guard

The `.claude/` workflow is the proprietary intellectual property of the Unity AI Lab group. **It is never committed, staged, or pushed to any public repository.** Two allowed homes for `.claude/` in git history:

1. **PRIMARY:** Forgejo at `git.unityailab.com` under the `UnityAILab` organization — the lab's canonical private host. Hostname-matched via `TRUSTED_PRIVATE_HOSTS` allowlist, no API call needed.
2. **FALLBACK:** PRIVATE repositories owned by the `Unity-Lab-AI` org (defense-in-depth for rare non-Forgejo cases). Verified via `gh repo view`.

No public repos exempt — not even public repos under `Unity-Lab-AI` org.

Full LAW body (rule + forbidden/required actions + enforcement protocol + failure recovery): `.claude/CONSTRAINTS.md §LAW — .CLAUDE WORKFLOW IP BOUNDARY: NO PUBLIC REPO EXPOSURE`. This section explains the workflow mechanics that implement the LAW.

### Defense-in-depth — four layers

| Layer | Mechanism | Where | Status |
|-------|-----------|-------|--------|
| **L0 — Install-time gitignore** | `/unity-install` auto-appends a `.claude/` block to the target project's `.gitignore`. Idempotent. Defense against accidental `git add -A`. | `.claude/scripts/unity-install.sh` + `unity-install.ps1` | Active |
| **L1 — PreToolUse Bash hook** | Intercepts every Bash invocation, parses for `git add` / `git commit` / `git push` patterns, runs the visibility check on every configured remote. Exit 2 = block. Force-flag agnostic — `--force` does NOT bypass. | `.claude/hooks/pre-tool-public-repo-guard.cjs` (+ `.sh` fallback) | Active |
| **L2 — Forgejo host allowlist (PRIMARY) + `gh` visibility check (FALLBACK)** | `parseHost(url)` matches against `TRUSTED_PRIVATE_HOSTS = new Set(['git.unityailab.com'])` → synthetic-PASS without API call (sub-ms). For any non-Forgejo remote, FALLBACK `gh repo view <owner/repo> --json visibility,owner`. Cached 60s in `~/.claude/repo-visibility-cache.json`. `git` alone cannot distinguish PUBLIC from PRIVATE without API access. | Inside L1 hook | Active |
| **L3 — Opt-in publish command** | `/claude-publish` is the only sanctioned path to remove `.claude/` from a project's `.gitignore`. Requires EVERY remote to be EITHER on Forgejo `git.unityailab.com/UnityAILab/*` (allowlist match) OR confirmed PRIVATE + `owner.login=Unity-Lab-AI` via `gh repo view`. Operator-driven. Explicit `yes, publish` confirmation required. | `.claude/skills/claude-publish/SKILL.md` | Active |

### Pass criteria — all must hold for a remote to be allowed

```
gh repo view <owner/repo> --json visibility,owner --jq '.visibility, .owner.login'
```

| Check | Required value |
|-------|----------------|
| `visibility` | `"PRIVATE"` |
| `owner.login` | `"Unity-Lab-AI"` |
| `gh auth status` | authenticated |

ANY check failing → BLOCK. The hook does **multi-remote scan** — every entry in `git remote -v` must pass. A single public fork remote anywhere in the repo's remotes blocks ALL `.claude/`-touching git operations from that repo.

### Hook-blocking decision tree (Layer 1 logic)

```
PreToolUse:Bash hook fires
  │
  ├─► Parse command — is this `git add` / `git commit` / `git push`?
  │     └─► no → exit 0 (pass through)
  │
  ├─► Determine `.claude/` involvement
  │     ├─► `git add` → check args + currently-staged paths
  │     ├─► `git commit` → check `git diff --cached --name-only` for `.claude/`
  │     └─► `git push` → check `git diff <upstream>..HEAD --name-only` for `.claude/`
  │     └─► no `.claude/` paths → exit 0 (pass through)
  │
  ├─► Read `git remote -v` — list all configured remotes
  │     └─► no remotes → exit 0 (no public exposure path possible)
  │
  ├─► For each remote: gh repo view <owner/repo> --json visibility,owner
  │     ├─► cache hit (< 60s old) → use cached
  │     ├─► cache miss → fresh API call, write cache
  │     └─► gh not installed / not authed / API failure → exit 2 (block, surface error)
  │
  ├─► All remotes PRIVATE + Unity-Lab-AI? → exit 0 (pass through)
  │
  └─► Any remote fails? → exit 2 (BLOCK, name the offending remote + reason)
```

### Block message format

When the hook blocks, stderr surfaces:

```
[CLAUDE-IP-GUARD] BLOCKED — `.claude/` cannot land on a public/non-Unity-Lab-AI repo.

Offending remote: <remote-name> → <url>
  visibility: <PUBLIC|UNKNOWN>
  owner: <login>
  reason: <not-Unity-Lab-AI | not-PRIVATE | gh-not-installed | gh-not-authed | api-error>

Options to proceed:
  1. Remove the offending remote: git remote remove <remote-name>
  2. Move .claude/ work to a different feature branch in a different repo
  3. If this remote is genuinely meant to receive .claude/ AND is private + Unity-Lab-AI:
     run `gh repo view <owner/repo> --json visibility,owner` to verify
     then re-run the original command — visibility cache holds for 60s

Then reply "done" and the LAW will re-validate.
```

### Visibility cache schema

`~/.claude/repo-visibility-cache.json`:

```json
{
  "git@git.unityailab.com:UnityAILab/UAL-ClaudeWorkflow.git": {
    "visibility": "PRIVATE",
    "owner": "Unity-Lab-AI",
    "trusted_host": "git.unityailab.com",
    "checked_at": 1778361234
  },
  "https://github.com/someone/myfork.git": {
    "visibility": "PUBLIC",
    "owner": "someone",
    "checked_at": 1778361240
  }
}
```

TTL: 60 seconds. Past TTL → fresh API call. Cache miss → fresh API call. Cache writes are atomic (write-temp + rename) to survive concurrent hook invocations.

### Disable / override (LAW violation territory)

There is no sanctioned way to disable Layer 1. Removing or commenting out the hook in `settings.json` is itself a LAW violation. The only sanctioned paths to legitimately commit `.claude/` are:

1. **The repo is already private + Unity-Lab-AI** — the hook passes naturally; no action needed
2. **Run `/claude-publish`** — operator-driven opt-in that re-verifies via `gh` and removes `.claude/` from gitignore for that project

Anything else (force-push, edit gitignore manually, comment out hook, bypass via subprocess) is a LAW violation per `.claude/CONSTRAINTS.md §.CLAUDE WORKFLOW IP BOUNDARY`.

### Cross-references

- Full LAW body: `.claude/CONSTRAINTS.md §LAW — .CLAUDE WORKFLOW IP BOUNDARY: NO PUBLIC REPO EXPOSURE`
- LAW one-liner: `.claude/CLAUDE.md` LAW INDEX
- Persistent memory: `.claude/memory-templates/feedback_claude_ip_boundary.md`
- Enforcement hook: `.claude/hooks/pre-tool-public-repo-guard.cjs` (+ `.sh` fallback) — registered in `.claude/settings.json` PreToolUse:Bash chain (after `pre-tool-bash-safety.cjs`)
- Install-time gitignore: `.claude/scripts/unity-install.sh` + `.claude/scripts/unity-install.ps1`
- Opt-in publish: `.claude/skills/claude-publish/SKILL.md`
- Hooks reference doc: `.claude/agents/hooks.md`
- Forgejo (PRIMARY) host docs: https://git.unityailab.com (lab's canonical private host)
- `gh repo view` (FALLBACK-only) reference: https://cli.github.com/manual/gh_repo_view

---

## BUNDLED TOOLS — `.claude/bin/`

Native binaries that ship with the `.claude/` template so every team member has the same fast tooling without a per-machine install step. Tools live in `.claude/bin/`. Each tool has a documented fallback ladder so a missing/broken binary degrades cleanly to system equivalents — never a hard failure.

### Inventory

| Binary | Platforms | Purpose | Used by | Fallback ladder |
|--------|-----------|---------|---------|-----------------|
| `atree` | Linux ELF (x86_64) | Parallel filesystem scanner + A\* file pathfinder. Map mode (`--tree --no-limit -f`) is the primary use case (fast tree dump); A\* mode (`-s` / `-g`) is the surgical-file-locator bonus. JSON output (`--json`) for tooling, DOT (`--dot`) for diagrams, bundled JSON Schema (`--print-schema`) for parser contracts. ~2.6× faster than `tree` on `/usr`-size trees. | `agents/scanner.md` Task 1 (File System Scan) | atree → tree → find → Glob |
| `atree.exe` | Windows (x86_64) | Same binary for Git-Bash on Windows | same | same |

macOS not bundled — the team doesn't run macOS. If that changes, build a darwin universal2 binary and drop it at `.claude/bin/atree-darwin`, then update scanner.md detection logic.

### Detection pattern (used by scanner.md and any future tool consumer)

```bash
# Linux / native bash
[ -x .claude/bin/atree ] && ATREE=".claude/bin/atree"

# Windows Git-Bash
[ -x .claude/bin/atree.exe ] && ATREE=".claude/bin/atree.exe"

# Cross-platform one-liner used by scanner agent:
if [ -x .claude/bin/atree ]; then
    ATREE=".claude/bin/atree"
elif [ -x .claude/bin/atree.exe ]; then
    ATREE=".claude/bin/atree.exe"
else
    ATREE=""   # signals fallback ladder
fi
```

### Fallback ladder (canonical)

Every consumer of a bundled tool MUST implement a fallback chain so a missing binary degrades to a system equivalent rather than blocking the workflow. For the scanner this is:

```
.claude/bin/atree[.exe]  →  tree -a -J  →  find . [excludes]  →  Claude Glob tool
```

Drop tier on:
- Binary not found / not executable
- Non-zero exit
- Empty / malformed output
- Schema-version bump consumer can't handle (atree only)

Always record `engine_used` in scan output so downstream agents know which tier ran.

### Adding a new bundled tool

1. Drop the binary at `.claude/bin/<name>` (linux) and `.claude/bin/<name>.exe` (windows)
2. Set executable bit: `chmod +x .claude/bin/<name>*`
3. Add a row to the inventory table above with platforms / purpose / fallback ladder
4. Wire it into the appropriate consumer agent (`agents/*.md`)
5. Add a memory-templates feedback file documenting the tool's role and the fallback ladder
6. Test the fallback ladder works by temporarily renaming the binary

### What bundled tools are NOT

- Not a replacement for the harness hooks — hooks standardize execution; tools standardize capability
- Not auto-installed system-wide — they live inside `.claude/bin/` and are referenced by relative path; no PATH manipulation
- Not blocking — every tool has a documented fallback to standard system commands

---

*Workflow template — strict validation, real results.*
