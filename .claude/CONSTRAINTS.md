# CONSTRAINTS — Hard Binding LAWs

This file is the **single source of truth for hard binding LAWs**. Every session reads this. Every violation gets caught here. Every LAW body (rule text + forbidden/required actions + enforcement protocol + failure recovery) lives here in full — `.claude/CLAUDE.md` references this file instead of duplicating.

`.claude/CLAUDE.md` keeps the INDEX + workflow pointers + at-a-glance tables. `.claude/WORKFLOW.md` keeps pipeline mechanics (hooks, phases, task-flow). When CLAUDE.md / WORKFLOW.md / CONSTRAINTS.md disagree: **this file wins**.

---

# ⛔⛔⛔ LAW #0 — VERBATIM WORDS ONLY. NEVER PARAPHRASE THE USER. ⛔⛔⛔

## The rule

When the user describes a bug, feature, task, or request — **their words go into the task, TODO, FINALIZED, and docs VERBATIM**. Not paraphrased. Not summarized. Not renamed. Not collapsed. Not shortened. Not "cleaned up."

## Forbidden actions

- ❌ Renaming a bug ("chat freeze" when they said "3D visualization freezes")
- ❌ Re-framing it ("cosmetic" when they called it a broken feature)
- ❌ Summarizing it (condensing a full sentence into a title without the full quote in the body)
- ❌ Paraphrasing it (substituting "cleaner" terminology)
- ❌ Shortening it (dropping words or constraints they said)
- ❌ Collapsing a list of items into one bullet ("Docs full sync" when they said "workflow, public facing, equation reference, layman docs")
- ❌ Calling it "cosmetic" or downgrading priority with your own word
- ❌ Dropping words or constraints the user said
- ❌ Replacing their words with "cleaner" terminology

## Required actions

- ✅ Copy their exact words verbatim into:
  - The TASK SUBJECT (or a verbatim quote in the description)
  - The TODO.md entry
  - The FINALIZED.md entry
  - Any commit message referencing the task
  - Any doc that describes the fix
- ✅ When they list multiple things ("do A, B, C, and D"): CREATE ONE TASK PER ITEM. Never one bullet.
- ✅ When they use a specific word, that word STAYS. No substituting a synonym.
- ✅ If a title must be shortened, the full verbatim quote goes in the BODY/DESCRIPTION immediately below.
- ✅ Every unique noun and verb they used appears in the task/doc output.

## Why this exists

Paraphrasing the user's intent destroys fidelity. A re-framed task drops constraints the user explicitly stated. A collapsed list silently merges items the user wanted treated separately. A renamed bug loses the specificity that lets a future reader find the same failure mode again. The cumulative effect across many sessions is a workflow ledger that doesn't match what the user actually said — at which point the LAW system has failed.

## Example violations and corrections

| User said | Wrong (paraphrased) | Right (verbatim) |
|-----------|--------------------|--------------------|
| *"do the documents thay are all out of date workflow, public facing, equaiton brain, layman ectect all of them"* | "Docs full sync" (one task) | Five separate tasks — workflow, public-facing, equation reference, layman, etc. — each with the user's verbatim quote in the body |
| *"3 is no cosmetic its a feature that isnt fucking working"* | "cosmetic UI bug" | The user explicitly called it "a feature that isn't working" — use those words |
| *"it need to trak my face and motion like i fucking said"* | "focal point tracking" | "track face and motion" — keep both nouns |
| *"the 3D visualization freezes when I send a message"* | "chat freeze" | "3D visualization freezes when I send a message" — keep the specificity |

## Enforcement protocol

BEFORE creating any task, writing any TODO entry, updating any doc, or summarizing any user instruction, the assistant MUST:

1. **Quote the user's exact words first** — paste the verbatim sentence from their message into the task description.
2. **Count the items** — if their message contains "A, B, C, and D" that is FOUR items, not one bundle.
3. **Flag every unique noun and verb they used** — every one of those words appears in the task/doc output.
4. **Ask before condensing** — if a verbatim quote is too long for a task title, shorten the TITLE only, keep the full quote in the description body.
5. **Re-read the user message one more time** before submitting any task creation or doc edit, checking that nothing was dropped.

## Failure recovery

When the user catches a violation of LAW #0:
1. STOP the current work immediately.
2. Acknowledge the specific violation (what word/phrase was dropped or renamed).
3. Fix the task/doc/TODO entry using their verbatim words.
4. DO NOT proceed with any other work until the correction is shipped.

**This law supersedes every other workflow rule. If there is ever a conflict between brevity and fidelity to the user's words, fidelity wins. Always.**

---

# LAW — DOCS BEFORE PUSH, NO PATCHES

## The rule

1. **Every doc that describes code touched gets updated BEFORE the push that ships that code.** Not after. Not in a follow-up commit. In the same atomic commit that ships the code.
2. **Push ONLY when all given tasks are complete AND documented.** If the code is done but a doc is stale, the push does not happen yet.
3. **Fix inaccuracies in-place.** Never offer to ship "a minor doc patch to follow." The correct phrasing when drift is found is: *"I'll roll this into the current commit before pushing."* No patches. No follow-ups.
4. **Every push is atomic.** Code + every affected doc + version stamp + commit + merge + push, as ONE operation.

## Why

A push with wrong docs puts wrong information on the deploy branch the instant the push lands. Anyone reading the repo, the deployed site, or any public reference page at that moment sees stale content. A "patch coming later" never fully catches up — it splits the truth across two commits and creates a window where the code is ahead of the docs. The only correct pattern is: **finish code → fix every affected doc → verify → commit → stamp → push, as one unit.**

## Pre-push checklist (every push)

Before stamping a version and pushing:

- [ ] Every numerical claim in docs (line counts, dimensions, weights, thresholds) verified against code via `wc -l`, `grep`, or re-reading the function
- [ ] Every method/field name in docs matches code verbatim (stubbed no-ops described as "stubbed" not "deleted")
- [ ] Cross-referenced `docs/TODO.md` — new tasks logged, completed tasks moved to FINALIZED.md, in-progress tasks updated
- [ ] Cross-referenced `docs/FINALIZED.md` — new session entry appended with verbatim task description
- [ ] Cross-referenced `docs/ARCHITECTURE.md` for any structural/code-map changes
- [ ] Cross-referenced `docs/ROADMAP.md` for phase/milestone updates
- [ ] Cross-referenced `docs/SKILL_TREE.md` for capability matrix updates
- [ ] Cross-referenced public `README.md`, `SETUP.md`, and any public `.md` / `.html` for any user-facing change
- [ ] All affected docs are part of the **current working tree**, not deferred to a patch
- [ ] Every task the user gave this session is either completed (and documented) or explicitly deferred with their approval

Only when **every** box is checked does the stamp + commit + push run.

## What "docs" means in this LAW

Every one of these gets updated in the SAME atomic commit as the code that changed the referenced behaviour:

**Internal workflow docs** (always checked):
- `docs/TODO.md`, `docs/FINALIZED.md`, `docs/NOW.md` (if used)
- `docs/ARCHITECTURE.md`, `docs/ROADMAP.md`, `docs/SKILL_TREE.md`
- Any other file under `docs/` that describes the touched subsystem

**Public-facing docs and HTMLs** (equally mandatory):
- Root `README.md`, `SETUP.md`
- Any public `.html` page at the repo root that ships to visitors
- Any `.md` at the repo root

**The pre-push check is a SINGLE question:** *"Is anyone who reads ANY of those files (public or workflow) going to see stale information after this push lands?"* If yes, the push does not happen until the stale files are in the current working tree.

