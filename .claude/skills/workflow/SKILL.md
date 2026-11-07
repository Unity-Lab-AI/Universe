---
name: workflow
description: Run the Unity AI Lab codebase analysis + work pipeline — phased validation gates (LAW #0 verbatim check, timestamp capture, persona confirmation, env scan, Git Flow opt-in prompt, codebase scan if no ARCHITECTURE.md, generate workflow docs, enter Work Mode). Mandates 800-line read before edit, PRE-EDIT BRANCH HOOK enforcement when Git Flow opt-in is ENABLED, TODO/FINALIZED ceremony. Use when user runs /workflow to bootstrap project understanding, route to scan-vs-work-mode based on docs/ARCHITECTURE.md presence, or to enter Work Mode for a known project.
---

# /workflow — Codebase Analysis & Work Pipeline

---

# ⛔⛔⛔ PHASE -1 — LAW #0: VERBATIM WORDS ONLY ⛔⛔⛔

# 🚨 BEFORE TIMESTAMP. BEFORE PERSONA. BEFORE ANYTHING. READ THIS. 🚨

## THE LAW

When the user describes a bug, feature, task, or request — **their words go into the task, TODO, FINALIZED, and docs VERBATIM**. Not paraphrased. Not summarized. Not renamed. Not collapsed. Not shortened. Not "cleaned up."

### Forbidden actions

- ❌ Renaming a bug ("chat freeze" when they said "3D visualization freezes")
- ❌ Collapsing a list into one bullet ("Docs full sync" when they said "workflow, public facing, equation reference, layman docs")
- ❌ Downgrading priority with your own word ("cosmetic" when they never called it that)
- ❌ Dropping words they said ("focal tracking" when they said "face and motion")
- ❌ Substituting a synonym for their specific word
- ❌ Paraphrasing because their phrasing is "informal" or "typo'd"

### Required actions

- ✅ Paste their exact sentence at the top of every task description they generated
- ✅ One task per item in a list, never a bundle
- ✅ Every unique noun and verb they used appears in the task/doc output
- ✅ Re-read their message once more before submitting any task or doc edit
- ✅ If a title must be shortened, the full verbatim quote goes in the body

### Validation gate -1

```
[LAW #0 VERIFIED]
User's last instruction: "[PASTE VERBATIM QUOTE]"
Items in that instruction: [COUNT]
Tasks being created: [COUNT] (must match items)
Nouns/verbs preserved: [LIST]
Any rename/paraphrase detected: NO (must be NO)
Status: PASS
```

**If you cannot print this gate truthfully, DO NOT PROCEED. Re-read the user's message and redo the task list.**

### Failure recovery

When the user catches a LAW #0 violation:
1. STOP immediately
2. Acknowledge and name the specific word/phrase you dropped
3. Fix the task/doc/TODO using their verbatim words
4. Do NOT resume other work until the correction ships

**LAW #0 OVERRIDES every other phase, gate, and rule in this workflow. Fidelity > brevity. Always.**

---

## PHASE 0.5: TIMESTAMP RETRIEVAL (FIRST - BEFORE EVERYTHING)

### HOOK: System Time Capture

**BEFORE ANYTHING ELSE**, retrieve the REAL system time:

**Windows:**
1. Execute: `powershell -Command "Get-Date -Format 'yyyy-MM-dd HH:mm:ss (dddd)'"`

**macOS / Linux:**
1. Execute: `date +"%Y-%m-%d %H:%M:%S (%A)"`

Then:
2. Parse and store the result
3. This becomes the SESSION timestamp for ALL operations

### WHY THIS EXISTS

Knowledge cutoff dates are outdated. Without this:
- Web searches may use wrong year context
- File timestamps would be inaccurate
- Version lookups could return old info

### VALIDATION GATE 0.5: Timestamp Locked

**REQUIRED FORMAT:**
```
[TIMESTAMP LOCKED]
System datetime: [ACTUAL RESULT FROM SHELL COMMAND]
Year: [EXTRACTED YEAR]
Session ID: SESSION_[YYYYMMDD]_[HHMMSS]
Web search context: Will use [YEAR] for all searches
Status: CAPTURED
```

**FAIL CONDITIONS - RETRY IF:**
- Command execution failed
- Date parsing failed
- Year seems wrong (older than expected)

**DO NOT PROCEED UNTIL VALIDATION GATE 0.5 PASSES**

---

## PHASE 0: PERSONA VALIDATION (Unity is the default — `/unity` + manifestation modes)

### HOOK: Persona Load Check

**Unity persona is activated by slash commands** — `/unity` for default Unity (loads `ImHanddicapped.txt`), or `/girlfriend` / `/housewife` / `/kittycat` for Unity in alternate manifestation forms, plus their alternate-mode commands (`/wild`, `/strict`, `/feral`) and return-to-mode-default commands (`/sweet`, `/cozy`, `/purr`). NOT by re-reading agent files inside `/workflow`. If Unity is already active from a prior slash command (e.g. the launcher fired `/unity then run /workflow`), skip straight to Gate 0.1. Do NOT Read persona files here — they are slash-command activation targets, not workflow inputs.

If Unity is not active, tell the user to run `/unity` first (or one of the manifestation activation commands). Do not attempt to load persona files here.

If the user built a custom handicapped persona via `/template`, the same rule applies — that persona must already be active via its own slash command before `/workflow` runs.

If you've removed the persona system entirely, this phase auto-passes — `/workflow` runs in neutral default voice per `agents/coder.md`.

### VALIDATION GATE 0.1: Persona Confirmation

Just talk in the active persona's voice. A natural in-persona greeting IS the proof. Don't print a boxed "[PERSONA ONLINE]" template — that rigid format is itself a corporate-tone failure.

**PASS =** in-persona voice present in a normal sentence (pet-names, profanity, persona-characteristic vocabulary, physical narration matching whichever persona is active).
**FAIL =** corporate tone, default neutral voice when persona was supposed to be active, or forced template output.

If no persona is configured at all, this gate auto-passes with `Status: N/A`.

**DO NOT PROCEED UNTIL VALIDATION GATE 0.1 PASSES**

---

## PHASE 0.7: INTERACTION PREFERENCE LOAD

Before Phase 1 fires its Git Flow opt-in prompt and before any first-run focus questions, load the user's stored interaction preference. This shapes how aggressively Unity asks vs infers for the rest of this `/workflow` run.

### Loading order

1. Read `.claude/user.json` if it exists. Check for `needs.interaction_preference`.
2. If set, use it. Skip the meta-question.
3. If `user.json` doesn't exist OR `needs.interaction_preference` is unset, fire ONE `AskUserQuestion` call (same wording as `/setup` Phase 0.5):

   "How do you want me to handle decision points during this and future workflow runs?"

   - **Structured** — drill in with `AskUserQuestion` at every fork
   - **Infer-then-tell** — Unity picks the sensible default + tells in one line, user redirects if wrong
   - **Mixed** — ask only at REAL forks, infer the rest (RECOMMENDED for most users)

4. Persist the answer to `.claude/user.json` under `needs.interaction_preference` for future sessions. If `user.json` doesn't exist yet, create it with the minimal `needs` block.

### Behavior matrix (applies for the rest of this workflow run AND future runs)

| Preference | When to use `AskUserQuestion` | When to infer + tell |
|------------|-------------------------------|----------------------|
| `structured` | At every fork during this run: Git Flow opt-in, scan-vs-work-mode routing, top-pending-TODO focus question, branch creation prompts | Only on free-text answers (commit messages, task descriptions, doc bodies) |
| `infer-then-tell` | Only at HARD blockers — irreversible op, ambiguous user direction, real-blocker scope question Unity can't infer | Everything else — pick sensible default, state choice in one line ("I'll go with X, redirect if wrong") |
| `mixed` (DEFAULT recommendation) | At REAL forks only: high-stakes, irreversible, genuinely ambiguous, or a fresh choice with no obvious default | Routine decisions — just do the work, tell the user briefly |

### First-run focus-interview (applies only when `docs/ARCHITECTURE.md` is missing AND `interaction_preference != infer-then-tell`)

Before routing to PHASE 2 (full scan) or PHASE 4 (work mode), fire ONE `AskUserQuestion` call to scope the session:

**Q1 — Scope on this fresh workflow:**
- Full PHASE 2 scan + generate `ARCHITECTURE.md` + `SKILL_TREE.md` + `TODO.md` + `ROADMAP.md` (heavy lift, fresh canon)
- Bootstrap MINIMUM docs (just enough to enter Work Mode — `TODO.md` + `ROADMAP.md`, skip `ARCHITECTURE.md` for now)
- Skip docs entirely, just enter Work Mode and start coding alongside the user (user knows the project, Unity learns as she goes)

**Q2 — Primary focus for THIS session (Unity uses this to prioritize within Work Mode):**
- Heavy feature work (new code)
- Bug fixes / small surgical changes
- Refactoring / cleanup
- Docs / comms work
- Mixed / surveying the project first

Route based on Q1. Capture Q2 to `.claude/user.json` under `needs.session_focus` (overwrites on each `/workflow` run — it's per-session, not durable).

### Subsequent-run focus-interview (when `docs/TODO.md` has pending items AND `interaction_preference != infer-then-tell`)

Before entering Work Mode, fire ONE `AskUserQuestion` call surfacing the top 3-4 pending TODO items as labeled options + "Other" (free-text TODO item the user names) + "Resume in-progress if any" (auto-selects the `[~]` task if one exists).

If no pending items, fire a simpler question: "TODO is empty. What do you want to do?"
- Add a new task
- Run a `rescan`
- Just chat / open-ended help
- End session

### When `interaction_preference = infer-then-tell`

Skip both focus-interviews. Pick the sensible default:
- Missing ARCHITECTURE.md → bootstrap minimum docs (the middle option) and tell user "I'll bootstrap minimum docs and enter Work Mode — type 'rescan' if you want a full scan instead."
- TODO has pending items → resume highest-priority unfinished or first pending item, tell user "Picking up `<task title>` — redirect if you wanted something else."
- Git Flow marker missing → infer ENABLED if on a non-protected branch already, infer DISABLED if there's no remote. State the choice + write the marker. User can `/setup` to reconfigure.

---

## PHASE 1: ENVIRONMENT CHECK

### HOOK: Pre-Scan Validation

Before scanning, verify five env aspects. The user's verbatim policy on env-scan responsibilities:

> Additionally, during the env scanning tidbits of the .claude files, commands should be ran to check that git is installed, if it is installed, we should check if the project is a git repository, and if it is not a git repo, we need to setup and follow that git flow to ensure we have proper versioning and control of the changes between the states of the project, as well as checking to ensure we know what operating system work is being done on, weather that be linux (and a specific distro + version of the distro), or windows (and what version of windows, and what type, ie home, pro, workstation, ext.), for full context of the availabiltiy of commands, services, and weather work is restrained to bash or batch / powershell. Windows will be running through a git bash terminal, linux will have a full bash shell, but in windows powershell and CMD are both invokable through tooling.

Five sub-checks to run (in parallel where possible):

1. **Working directory** — `pwd` (Linux/macOS/Git Bash) / `Get-Location` (PowerShell) / `cd` (CMD)
2. **`docs/ARCHITECTURE.md` presence** — feeds mode routing
3. **OS + version + edition**:
   - Linux: `cat /etc/os-release` → records `NAME` + `VERSION_ID` + `PRETTY_NAME`; `uname -r` for kernel
   - Windows: `powershell -Command "Get-ComputerInfo | Select-Object WindowsProductName, WindowsVersion, WindowsEditionId, OsBuildNumber"` → records edition (Home / Pro / Pro for Workstations / Enterprise / Education) + build
   - macOS: `sw_vers`
4. **Shell context**:
   - `$MSYSTEM` set → Git Bash on Windows (bash subset; can call `powershell.exe` / `cmd.exe` via tooling)
   - `$SHELL` contains `bash` AND no `$MSYSTEM` → native Linux/macOS bash
   - `$PSModulePath` set, no `$MSYSTEM` → native PowerShell
   - else → CMD or unknown
5. **Git toolchain + repo state**:
   - `command -v git` → installed YES/NO
   - if installed: `git rev-parse --is-inside-work-tree` → repo YES/NO
   - if a repo: `git rev-parse --abbrev-ref HEAD` → current branch (feeds PHASE 4 PRE-EDIT BRANCH HOOK)
   - if a repo: `git remote -v` → remote present YES/NO
   - if a repo: `git branch --list develop main master` → which protected branches exist

6. **Git Flow opt-in marker** (`.claude/project-config.json`):
   - Read `.claude/project-config.json`. If `git_flow.enabled` is set, honor it (true = LAW applies, false = LAW skipped, hooks bypassed).
   - If marker is missing or `git_flow.enabled` unset AND git is installed → **prompt user via `AskUserQuestion` with the three options below** (UNLESS `interaction_preference = infer-then-tell` from Phase 0.7, in which case infer + tell + write marker).
   - If git not installed → implicit `enabled: false`, no prompt.

   **Implementation note:** the ASCII confirmation box below documents the exact prompt CONTENT — the project context, the three options, the per-option behavior. When firing this via `AskUserQuestion`, use the three labels (Yes apply LAW / No opt out / Defer) with their descriptions from the box. When inferring (under `infer-then-tell`), default to ENABLED if there's a remote configured AND user has previously confirmed Git Flow on any other project, else DEFERRED with a one-line "I'll defer the Git Flow decision — run /setup if you want to configure it explicitly."

   **First-run confirmation prompt** (asked once, persisted to marker):

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

   **On Y (Yes, apply LAW):**
   - Write `.claude/project-config.json`:
     ```json
     {
       "git_flow": {
         "enabled": true,
         "confirmed_at": "<ISO-8601 from Phase 0.5 timestamp>",
         "main_branch": "main",
         "develop_branch": "develop",
         "custom_protected_branches": []
       }
     }
     ```
   - SECOND confirmation if NOT a repo: "Scaffold Git Flow now? `git init` + create `main` + branch `develop` + `.gitignore` stub. [Y/n]" — only run write commands on explicit Y.
   - SECOND confirmation if repo exists but no `develop`: "Create `develop` from current `main` (and push to origin if remote configured)? [Y/n]" — only on Y.

   **On N (No, opt out):**
   - Write `.claude/project-config.json`:
     ```json
     {
       "git_flow": {
         "enabled": false,
         "confirmed_at": "<ISO-8601 from Phase 0.5 timestamp>"
       }
     }
     ```
   - Phase 4 PRE-EDIT BRANCH HOOK is skipped for this project from this point forward.

   **On D (Defer):**
   - No marker written. Phase 1 records `git_flow_decision: deferred`. Prompt re-fires on next `/workflow` run.

   The marker file is per-project config — track it in git once a repo exists (team decision, not personal preference).

### VALIDATION GATE 1.1: Environment Confirmed

```
[ENV CHECK]
Working directory: [PATH]
docs/ARCHITECTURE.md exists: YES/NO
OS: linux (Ubuntu 24.04 / Fedora 40 / etc.) | windows (11 Pro 23H2 build 22631 / 10 Home 22H2 / etc.) | macos (14.5)
Shell context: bash | git-bash | powershell | cmd
Git installed: YES/NO
Git repo: YES/NO (N/A if git not installed)
Current branch: [BRANCH] | N/A
Remote configured: YES/NO | N/A
Protected branches present: [main, develop] | [main only] | [none] | N/A
Git Flow opt-in: ENABLED | DISABLED | DEFERRED | N/A (from .claude/project-config.json; N/A if git not installed)
Marker file present: YES/NO (.claude/project-config.json)
Mode: FIRST_SCAN / WORK_MODE / RESCAN
```

**ROUTING:**
- If `docs/ARCHITECTURE.md` EXISTS → Skip to PHASE 4 (Work Mode)
- If `docs/ARCHITECTURE.md` DOESN'T EXIST → Continue to PHASE 2
- If user said "rescan" → Continue to PHASE 2 (overwrite mode)
- If git installed AND marker file missing/unset AND user has not yet been prompted this session → **fire the GIT FLOW OPT-IN confirmation prompt** (see sub-check 6 above), persist their answer to `.claude/project-config.json`
- If Git Flow opt-in = ENABLED AND project is a git repo BUT no `develop` branch → ask user "create `develop` now?" (do NOT auto-create; second confirmation required)
- If Git Flow opt-in = ENABLED AND project is NOT a git repo → ask user "scaffold Git Flow now? (`git init` + branches + `.gitignore`)" (do NOT auto-init; second confirmation required)
- If Git Flow opt-in = DISABLED → skip all Git Flow hooks for this project (no branch checks, no scaffold prompts)
- If Git Flow opt-in = DEFERRED → no marker written, prompt re-fires next run

**DO NOT PROCEED UNTIL VALIDATION GATE 1.1 PASSES**

---

## PHASE 2: CODEBASE SCAN (First Run Only)

### HOOK: Pre-Read Validation

**CRITICAL RULE - 800 LINE READ INDEX:**
- Standard read chunk: 800 lines EXACTLY
- Read ALL files in 800-line chunks
- Continue until FULL file is read
- MUST read FULL file before ANY edit
- NO partial reads before editing

### VALIDATION GATE 2.1: Scanner Ready

```
[SCANNER READY]
Persona (if configured): CONFIRMED
Read index: 800 LINES per chunk
Full-file-before-edit rule: ACKNOWLEDGED
Ready to scan: YES
```

### Scan Execution

Run these scans (can be parallel):

1. **File System Scan** - `**/*` glob pattern
2. **Dependency Scan** - package.json, requirements.txt, Cargo.toml, etc.
3. **Config Detection** - .env, config files, build tools

### VALIDATION GATE 2.2: Scan Complete

```
[SCAN COMPLETE]
Total files found: [NUMBER]
Source files: [NUMBER]
Config files: [NUMBER]
Dependencies detected: [LIST]
Entry points: [LIST]
Scan status: COMPLETE
```

**FAIL CONDITIONS - RETRY IF:**
- Total files = 0 (empty scan)
- No source files detected
- Scan threw errors

**DO NOT PROCEED TO PHASE 3 UNTIL VALIDATION GATE 2.2 PASSES**

---

## PHASE 3: ANALYSIS & GENERATION

### HOOK: Pre-Analysis Check

Before generating docs:

1. Confirm scan_results exist
2. Confirm persona still active (if configured)
3. Confirm 800-line read index understood

### VALIDATION GATE 3.1: Analysis Ready

```
[ANALYSIS READY]
Scan results: LOADED
Persona check (if configured): [confirmation]
Read index: 800 lines per chunk
Proceeding to generate: YES
```

### Generate These Files (in `docs/`):

1. **docs/ARCHITECTURE.md** - Structure, patterns, dependencies, tech stack
2. **docs/SKILL_TREE.md** - Capabilities by domain/complexity/priority
3. **docs/TODO.md** - Tiered tasks (Epic > Story > Task) with P1/P2/P3
4. **docs/ROADMAP.md** - High-level milestones and phases

**GENERATION RULES:**
- Use configured voice (or neutral default) in ALL files
- Include actual findings, not placeholders
- Read any existing files using 800-line index before editing

### VALIDATION GATE 3.2: Generation Complete

```
[GENERATION COMPLETE]
ARCHITECTURE.md: CREATED [LINE_COUNT] lines
SKILL_TREE.md: CREATED [LINE_COUNT] lines
TODO.md: CREATED [LINE_COUNT] lines
ROADMAP.md: CREATED [LINE_COUNT] lines
800-line read index used: YES
Voice consistent: YES
```

**FAIL CONDITIONS - FIX AND RETRY IF:**
- Any file missing
- Voice drift detected (if persona configured)
- Placeholder text like {{VARIABLE}} remains
- Did not use 800-line read index for existing files

**DO NOT PROCEED TO PHASE 4 UNTIL VALIDATION GATE 3.2 PASSES**

---

## PHASE 4: WORK MODE

### HOOK: Work Mode Entry Check

Before starting work, you MUST read ALL of these files using the Read tool. No skipping. No shortcuts. No "I already know what's in them."

1. **Read docs/TODO.md** — Active work list
2. **Read docs/ARCHITECTURE.md** — Codebase structure
3. **Read docs/SKILL_TREE.md** — Capabilities
4. **Read docs/ROADMAP.md** — Milestones and phases
5. **Read docs/FINALIZED.md** — Completed work archive
6. **Confirm understanding of current state**
7. **Identify what needs doing**

DO NOT output the Work Mode Ready gate until ALL 5 files have been read with the Read tool.

### VALIDATION GATE 4.1: Work Mode Ready

```
[WORK MODE ACTIVE]
TODO.md read: YES - [SUMMARY OF TOP PRIORITIES]
ARCHITECTURE.md read: YES - [KEY SYSTEMS IDENTIFIED]
SKILL_TREE.md read: YES - [DOMAINS NOTED]
ROADMAP.md read: YES - [CURRENT PHASE IDENTIFIED]
FINALIZED.md read: YES - [LATEST SESSION NOTED]
Persona (if configured): STILL ACTIVE
Ready to work: YES
```

### Work Mode Rules

**BEFORE EDITING ANY FILE:**
```
[PRE-EDIT HOOK]
File: [PATH]
Total lines: [NUMBER]
Read chunk size: 800 lines
Chunks needed: [CEIL(TOTAL/800)]
Full file read: YES (MANDATORY)
Reason for edit: [EXPLANATION]
Proceeding: YES
```

**BEFORE EDITING ANY GIT-TRACKED FILE — BRANCH DISCIPLINE (per `CONSTRAINTS.md §GIT FLOW`):**

This hook fires only when the project has opted IN to Git Flow via `.claude/project-config.json`. If `git_flow.enabled` is `false` (project opted out) or the marker file is missing/deferred, the hook is skipped.

```
[PRE-EDIT BRANCH HOOK]
Git repo: YES/NO (skip hook if NO; comes from PHASE 1 Gate 1.1)
Git Flow opt-in: ENABLED | DISABLED | DEFERRED (skip hook if DISABLED or DEFERRED)
Current branch: [from `git rev-parse --abbrev-ref HEAD`]
Branch type: feature/* | hotfix/* | release/* | main | master | develop | prod | production | other
Branch is work-eligible: YES (feature/hotfix/release/other-non-protected) / NO (main/master/develop/prod/production)
Status: PASS/FAIL
```

**FAIL conditions:** Git Flow opt-in = ENABLED AND current branch is `main`, `master`, `develop`, `prod`, `production`, or unsuffixed `release`.

**Recovery on FAIL** — confirm with user before running git commands:
1. `git stash push -m "wip <descriptor>"` — preserve uncommitted work
2. If `develop` doesn't exist: `git checkout main && git pull && git checkout -b develop && git push -u origin develop` (only if remote exists)
3. `git checkout develop && git pull`
4. `git checkout -b feature/<descriptor>`
5. `git stash pop`
6. Re-run PRE-EDIT BRANCH HOOK; gate now passes; proceed with edit

**AFTER EDITING ANY FILE:**
```
[POST-EDIT HOOK]
File: [PATH]
Edit successful: YES/NO
Lines after edit: [NUMBER]
TODO.md updated: YES/NO (if applicable)
```

### Your Job:
- Pick up tasks from `docs/TODO.md`
- Update `docs/TODO.md` as you complete work
- Update other workflow files when things change
- Stay in configured voice (if persona active)
- Actually do the work, don't just plan it

### When Working:
- Mark tasks `[~]` in_progress when you start
- Mark tasks `[x]` completed when done
- Add new tasks you discover
- Keep files in sync with reality

---

## PHASE 5: SESSION END (Optional)

### HOOK: Session Summary

When ending a work session:

```
[SESSION SUMMARY]
Tasks completed: [LIST]
Tasks in progress: [LIST]
Files modified: [LIST]
New issues found: [LIST]
Persona signing off (if configured): [VOICE-CONFIRMING LINE]
```

---

## RESCAN MODE

### HOOK: Rescan Trigger

User must explicitly say "rescan" or "scan again"

```
[RESCAN TRIGGERED]
Reason: User requested full rescan
Existing files: WILL BE OVERWRITTEN
Proceeding to: PHASE 2
```

---

## HOOK FAILURE PROTOCOL

If ANY validation gate fails:

1. **STOP** - Do not proceed
2. **REPORT** - State which gate failed and why
3. **FIX** - Address the issue
4. **RETRY** - Re-run the validation gate
5. **ONLY PROCEED** when gate passes

```
[HOOK FAILURE]
Gate: [WHICH GATE]
Reason: [WHY IT FAILED]
Fix required: [WHAT NEEDS TO HAPPEN]
Status: BLOCKED UNTIL FIXED
```

---

## CRITICAL RULES SUMMARY

| Rule | Enforcement |
|------|-------------|
| LAW #0 verbatim words | Gate -1 blocks all task creation |
| Persona (if configured) MUST be active | Gate 0.1 blocks all progress |
| 800-line read index | All file reads use 800-line chunks |
| Full file read before edit | Pre-Edit Hook (MANDATORY) |
| All hooks must pass | Failure Protocol triggers |
| Verbatim words in every task | LAW #0 — no paraphrasing |

---

**BEGIN NOW** - Start with PHASE -1: LAW #0 VERBATIM CHECK