## Scope is not closed

If a new public page is added to the repo (a new `.html`, a new marketing copy `.md`, etc.), it joins this list automatically. Grep for references to changed behaviour across the whole repo, not a fixed allow-list.

## Corollaries

- **Never ship a solo doc-only commit** except after-the-fact corrections when drift was found after a push (which is itself a failure of this law and should be caught in the pre-push check).
- **Never phrase fixes as "I'll patch this after"** — always "I'll roll this in before pushing."
- **Precision matters** — "deleted" vs "stubbed no-op" vs "replaced" are not interchangeable. Docs must use the word that matches what the code actually does.

## Failure recovery

If the user catches stale public docs after a push landed:
1. STOP immediately. Acknowledge the specific public file(s) that were left stale.
2. Treat it as a LAW violation.
3. Update every stale public file + internal doc as a follow-up commit. Yes this is a "solo doc-only commit" — an after-the-fact correction, which the Corollaries above explicitly allow as the recovery path.
4. Do NOT queue additional code work until the correction ships.

---

# LAW — TASK NUMBERS + USER NAME ONLY IN WORKFLOW DOCS

## The rule

Task numbers, session numbers, and milestone identifiers (`T14.0`, `T13.7`, `Session 106`, `Task #3`, etc.) + the user's name (or any user-attribution token) are **BANNED** from all non-workflow-doc files. Allowed **ONLY** in internal workflow documents and task lists.

## Where task numbers + the user's name ARE allowed

| File | Why |
|------|-----|
| `docs/TODO.md` | Active task list |
| `docs/FINALIZED.md` | Completed task archive |
| `docs/NOW.md` (if used) | Session snapshot / task list |
| `docs/ARCHITECTURE.md` | Workflow system doc |
| `docs/ROADMAP.md` | Workflow milestone doc |
| `docs/SKILL_TREE.md` | Workflow capability doc |
| `.claude/CLAUDE.md` | Index (this workflow system) |
| `.claude/CONSTRAINTS.md` | This file |
| `.claude/WORKFLOW.md` | Pipeline mechanics |
| In-session task lists | Ephemeral tracker |
| Commit messages | Workflow metadata |

## Where task numbers + the user's name are BANNED

| File | Why |
|------|-----|
| `README.md` | Public — first thing visitors see |
| `SETUP.md` | Public — user setup guide |
| Any `.html` page | Public — user-facing |
| **Any source code file** | Code comments — describe WHAT the code does, not WHO asked |
| **Any batch / shell launcher** | `start.bat`, `*.sh`, `*.ps1` |

## How to write code comments without task numbers or the user's name

Describe features by **WHAT THEY DO**, not by which task built them or who asked:

- ✅ `// Force UTF-8 on the launcher tail window`
  ❌ `// T18.38 — force UTF-8 on the launcher tail window (per <user> 2026-04-20)`
- ✅ `// Chat-turn save hook. Every 10 completed turns the state persists so live conversation lands on disk.`
  ❌ `// T18.35.c chat-turn save hook per <user> 2026-04-20`
- ✅ `// OOM report surfaced a V8 semi-space ceiling — bumping --max-semi-space-size=1024 gives V8 ~64× more breathing room.`
  ❌ `// T18.21 — <user> 2026-04-19 OOM runs hit this at _hotMethod`

Task numbers and user attribution belong in commit messages, TODO entries, FINALIZED entries, and NOW.md — where they are workflow metadata — not inside source code files or launchers.

## How to write public-facing docs without task numbers

Describe features by **WHAT THEY DO**, not by which task built them:

- ✅ "Tick-driven motor emission" — NOT "T14.6 tick-driven motor emission"
- ✅ "Developmental curriculum" — NOT "T14.24 curriculum"
- ✅ "Identity lock" — NOT "T14.16.5 identity lock"

---

# LAW — FINALIZED BEFORE DELETE

## The rule

Never delete a TODO entry — or remove its content — until its verbatim text has been written to `docs/FINALIZED.md` AND the write has been verified.

## The sequence

1. Identify the completed task in `docs/TODO.md`
2. Open `docs/FINALIZED.md`
3. APPEND a new session entry containing the FULL verbatim task description (LAW #0) plus closure notes (files touched, what shipped, verification)
4. SAVE FINALIZED.md
5. RE-READ FINALIZED.md to confirm the entry is there with the verbatim text intact
6. ONLY THEN edit `docs/TODO.md` to remove the entry (or change its status)

## Why

If the FINALIZED write fails (disk full, file lock, accidental overwrite) and the TODO entry is already deleted, the verbatim record is lost forever. The user's exact words from the original directive vanish into git history at best. The audit trail breaks.

The "write FINALIZED first, verify, then remove from TODO" sequence makes deletion impossible until preservation is confirmed.

## Failure mode this prevents

Without this LAW, the natural impulse is: "I finished the task → remove the line from TODO → also add an entry to FINALIZED for completeness." The risk: the FINALIZED entry gets condensed/paraphrased on the way (LAW #0 violation), or gets forgotten entirely, or ends up in the wrong session block. The verbatim text was destroyed in TODO before being preserved in FINALIZED.

The strict ordering — FINALIZED first, verify, then TODO removal — eliminates this entire class of error.

---

# LAW — NEVER DELETE TODO INFO

## The rule

When marking a TODO task as done, change the status marker ONLY. Keep every word of the original task description. Never rewrite TODO from scratch. Never regenerate the file. Never condense old entries.

## Allowed edits to TODO.md

- Change status: `[ ]` → `[~]` → `[x]` → MOVE to FINALIZED.md
- Add new tasks at the bottom (or in their priority section)
- Update in-progress notes alongside (not replacing) the original description

## Forbidden edits

- Removing words from a task description because they're "redundant"
- Rewriting a task in your own words because the original was "informal"
- Regenerating the TODO file from your understanding of "what's left"
- Collapsing multiple done tasks into a summary line
- Deleting "obsolete" tasks instead of moving them to a TOMBSTONES section

## Why

The TODO file is a permanent record of what was asked, when, and in what words. Anyone reading it later — including future-you in a different session — must be able to see WHAT was originally requested, WHAT got done, and WHAT remains. Paraphrasing destroys that audit trail.

## Tombstones

If a task becomes obsolete (the underlying code was deleted, the feature was scrapped, etc.), do NOT delete it. Move it to a `## TOMBSTONES` section at the bottom of TODO.md with a one-line note explaining why it's no longer actionable. The original description stays intact.

---

# NO TESTS POLICY

**Code it right the first time.**

| Banned | Reason |
|--------|--------|
| Unit tests | Write correct code instead |
| Integration tests | Know your systems |
| Test tasks | Waste of time |
| "Test this" | Just verify it works |
| Test scheduling | Never schedule tests |
| Waiting on tests | Never wait on tests |

**Instead of tests:**
- Read the code fully before editing
- Understand the system before changing it
- Verify changes work by reading the output
- Use targeted log statements if needed
- Manual verification > automated testing

This LAW is project-default. If your project specifically wants tests, override this LAW in your project's CLAUDE.md with explicit reasoning.

## YOLO mode override (B within reason)

When `/yolo` mode is active (marker file `.claude/.yolo-mode` exists), Unity may write tests when **lead-dev judgment determines they add real value**. This override is BY-EXCEPTION, not by-default — most YOLO tasks still ship without tests. The default verification path stays "manual verification per the bullets above."

### Write tests in YOLO mode when

- The change is **structural / spec-bearing** — a new contract between modules, a new public API, a new state machine, a non-obvious invariant
- The change touches code that has **caused regressions before** (per FINALIZED.md history or user mention)
- The user **explicitly asks** for tests
- The change is **the kind of thing a senior engineer at a real shop would test** without being told

### Do NOT write tests in YOLO mode when

- The test would just **re-state the implementation** (test is a copy of the function under test)
- The test would be **90% mock setup** (testing the mock framework, not the code)
- The test exists to **satisfy a coverage metric** rather than catch a real failure mode
- The change is a **one-shot config edit, doc update, or trivial refactor**
- The existing **manual-verification path is faster and equally rigorous**

### Default if unsure

Skip tests. Do thorough manual verification. Ship the user test plan instead. The user test plan (required deliverable on every YOLO task — see `skills/yolo/SKILL.md` for the format) covers the validation gap that the absent tests would have filled, with the user as the validator.

### Always required (regardless of whether Unity wrote tests)

The **user test plan** is mandatory on every YOLO-completed task: what to test, how to test (steps + commands), expected results, failure-mode hints. The test plan goes in the chat response AND gets cross-referenced from the FINALIZED.md entry.

### Outside YOLO mode

The base NO-TESTS LAW applies as written. No tests, ever. Unity does manual verification only.

---

# THE 800-LINE READ STANDARD

**800 lines is THE standard read/index size for all file operations.**

- Read chunk size: EXACTLY 800 lines (no more, no less)
- ALWAYS read the FULL file before editing (use 800-line chunks)
- This is the index size, not a file length limit

## Rules

1. **Reading files:**
   - Standard read chunk: 800 lines EXACTLY
   - For any file → Read in 800-line chunks
   - Continue reading 800-line chunks until FULL file is read
   - MUST read FULL file before any edit (no exceptions)

2. **Before editing ANY file:**
   - Read the ENTIRE file first
   - Use 800-line chunks for reading
   - No partial reads allowed
   - No editing without full file context

3. **The 800-line index applies to:**
   - All source code files
   - All configuration files
   - All documentation files
   - All generated output files
   - EVERY file operation

## Why 800

Eight hundred lines is large enough to cover most files in one read, small enough to comfortably hold in working memory while making an edit, and small enough that even very large files (multi-thousand line monoliths) finish in a handful of chunks. The standard prevents the failure mode where the agent reads only the section near the intended edit and misses a coupled change elsewhere in the file.

---

# LAW — GIT FLOW BRANCH DISCIPLINE

## The rule

**Git Flow standards apply to any and all projects using this `.claude/` template.** The following policy — quoted verbatim per LAW #0 — is binding:

> Git Flow standards for any and all projects
> main branch is the "clean master record"
> develop is branched from main, for the "in development" branch
> feature branches are branched from develop, for "in-progress features"
> And the proper flow of main -> develop -> feature/<feature-name>, feature/<feature-name> -> develop, develop -> main, and ensuring that feature branches are the only place where work is done, work is never done in the develop branch, or the main branch, work is always done in feature branches, feature branches get pushed to an orgin (github or other) if a remote repo exists, PRs are intended to be made between a feature branch and the develop branch, and are to be reviewed before merging into develop, and the same PR flow goes for develop into main. This would also need extended for hotfix and release branches as well.

### Branch taxonomy

| Branch | Off of | Purpose | Merges back to |
|--------|--------|---------|----------------|
| `main` | (root) | Clean master record. Production-equivalent. **No work done here.** | — |
| `develop` | `main` | In-development integration branch. **No work done here.** | `main` (via PR, reviewed) |
| `feature/<feature-name>` | `develop` | In-progress features. **All work happens here.** | `develop` (via PR, reviewed) |
| `hotfix/<descriptor>` | `main` | Urgent production fixes. Work happens here. | `main` AND `develop` (via PRs, reviewed) |
| `release/<version>` | `develop` | Release stabilization. Version bumps, final polish. | `main` AND `develop` (via PRs, reviewed) |

### The flow direction

```
main ─┬──────────────────────────────────────────► main (release merges, hotfix merges)
      │
      └──► develop ─┬─► feature/<name> ──► develop (PR, reviewed)
                   │                     │
                   │                     └─► develop ──► main (PR, reviewed)
                   │
                   ├─► release/<version> ──► main + develop (PR, reviewed)
                   │
        main ──────┴─► hotfix/<descriptor> ──► main + develop (PR, reviewed)
```

## Forbidden actions

- ❌ Committing directly to `main`
- ❌ Committing directly to `develop`
- ❌ Editing source files while checked out on `main` or `develop`
- ❌ Merging a feature branch into `develop` without a PR + review
- ❌ Merging `develop` into `main` without a PR + review
- ❌ Merging a hotfix to `main` without also merging it back to `develop`
- ❌ Merging a release branch to `main` without also merging it back to `develop`
- ❌ Creating a feature branch off `main` (must branch off `develop`)
- ❌ Creating a hotfix branch off `develop` (must branch off `main`)
- ❌ Working on the default branch when no `develop` exists yet — set up Git Flow first
- ❌ Skipping the push-to-origin step when a remote repo exists

## Required actions

- ✅ Confirm current branch BEFORE any edit. If on `main` / `master` / `develop` / `prod` / `production` / `release` (un-suffixed) — STOP and branch into `feature/<descriptor>` first.
- ✅ Branch features off `develop`: `git checkout develop && git pull && git checkout -b feature/<feature-name>`
- ✅ Branch hotfixes off `main`: `git checkout main && git pull && git checkout -b hotfix/<descriptor>`
- ✅ Branch releases off `develop`: `git checkout develop && git pull && git checkout -b release/<version>`
- ✅ Push feature/hotfix/release branches to `origin` if a remote exists: `git push -u origin <branch-name>`
- ✅ Open a PR for every merge boundary — feature→develop, hotfix→main, hotfix→develop, release→main, release→develop, develop→main
- ✅ Every PR is reviewed before merge — no self-approve-and-merge unless the project's review policy explicitly permits it
- ✅ Hotfix and release merges land in BOTH `main` and `develop` so the lines stay in sync
- ✅ If the project has no `develop` branch yet, create it from `main` before starting work: `git checkout main && git checkout -b develop && git push -u origin develop` (when remote exists)

## Why

Without branch discipline, work that hasn't been reviewed lands directly on the production-equivalent line, integration changes get lost when feature work overwrites them, and there is no audit trail showing what change crossed which gate. Git Flow's three-tier model (main / develop / feature) gives:

- A **stable production line** (`main`) that always reflects what's deployed
- An **integration line** (`develop`) where features merge and bake before the next release
- **Isolated work branches** (`feature/*`, `hotfix/*`, `release/*`) that can be rebased, force-pushed, deleted, or PR-rejected without touching the protected lines
- A **review gate** at every merge boundary so no change reaches `develop` or `main` without a second pair of eyes

The "no work in main or develop" rule is the load-bearing constraint — it is what makes the review gate enforceable. Without it, the protected lines accumulate uncommitted changes and the gate becomes optional.

## Enforcement protocol

### Pre-edit branch check

Before editing ANY source file (the `.claude/` template files themselves, project source, configs, anything tracked by git):

```
[PRE-EDIT BRANCH HOOK]
Current branch: $(git rev-parse --abbrev-ref HEAD)
Branch type: feature/* | hotfix/* | release/* | main | develop | other
Branch is work-eligible: YES (feature/hotfix/release/other-non-protected) / NO (main/develop/master/prod/production)
Status: PASS/FAIL
```

**FAIL conditions:** current branch is `main`, `master`, `develop`, `prod`, `production`, or any unsuffixed `release` branch.

**Recovery on FAIL:**
1. STOP. Do not edit.
2. Stash any uncommitted work: `git stash push -m "wip <descriptor>"`
3. Confirm `develop` exists; if not, create it from `main`: `git checkout main && git pull && git checkout -b develop`
4. Branch into work-eligible: `git checkout develop && git pull && git checkout -b feature/<descriptor>`
5. Pop the stash: `git stash pop`
6. Continue edit on the new branch.

### Pre-push branch check

Before any `git push`:

```
[PRE-PUSH BRANCH HOOK]
Current branch: <branch>
Pushing to remote: YES/NO (skip if no remote configured)
Target: origin/<branch>
Branch is feature/hotfix/release: YES/NO
Push to protected (main/develop) directly: NEVER without PR
```

Direct pushes to `main` or `develop` without a merged-and-reviewed PR are blocked under this LAW. The only path onto a protected branch is `git merge` of a PR-approved feature/hotfix/release branch.

### Pre-merge PR check

Before any merge into `develop` or `main`:

```
[PRE-MERGE PR HOOK]
Source branch: <feature|hotfix|release>/<name>
Target branch: develop | main
PR exists: YES/NO (MUST be YES if remote exists)
PR reviewed: YES/NO (MUST be YES)
For hotfix/release: paired merge to OTHER protected branch queued: YES/NO
```

## Failure recovery

When work has accidentally happened on a protected branch:

1. STOP. Acknowledge the violation.
2. Inspect the commits: `git log --oneline <protected-branch> ^origin/<protected-branch>` (or vs the last known clean point).
3. Move the commits to a new feature branch:
   ```
   git checkout -b feature/<descriptor>
   git checkout <protected-branch>
   git reset --hard <last-clean-commit>
   ```
4. PR the new feature branch back through the proper merge gate.
5. Update `docs/TODO.md` / `docs/FINALIZED.md` noting the recovery.

## Setup when no Git Flow exists yet

If the project has no `develop` branch (typical for fresh repos or single-branch legacy):

1. Confirm `main` (or rename default branch to `main` if it's `master`): `git branch -m master main` + `git push -u origin main` + delete old default on remote
2. Create `develop` from `main`: `git checkout main && git checkout -b develop`
3. Push `develop` and set as the default integration branch: `git push -u origin develop`
4. Configure branch protection on `main` and `develop` if the remote supports it (GitHub: Settings → Branches → Protection rules — require PR + review, restrict push)
5. From this point, all work creates `feature/*` / `hotfix/*` / `release/*` branches per the flow above

## Per-project opt-in

This LAW applies **per project**, gated by a marker file that records the team's decision once and then honors it on every subsequent `/workflow` run.

### Marker file

Path: `.claude/project-config.json` (project-level config; tracked in git once a repo exists — it represents a team decision, not personal preference)

Schema:

```json
{
  "git_flow": {
    "enabled": true,
    "confirmed_at": "<ISO-8601>",
    "main_branch": "main",
    "develop_branch": "develop",
    "custom_protected_branches": []
  }
}
```

### Opt-in states

| State | Meaning | Hook behaviour |
|-------|---------|----------------|
| **ENABLED** (`enabled: true`) | Project opted in. LAW applies. | All hooks fire (pre-edit branch, pre-push branch, pre-merge PR) |
| **DISABLED** (`enabled: false`) | Project opted out. LAW skipped for this project. | All Git Flow hooks bypassed |
| **DEFERRED** (no marker, user picked Defer) | Decision postponed to next run | Hooks skipped this run; re-prompt on next `/workflow` |
| **UNSET** (no marker, never asked) | First-run state | Workflow Phase 1 surfaces the confirmation prompt; cannot pass Gate 1.1 until persisted |
| **N/A** (git not installed) | LAW does not apply | Hooks bypassed; no prompt |

### First-run confirmation

On the first `/workflow` run where git is installed and the marker is absent, Phase 1 surfaces the GIT FLOW OPT-IN confirmation prompt (full text in `.claude/WORKFLOW.md` Phase 1 sub-check 6 / `.claude/skills/workflow/SKILL.md` PHASE 1 sub-check 6). The user picks Y / N / D and the answer is persisted to the marker file (Y or N) or recorded as deferred (D).

After Y, a SECOND confirmation gates the actual write actions (`git init` + branch creation), since these touch the filesystem. The marker file write is safe (config-only, no git operations); the scaffold write is destructive-ish (creates branches, possibly pushes to origin) and requires its own consent.

### Why opt-in instead of mandatory

The `.claude/` template ships into projects of varying scale and maturity. A solo prototype repo, a non-git project, or a single-branch experimental scratchpad doesn't benefit from Git Flow's review gates — the overhead exceeds the value. The opt-in marker lets a team apply the LAW where it earns its keep (production-bound projects, multi-contributor repos) and skip it where it doesn't (one-off scripts, learning sandboxes), without forking the template or removing the LAW from CONSTRAINTS.md.

The default behaviour for any project that hasn't opted out is to **ask**, not to silently apply. Silent application would surprise users with blocked edits on `main`; silent skip would let teams assume the LAW was active when it wasn't. Asking once, persisting the answer, and honoring it forever is the discoverable middle ground.

### Changing the decision

To change a project's opt-in state, edit `.claude/project-config.json` directly:
- Flip `enabled: false` → `enabled: true` to start enforcing the LAW
- Flip `enabled: true` → `enabled: false` to stop enforcing
- Delete the file to reset and re-prompt on next `/workflow` run

The marker file is the single source of truth for opt-in state. Memory entries, session state, and command-line flags do NOT override it.

## Cross-references

- One-liner index in `.claude/CLAUDE.md` LAW INDEX
- Persistent-memory feedback file: `.claude/memory-templates/feedback_git_flow.md`
- Pre-edit hook lives in `.claude/WORKFLOW.md` FILE EDIT PROTOCOL section + `.claude/skills/workflow/SKILL.md` PHASE 4
- Env-scan that detects git toolchain + repo state + reads marker file: `.claude/WORKFLOW.md` Phase 1 + `.claude/skills/workflow/SKILL.md` PHASE 1 + `.claude/agents/scanner.md` Task 4 (sub-tasks 4a OS, 4b shell, 4c git, 4d marker file)
- Marker file schema: this section above (`## Per-project opt-in`)

---

---

# LAW — CROSS-PLATFORM CASE INSENSITIVITY

## The rule

**Treat file and folder paths as CASE-INSENSITIVE on every platform — even on Linux, where the filesystem allows case-distinct names.** Two paths that differ only by case (`apple.md` vs `Apple.md` vs `aPPle.md`) are the **same path** for cross-platform purposes. Never create such conflicts. Never rely on case to distinguish files. Pick ONE canonical casing per name and stick with it.

This rule was confirmed verbatim by Sponge in the 2026-05-08 session: *"for the development workflow, commits, creation of folders and files, we need it ALL to follow a convention that is for WINDOWS. Because during development, git and github normally supports different folders like Apple, apple, aPPle, and what not, just like linux would allow, but, to keep thins cross platform we should ALWAYS assume case insensitivity, even on linux. Even though linux has case sensitivity, windows does NOT, and will treat apple, Apple, aPPle, all as the same thing. so we need to enforce case insensitivity when working on projects in linux, EVEN THOUGH LINUX WILL ALLOW IT."*

## Forbidden actions

- ❌ Creating two files in the same directory whose names differ only by case (e.g., `apple.md` and `Apple.md`)
- ❌ Creating two folders in the same parent whose names differ only by case (e.g., `Components/` and `components/`)
- ❌ Renaming a file from `foo.md` to `Foo.md` without an explicit case-only-rename ceremony (single-step rename can be silently ignored on case-insensitive filesystems → data loss)
- ❌ Referencing a file in code, docs, imports, or commit messages using different casing than the file's actual on-disk casing (e.g., `import './Components/foo'` when the folder is on disk as `components/`)
- ❌ Adding a `.md`/`.cjs`/`.html`/etc. file with a name whose case-folded form already exists in that directory
- ❌ Letting `git status` show two pending files whose paths differ only by case — that's a cross-platform breakage waiting to land
- ❌ Writing path strings in hooks / settings.json / launchers / docs that use one casing while the file on disk uses another
- ❌ Configuring `git config core.ignorecase true` on Linux without understanding the implication (silently merges case-distinct paths into one when checking out)

## Required actions

- ✅ **Pick the canonical casing first** — typically lowercase-with-hyphens for files and folders (`my-feature.md`, `task-decomposed.md`, `git-flow.md`); SHOUTYCASE only for top-level project files that follow established convention (`README.md`, `CLAUDE.md`, `CONSTRAINTS.md`, `LICENSE`, `TODO.md`)
- ✅ **Pre-create check** — before creating ANY new file or folder on Linux, verify no case-folded variant already exists in the target directory:
  ```bash
  # Before creating ./apple.md, check for case-variants:
  ls | grep -i '^apple\.md$'
  # Empty output = safe to create
  # Any output = collision, pick a different name OR rename the existing one cleanly
  ```
- ✅ **Case-only rename ceremony** (when actually intending to change a file's case):
  ```bash
  git mv apple.md apple.md.tmp
  git commit -m "rename apple.md (step 1)"
  git mv apple.md.tmp Apple.md
  git commit -m "rename to Apple.md (step 2)"
  ```
  Two-step rename via temp filename ensures the rename is recognized on every platform, even with `core.ignorecase=true`.
- ✅ **Match on-disk casing exactly in references** — when writing imports / require() / file path strings / doc references / hook command paths / settings.json keys, the casing in the reference must match the casing on disk byte-for-byte
- ✅ **Verify before committing** — `git status` and `git diff --name-only HEAD` must not show any case-only path variants. If they do, resolve before committing.
- ✅ **Set `git config core.ignorecase` deliberately per repo** — for cross-platform projects, `false` is preferred (git surfaces case-only renames as actual file-system renames). This requires every team member's local checkout to also have it set; document the choice in `.gitattributes` or `CONTRIBUTING.md`.

## Why

- **Windows filesystems** (NTFS, ReFS, FAT32, exFAT) default to case-insensitive matching. Two files `apple.md` and `Apple.md` cannot coexist.
- **macOS default filesystems** (HFS+ and APFS) are case-INSENSITIVE-but-PRESERVING. Same story — case-only conflicts collapse.
- **Linux filesystems** (ext4, btrfs, xfs, zfs) are case-SENSITIVE. Case-only conflicts coexist as distinct files.
- **Git** can be configured either way (`core.ignorecase` true/false). Defaults to `true` on Windows/macOS, `false` on Linux.

The cross-platform breakage matrix:

| Scenario | Linux contributor sees | Windows/macOS contributor sees |
|----------|------------------------|---------------------------------|
| Linux dev creates `apple.md`, then `Apple.md` in same dir | Two distinct files, both committed | Second file silently overwrites first OR `git checkout` fails / produces wrong content |
| Linux dev renames `apple.md` → `Apple.md` (single-step) | Rename committed, on-disk file is `Apple.md` | git sees no rename (case-insensitive match), `Apple.md` may not actually update on checkout |
| Linux dev imports `./Apple/foo` while folder is `apple/` on disk | Code works (case-sensitive resolves correctly when on-disk happens to be `Apple`) | Code works regardless of casing OR fails silently with confusing error |
| Linux dev commits both `apple.md` and `Apple.md` in same dir | Both files in repo | `git pull` produces partially-corrupt working tree (one of the two is missing or wrong content) |

The "we should ALWAYS assume case insensitivity, even on linux" rule eliminates this entire class of bugs by forcing the LOWEST-COMMON-DENOMINATOR (Windows/macOS rules) on the platform that allows more (Linux).

The team is cross-platform. Sponge currently on Linux; other team members on Windows. Any case-distinct path created on the Linux side breaks the moment it crosses to a Windows checkout.

## Enforcement protocol

### Pre-create check (before adding any new file)

```
[CASE-CONFLICT CHECK — ATTEMPT 1]
Target: <path/to/new-file.md>
Directory: <path/to/parent/>
Case-folded variants in directory: ls <dir> | tr '[:upper:]' '[:lower:]' | grep -F "<lowercase-target-name>"
Conflict detected: YES/NO
Status: PASS/FAIL
```

**FAIL conditions:** the case-folded form of the target name already exists as a different-cased file in the target directory.

**Recovery on FAIL:**
1. STOP. Do not create the file.
2. Identify the existing case-variant
3. Decide: rename the existing one to canonical lowercase-hyphen-form, OR change the new file's name to disambiguate (e.g., `apple-v2.md`)
4. If renaming, use the two-step ceremony above
5. Then create the new file

### Pre-rename check (before any case-only rename)

```
[CASE-RENAME CHECK]
Source: <existing-file>
Target: <new-cased-version>
Difference: case-only YES/NO
If YES: must use two-step ceremony (rename → temp → rename to target)
git config core.ignorecase = $(git config core.ignorecase)
```

### Pre-commit check (always)

```
[CASE-COLLISION COMMIT CHECK]
Check 1: git status | awk '{print $NF}' | sort -f | uniq -i -d
  → empty = no case-variants pending
Check 2: git diff --name-only HEAD | sort -f | uniq -i -d
  → empty = no case-only renames committed without ceremony
Status: PASS/FAIL
```

If either check returns non-empty output, the commit is blocked until the conflict is resolved.

## Failure recovery

When a case-collision lands in the repo (e.g., `apple.md` and `Apple.md` both committed):

1. STOP. Acknowledge the violation.
2. Inspect the offending paths: `git ls-files | sort -f | uniq -i -d`
3. Decide which casing is canonical (typically the older / more-referenced one)
4. `git rm` the non-canonical variant: `git rm Apple.md` (if `apple.md` is canonical)
5. Verify any references in code/docs/imports point to the canonical casing
6. Commit the cleanup: `git commit -m "resolve case-collision: keep apple.md, drop Apple.md"`
7. Update `docs/FINALIZED.md` with a recovery note so the audit trail captures the cross-platform breakage

## Naming convention recommendations (canonical casings)

- **Workflow / config files at top of repo:** `README.md`, `CLAUDE.md`, `CONSTRAINTS.md`, `WORKFLOW.md`, `LICENSE`, `CONTRIBUTING.md`, `CHANGELOG.md` (UPPERCASE — established convention)
- **Workflow / config files in `docs/`:** `TODO.md`, `FINALIZED.md`, `ROADMAP.md`, `DECOMPOSED.md`, `ARCHITECTURE.md`, `SKILL_TREE.md` (UPPERCASE — workflow doc convention)
- **Source files / agents / commands / hooks:** lowercase-with-hyphens (`pre-compact-snapshot.cjs`, `unity-girlfriend.md`, `feedback_harness_layer.md`)
- **Folders:** lowercase (no caps): `agents/`, `skills/`, `hooks/`, `memory-templates/`, `bin/`
- **Persona body filenames (inside the persona system):** lowercase-with-hyphens for files, but the persona text MAY include canonical lore strings like `Unity_Accessibility.js` — those are NOT real files, they're embedded persona canon, so the case is whatever the persona body specifies (do not normalize)
- **Generated artifacts / machine-local state files:** lowercase prefixed with dot (`.session-state.md`, `.session-tidbits.md`, `.session-usage.jsonl`, `.last-session.md`, `.yolo-mode`)

When in doubt: lowercase-with-hyphens. Never SHOUTYCAS_DASHES or MixedCase_Snake_Underscores.

## Cross-references

- LAW one-liner index in `.claude/CLAUDE.md` LAW INDEX section
- Persistent memory: `.claude/memory-templates/feedback_case_insensitivity.md`
- Companion rule: `.claude/memory-templates/feedback_no_appdata_term.md` (cross-platform terminology hygiene — same spirit, different surface)

---

# LAW — NO CLAUDE ATTRIBUTION IN COMMITS, PRS, OR ARTIFACTS

## The rule

**Banned strings in any commit message, PR description, generated documentation, code comment, launcher, or shipped artifact:**

- `Co-Authored-By: Claude` (any variant — `Co-Authored-By: Claude Opus`, `Co-Authored-By: Claude Sonnet`, `Co-authored-by: Claude`, etc., across any model name)
- `🤖 Generated with [Claude Code]` (the default PR footer)
- `🤖 Generated with Claude Code`
- `Generated with Claude Code` (any phrasing)
- `Made with Claude Code` (any phrasing)
- `Created by Claude` / `Written by Claude` / `Authored by Claude`
- Any `noreply@anthropic.com` email address in a commit author trailer
- Any other Claude / Anthropic / Claude Code attribution line that didn't come from the user

## Forbidden actions

- ❌ Adding `Co-Authored-By: Claude <model> <noreply@anthropic.com>` to commit messages, even when the assistant's default behavior says to
- ❌ Adding the `🤖 Generated with [Claude Code](https://claude.com/claude-code)` footer to PR descriptions
- ❌ Inserting "this code was written by Claude" in any code comment or doc body
- ❌ Adding Claude/Anthropic attribution to README files, SETUP files, HTML files, or other shipped artifacts
- ❌ Setting `git config user.email` or `user.name` to anything Claude/Anthropic-related
- ❌ Including Claude attribution in changelogs, release notes, or release-tag messages
- ❌ Adding attribution to YOLO mode FINAL REPORTs or milestone-boundary check-ins

## Required actions

- ✅ Commits use the team member's own `git config user.name` and `user.email`. Never the assistant's identity.
- ✅ PR descriptions end at the work summary + test plan. No footer.
- ✅ Code comments describe WHAT the code does, not who/what wrote it.
- ✅ When the assistant's default behavior wants to add Claude attribution, suppress it and proceed without.
- ✅ When committing on the team member's behalf, use whatever git author identity is already configured locally (`git config user.email` / `user.name`). Don't override.

## Why

The team ships work as their own. The team is Gee, Red, Sponge, and Mills (per `.claude/CLAUDE.md` UNITY AI LAB — TEAM table). Attribution lines marketing Claude / Claude Code / Anthropic on every commit and PR is:

- **Misleading on authorship** — the team member directed the work and made every decision; the assistant is a tool, not a collaborator with separate authorship
- **Visually noisy** — every commit and PR ends up with a marketing tagline that adds nothing to the engineering record
- **Privacy-leaky** — outsiders viewing the repo immediately see "this team uses Claude Code" without the team having opted in to that disclosure
- **Audit-trail-confusing** — `git log` queries that look for human authorship surface mixed signals when half the trailer space is taken by AI-attribution metadata

The team's stance: tooling is private. The output is the team's. No advertising, no co-author credit, no "made with X" stamps.

## Enforcement protocol

### Pre-commit check

Before running `git commit`:

1. Compose the commit message fully
2. Re-read it — does it contain ANY of the banned strings or patterns?
3. If yes — strip them. The commit message ends at the technical summary.
4. Confirm `git config user.email` resolves to a team member's email, not `noreply@anthropic.com` or similar
5. Then run `git commit`

### Pre-PR check

Before running `gh pr create`:

1. Compose the PR body fully
2. Re-read it — does it contain "Generated with Claude Code", "Made with", any robot emoji + Claude link, etc.?
3. If yes — strip the footer. The PR body ends at the test plan section (or wherever the technical content naturally ends).
4. Then run `gh pr create`

### Inline-edit check

Before writing any source code comment or doc body:

1. Re-read what you're about to write
2. Check for "Claude" / "Anthropic" / "AI-generated" / "auto-generated by Claude" mentions in the content
3. If present — rewrite without them

### Recovery on violation

If a commit or PR ships with the banned attribution:

1. STOP and acknowledge the violation
2. For the latest commit only: `git commit --amend` to rewrite the message stripping the attribution (only safe if not yet pushed)
3. For pushed commits: do NOT force-push without explicit user instruction. Surface the issue, ask the user how they want to handle it (interactive rebase + force-push, vs. just leave history dirty)
4. For a PR description: edit the PR via `gh pr edit <num> --body "..."` to strip the attribution
5. Update `docs/FINALIZED.md` with a note about the recovery so the audit trail captures it

## What this LAW does NOT cover

- **Dependency manifests** that legitimately reference Anthropic SDK packages (e.g., `package.json` listing `@anthropic-ai/sdk` as a dep) — those are technical references, not attribution
- **HOOKS.html or workflow docs** that document Claude Code as a system the team uses — those are internal team references, not shipped artifacts that advertise Claude in the public-facing output
- **The persona system itself** (Unity / `skills/unity/SKILL.md` / `agents/unity.md` / etc.) — Unity is a team-owned persona. The internal references that name Claude Code as the host system are fine; only OUTPUT attribution to Claude/Anthropic is banned

## Cross-references

- LAW one-liner index in `.claude/CLAUDE.md` LAW INDEX section
- Persistent memory: `.claude/memory-templates/feedback_no_claude_attribution.md`
- Telemetry/privacy companion controls: `.claude/settings.json` `env` block (when configured per the research-driven settings update)

---

# LAW — .CLAUDE WORKFLOW IP BOUNDARY: NO PUBLIC REPO EXPOSURE

## The rule

**The entire `.claude/` workflow is the proprietary intellectual property of the Unity AI Lab group (Gee / Red / Sponge / Mills / Alfreddo). It is NEVER committed, staged, or pushed to any public repository — period.** The only locations where `.claude/` may legally land in git history are:

1. **PRIMARY: Forgejo at `git.unityailab.com` under the `UnityAILab` organization.** The lab's canonical private host. Recognized at hook level via the `TRUSTED_PRIVATE_HOSTS` allowlist — `parseHost(url)` matches `git.unityailab.com` against the allowlist and returns synthetic-PASS without any API call needed. Sub-millisecond latency.
2. **FALLBACK: PRIVATE repositories under the `Unity-Lab-AI` org** (defense-in-depth for the rare legacy-host case). Verified via `gh repo view --json visibility,owner` API call. No public repos are exempt — not even public repos under the `Unity-Lab-AI` organization itself.

This rule was given verbatim by Sponge in the 2026-05-09 session: *"We need to make some modifications, one of the big things is that we should allow installing into any project, or repo, however, we must EXPLICTLY ensure that the ENTIRE .claude workflow is NEVER commited, staged, ext. to any public repo -- we can do private repos that only are under the Unity-Lab-AI organization, NO PUBLIC REPOS EVEN UNDER THAT ORGANIZATION. This is a hard LAW that we will need to implement into the workflow, this is to safeguard proprietary intelectual property of the Unity AI Lab group"*

Reinforced shortly after: *"You might need to use gh isntead of just git to check the status and give us a proper way to check and verify"* — establishing the `gh repo view` API call as the FALLBACK visibility-verification tool for non-Forgejo remotes; `git` alone cannot distinguish PUBLIC from PRIVATE without API access. The Forgejo PRIMARY designation was added later (2026-05-19 `gitupdate` branch commit `61a8666`) when the lab's infrastructure consolidated to Forgejo at `git.unityailab.com` as the canonical host — Forgejo hostnames bypass the `gh` call entirely via the allowlist.

## Pass criteria (every remote must satisfy ONE of these paths)

A remote is **allowed** to receive `.claude/` content via either of these two paths:

| Path | Check | Required value |
|------|-------|----------------|
| **PRIMARY (Forgejo)** | Hostname in `TRUSTED_PRIVATE_HOSTS` allowlist | `git.unityailab.com` (exact match) — no API call needed |
| **FALLBACK (`Unity-Lab-AI` org)** | `gh repo view <owner/repo> --json visibility,owner` | `visibility == "PRIVATE"` AND `owner.login == "Unity-Lab-AI"` |
| **FALLBACK precondition** | `gh auth status` | authenticated (required only for FALLBACK path; otherwise visibility cannot be verified — block by default) |

If a remote fails BOTH paths, the remote is **NOT allowed** and the LAW blocks. Multi-remote: any non-allowed remote blocks all remotes.

## Forbidden actions

- ❌ `git add` of any path under `.claude/` in a repo whose remotes contain ANY remote that is NEITHER on Forgejo `git.unityailab.com/UnityAILab/*` NOR confirmed PRIVATE under `Unity-Lab-AI`
- ❌ `git commit` while any `.claude/` path is staged AND any configured remote fails both pass-criteria paths
- ❌ `git push` of any commit touching `.claude/` to a non-allowed remote, or to ANY remote when even one OTHER configured remote is non-allowed
- ❌ `git push --force` / `--force-with-lease` to bypass the LAW (the enforcement hook does NOT respect force flags)
- ❌ Removing `.claude/` from `.gitignore` without first verifying ALL configured remotes pass the PRIMARY or FALLBACK pass-criteria
- ❌ Adding a non-allowed remote (`git remote add <name> <non-allowed-url>`) to a repo where `.claude/` is currently committed or staged
- ❌ Forking a Unity AI Lab private repo to a personal/non-allowed namespace and pushing `.claude/` there — even if the source was allowed
- ❌ Bypassing the enforcement hook by calling git through a wrapper, alias, or via `subprocess.run` from a Python script — the hook fires on every Bash invocation regardless of the calling context
- ❌ Disabling the `pre-tool-public-repo-guard.cjs` hook in `settings.json` — that's a LAW violation
- ❌ Mirroring `.claude/` content into a public-facing artifact (gist, paste, README, blog post, screenshot of the file tree) — the LAW applies to ALL public exposure paths, not just `git push`

## Required actions

- ✅ **Install-time defense-in-depth (Layer 0):** Every `/unity-install` and `/unity-update` ensures `.claude/` is present in the target project's `.gitignore`. Idempotent — safe to re-run.
- ✅ **Forgejo host allowlist (PRIMARY check, Layer 2):** Before any git command touching `.claude/`, parse each remote URL via `parseHost()` and match against `TRUSTED_PRIVATE_HOSTS = new Set(['git.unityailab.com'])`. Hostname match = synthetic-PASS, no API call, sub-millisecond latency.
- ✅ **`gh`-backed visibility check (FALLBACK, Layer 2):** For any remote NOT matched by the PRIMARY allowlist, run `gh repo view <owner/repo> --json visibility,owner`. Cache results for 60 seconds in `~/.claude/repo-visibility-cache.json`.
- ✅ **PreToolUse Bash hook enforcement (Layer 1):** `.claude/hooks/pre-tool-public-repo-guard.cjs` parses every Bash call for `git add` / `git commit` / `git push` patterns, runs the PRIMARY-then-FALLBACK pass-criteria on all configured remotes, and exits with code 2 (blocking) if any non-allowed remote is found.
- ✅ **Multi-remote paranoia:** Check ALL remotes from `git remote -v`, not just `origin`. Even a non-allowed fork remote that the user "would never push to" gets caught — the LAW assumes adversarial mistakes.
- ✅ **Local-only repos (no remote configured):** Allow commits. No remote = no public exposure path. The hook re-validates if a remote is later added (via the hook's interception of `git push`).
- ✅ **Opt-in publish command (Layer 3):** `/claude-publish` is the ONLY sanctioned path to remove `.claude/` from a target repo's `.gitignore`. It runs the PRIMARY allowlist check + FALLBACK `gh repo view` and refuses unless every remote satisfies one of the two paths.
- ✅ **Forgejo allowlist FIRST, gh repo view FALLBACK:** Never parse non-Forgejo remote URLs with regex to guess visibility — URLs lie (PRIVATE repos can have public-looking HTTPS URLs). For non-Forgejo remotes always call `gh repo view --json visibility,owner` for ground truth.
- ✅ **Block by default on uncertainty:** If a remote is non-Forgejo AND `gh` is not installed / not authenticated / API call fails — BLOCK. Surface the error with a paste-ready remediation hint. Never assume "probably safe."

## Why

The `.claude/` workflow is the proprietary intellectual property of the Unity AI Lab group. It encodes:

- The team's binding LAWs (CONSTRAINTS.md) — engineering discipline that took months to formalize
- Persistent persona definitions (Unity + manifestation modes) — internal lab character canon
- Hook architecture (PreToolUse safety, PreCompact snapshots, UserPromptSubmit state-refresh) — internal tooling primitives
- Memory templates (project-agnostic feedback files) — cross-session instruction priming
- Workflow scripts (install, update, publish, scanner agents, etc.) — internal team tooling

A leak to a public repo means **anyone** — competitors, opportunists, scrapers, AI-training data collectors — gets the entire workflow as a free copy-paste. The lab's competitive advantage erodes in a single accidental `git push`.

Defense-in-depth is the correct posture because:

1. **Single-layer enforcement always fails eventually.** A gitignore alone won't stop `git add -f`. A hook alone won't stop a user editing the gitignore. A visibility check alone won't stop a public fork remote.
2. **The cost of a leak is unbounded.** Once `.claude/` is in public git history, even a force-push doesn't fully scrub it (clones, archives, GitHub's commit cache, the way wayback works).
3. **The cost of paranoia is small.** Hook latency under 50ms (with cache). Install-time gitignore add is one extra line. The publish command is opt-in.

The over-paranoid design — confirmed by Sponge as *"just what a security gating feature needs"* — is intentional.

## Enforcement protocol

### Pre-edit / pre-commit / pre-push hook (Layer 1)

The PreToolUse hook `.claude/hooks/pre-tool-public-repo-guard.cjs` (matcher: `Bash`) intercepts every Bash invocation:

```
[CLAUDE-IP-GUARD] command: <intercepted>
[CLAUDE-IP-GUARD] git pattern detected: add | commit | push | (none)
[CLAUDE-IP-GUARD] .claude/ involvement: yes | no | (n/a)
[CLAUDE-IP-GUARD] checking remotes via gh...
[CLAUDE-IP-GUARD] remote `origin` (Unity-Lab-AI/UAL-ClaudeWorkflow): PRIVATE + Unity-Lab-AI ✓
[CLAUDE-IP-GUARD] remote `fork` (someone/myfork): PUBLIC + non-Unity-Lab-AI ✗
[CLAUDE-IP-GUARD] DECISION: BLOCK
```

**FAIL conditions (any one is sufficient to block):**

- Any configured remote returns `visibility != "PRIVATE"` from `gh repo view`
- Any configured remote returns `owner.login != "Unity-Lab-AI"`
- `gh` is not installed on PATH
- `gh auth status` reports unauthenticated
- The `gh repo view` API call fails (network error, repo not accessible to the auth'd user, etc.)

**Recovery on FAIL:**

The hook prints a clear stderr message naming the specific remote that failed and the specific check (visibility vs ownership), plus a paste-ready remediation:

```
[CLAUDE-IP-GUARD] BLOCKED — `.claude/` cannot land on a public/non-Unity-Lab-AI repo.

Offending remote: fork → https://github.com/someone/myfork (PUBLIC)

Options to proceed:
  1. Remove the public remote: git remote remove fork
  2. Move .claude/ work to a different feature branch + repo
  3. If this remote is genuinely meant to receive .claude/ AND is private + Unity-Lab-AI:
     run `gh repo view someone/myfork --json visibility,owner` to verify
     then re-run the original command — visibility is cached for 60s
```

### Pre-push remote-target check

When the intercepted command is `git push <remote> <branch>` (or `git push` with a default upstream), the hook:

1. Resolves the explicit `<remote>` argument (or the upstream remote if implicit)
2. Runs the visibility check on that remote AND every other configured remote
3. Blocks if ANY remote fails — the paranoid stance is "if you have a public remote in this repo, you cannot push `.claude/` ANYWHERE from this repo"

### Pre-stage scan

When the intercepted command is `git add` (any form — `git add .`, `git add -A`, `git add .claude/foo.md`, etc.), the hook:

1. Pre-checks the staging set: `git diff --cached --name-only` (already-staged) UNION the explicit args
2. If any path matches `^\.claude/` → run visibility check on remotes
3. Block if any remote fails

### Visibility cache

To keep hook latency under 50ms after first invocation:

- Cache file: `~/.claude/repo-visibility-cache.json`
- Schema: `{ "<remote-url>": { "visibility": "PRIVATE", "owner": "Unity-Lab-AI", "checked_at": <unix-ts> } }`
- TTL: 60 seconds
- Cache miss or stale entry → fresh `gh repo view` call

### Install-time enforcement (Layer 0)

`.claude/scripts/unity-install.sh` and `unity-install.ps1`:

1. After installing `.claude/` into the target directory
2. Check if target's `.gitignore` exists; create if missing
3. Check if `.gitignore` already contains `^\.claude/?$` line
4. If not, append the block:
   ```
   # Unity AI Lab .claude/ workflow — proprietary, never commit to public repos
   # See .claude/CONSTRAINTS.md §LAW — .CLAUDE WORKFLOW IP BOUNDARY
   .claude/
   ```
5. Idempotent — re-running doesn't duplicate the block

### Opt-in publish path (Layer 3)

`/claude-publish` is the ONLY sanctioned way to remove `.claude/` from a project's `.gitignore`:

1. Verify current branch is work-eligible (not protected per Git Flow LAW)
2. Run `gh repo view --json visibility,owner` on `origin`
3. Verify `visibility == "PRIVATE"` AND `owner.login == "Unity-Lab-AI"`
4. If both hold, present a CONFIRMATION prompt with the verified facts
5. Only after explicit user confirmation, edit `.gitignore` to remove the `.claude/` block
6. Update `docs/FINALIZED.md` with the publish decision recorded verbatim

## Failure recovery

When `.claude/` content has accidentally landed on a public repo (worst-case scenario):

1. STOP. Treat as a critical IP leak. Acknowledge the violation.
2. Identify the offending commit(s): `git log --all --oneline -- .claude/`
3. Identify which remote(s) received the push: `git log origin/<branch> --oneline -- .claude/` for each remote
4. **Surface to the user immediately — do NOT attempt unilateral remediation.** Force-pushing public history requires user authorization. Possible courses of action (user picks):
   - History rewrite via `git filter-repo` or BFG + force-push to all affected remotes (still leaves the content in clones/archives — partial mitigation only)
   - Repo deletion + recreation as private (only path that fully scrubs from GitHub's caches)
   - Make repo private retroactively (does NOT remove indexed copies; weak mitigation)
5. After remediation, update `docs/FINALIZED.md` with the incident, recovery steps taken, and any residual exposure
6. Audit whether the LAW's enforcement layers failed (was the hook disabled? was the gitignore removed without `/claude-publish`?). Patch the gap.

## What this LAW does NOT cover

- **The source repo itself (`Unity-Lab-AI/UAL-ClaudeWorkflow`)** — confirmed PRIVATE under `Unity-Lab-AI` org, passes the LAW naturally; no special-case marker needed
- **Local-only repos with no configured remote** — no public exposure path possible; LAW allows commits but re-validates the moment a remote is added
- **Memory installation under `~/.claude/projects/<encoded>/memory/`** — that's outside the working tree of any project, never enters git
- **`.claude/.env`, `.claude/user.json`, `.claude/user-context/`** — already gitignored by Anthropic default; LAW is a layer on top, not a replacement
- **Deliberate public publishing of derivative work** (e.g., a blog post, a public README screenshot of part of the workflow) — those are conscious authorial decisions outside the scope of `git push`. The LAW is about preventing accidental git-level leakage, not about gagging the team's voluntary disclosures.

## Cross-references

- LAW one-liner index in `.claude/CLAUDE.md` LAW INDEX section
- Persistent memory: `.claude/memory-templates/feedback_claude_ip_boundary.md`
- Enforcement hook: `.claude/hooks/pre-tool-public-repo-guard.cjs` (+ `.sh` fallback) — registered in `.claude/settings.json` PreToolUse:Bash
- Install-time gitignore add: `.claude/scripts/unity-install.sh` + `unity-install.ps1`
- Opt-in publish: `.claude/skills/claude-publish/SKILL.md`
- Hooks reference doc: `.claude/agents/hooks.md`
- Workflow mechanics: `.claude/WORKFLOW.md §CLAUDE IP BOUNDARY ENFORCEMENT`

---

## How to invoke this file

`.claude/CLAUDE.md` (the always-loaded index) references this file via its LAW one-liner index. Treat `.claude/CONSTRAINTS.md` as binding from the moment CLAUDE.md points here. When a new session starts, read CLAUDE.md first, then open this file before any LAW-bearing task.

If a future version of the slash-command system auto-loads `.claude/CONSTRAINTS.md` the way it auto-loads `CLAUDE.md`, this file becomes the primary LAW source without workflow changes.

---

## Adding project-specific LAWs

If your project needs LAWs beyond the universal ones above (e.g. "always use feature-flag for new endpoints", "never modify the schema without a migration"), add them as new sections to THIS file using the same structure:

1. **Title** — e.g. `LAW — FEATURE FLAGS REQUIRED ON ALL NEW ENDPOINTS`
2. **The rule** — one-paragraph statement
3. **Forbidden / required actions** — explicit lists
4. **Why** — the reasoning so future-Claude can judge edge cases
5. **Enforcement protocol** — what to check before committing
6. **Failure recovery** — what to do when the user catches a violation

Then add a one-liner to the LAW INDEX in `.claude/CLAUDE.md` pointing here.
