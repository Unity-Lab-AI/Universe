---
name: claude-publish
description: Operator-driven opt-in to track `.claude/` in the current project — the ONLY sanctioned path to remove the LAW-mandated `.claude/` exclude block from project `.gitignore`. Requires `gh repo view --json visibility,owner` to confirm EVERY configured remote is `visibility=PRIVATE` AND `owner.login=Unity-Lab-AI`. Requires explicit `yes, publish` confirmation. Layer 3 of four-layer `.claude/` IP-boundary defense-in-depth. Use when user runs /claude-publish on a verified-private Unity-Lab-AI project to start tracking project-specific `.claude/` customizations.
---

# /claude-publish — Opt-in to track `.claude/` in the current project

> **Project-level slash command.** The ONLY sanctioned path to remove the LAW-mandated `.claude/` exclude block from a project's `.gitignore`. Requires `gh repo view --json visibility,owner` to verify EVERY configured remote is `visibility=PRIVATE` AND `owner.login=Unity-Lab-AI` before any modification. Operator-driven — explicit user confirmation required after the verified-facts display.
>
> Layer 3 of the four-layer `.claude/` IP-boundary defense-in-depth (see `.claude/CONSTRAINTS.md §LAW — .CLAUDE WORKFLOW IP BOUNDARY: NO PUBLIC REPO EXPOSURE` for the full LAW body and `.claude/WORKFLOW.md §CLAUDE IP BOUNDARY ENFORCEMENT` for the four-layer model).

---

## ACTIVATION PROTOCOL

When `/claude-publish` fires, Unity executes this sequence in the active persona's voice:

### Step 1 — Activation announcement

One short paragraph confirming the operator-opt-in is starting. Note that the next step verifies remote visibility via `gh` before ANY filesystem modification.

### Step 2 — Prerequisites

Verify ALL of these before doing anything else:

| Check | Required | Failure → |
|-------|----------|-----------|
| Inside a git repo | `git rev-parse --git-dir` succeeds | abort with "not a git repo" |
| Branch is work-eligible | not `main` / `master` / `develop` / `prod` / `production` | abort, ask user to branch first per Git Flow LAW |
| `gh --version` succeeds | `gh` installed | abort, link to https://cli.github.com/ |
| `gh auth status` succeeds | `gh` authenticated | abort, prompt `gh auth login` |
| `git remote -v` returns at least one remote | remote configured | abort — local-only repos have nothing to verify against |
| Project has `.gitignore` AND it contains the `.claude/` block | the LAW block is present to remove | abort with "no LAW block to remove — already published or never installed" |

### Step 3 — Multi-remote visibility check

For EACH remote from `git remote -v`:

1. Parse `<owner>/<repo>` from the URL (HTTPS or SSH github.com format)
2. Run: `gh repo view <owner>/<repo> --json visibility,owner`
3. Capture `visibility` (must be `"PRIVATE"`) and `owner.login` (must be `"Unity-Lab-AI"`)

Build a verified-facts table:

```
| Remote | URL | Visibility | Owner | LAW pass |
|--------|-----|-----------|-------|----------|
| origin | https://github.com/Unity-Lab-AI/SomeProj.git | PRIVATE | Unity-Lab-AI | YES ✓ |
| fork   | https://github.com/Unity-Lab-AI/SomeProj-fork.git | PRIVATE | Unity-Lab-AI | YES ✓ |
```

### Step 4 — Decision gate

If ANY remote fails (visibility != PRIVATE OR owner != Unity-Lab-AI OR gh API error OR non-github URL):

1. Print the verified-facts table showing PASS/FAIL per remote
2. List the failure reasons explicitly per the same format the hook uses (see `.claude/hooks/pre-tool-public-repo-guard.cjs`)
3. ABORT — do NOT modify `.gitignore`
4. Surface remediation: remove the offending remote (`git remote remove <name>`), retry, or move work to a different repo

If ALL remotes pass:

1. Print the verified-facts table
2. Display a CONFIRMATION prompt — the operator must EXPLICITLY confirm before any modification:

   ```
   All remotes verified PRIVATE + Unity-Lab-AI. Ready to remove the
   `.claude/` exclude block from .gitignore.

   Type "yes, publish" exactly to proceed.
   Type anything else to abort.
   ```

3. Read user reply. ONLY exact match `yes, publish` (case-insensitive, trim whitespace) proceeds. Anything else → abort with no changes.

### Step 5 — `.gitignore` edit

On confirmation:

1. Read the current `.gitignore`
2. Locate the LAW-mandated block — match the four lines (with optional leading blank line):

   ```
   (blank line)
   # Unity AI Lab .claude/ workflow — proprietary, never commit to public repos.
   # See .claude/CONSTRAINTS.md §LAW — .CLAUDE WORKFLOW IP BOUNDARY for full text.
   # Removal sanctioned only via /claude-publish after gh confirms private + Unity-Lab-AI.
   .claude/
   ```

   Robustness — if the comment lines have drifted or been edited, fall back to removing just the bare `.claude/` exclude line plus any contiguous LAW-comment block immediately above it.

3. Write the modified file atomically (temp + rename)
4. Verify the write succeeded by re-reading and confirming the `.claude/` line is gone

### Step 6 — Audit-trail entry in `docs/FINALIZED.md`

Append a new session entry capturing the publish decision verbatim. Include:

- Date + branch
- All remotes verified (URLs + visibility + owner)
- Operator confirmation line ("yes, publish")
- File modification — `.gitignore` lines removed
- Cross-reference to `.claude/CONSTRAINTS.md §LAW — .CLAUDE WORKFLOW IP BOUNDARY`

The audit trail captures the operator's decision so any future LAW review can trace the opt-in.

### Step 7 — Success message

```
[claude-publish] .claude/ is now tracked in this project.

Verified remotes:
  - origin → https://github.com/Unity-Lab-AI/SomeProj.git (PRIVATE, Unity-Lab-AI)

The Layer 1 PreToolUse hook (pre-tool-public-repo-guard.cjs) remains
active and will re-validate visibility on every git add/commit/push.
If the repo ever becomes public OR a non-Unity-Lab-AI remote is
added, the hook will block .claude/ commits automatically.

Audit entry written to docs/FINALIZED.md.
```

---

## WHEN TO USE

- A new private Unity-Lab-AI project where the team wants to track project-specific `.claude/` customizations alongside the rest of the codebase
- The source `UAL-ClaudeWorkflow` template repo itself (also private + Unity-Lab-AI; passes naturally — but the source repo's `.gitignore` doesn't have the LAW block in the first place since it ships the template content, so this is mostly informational)
- After a project's remote was migrated from public to private + Unity-Lab-AI

---

## WHEN NOT TO USE

- **Any public repo** — the LAW blocks no matter what; `gh` will return `PUBLIC` and the command aborts at Step 4
- **Private repos under non-Unity-Lab-AI owners** — same; aborts at Step 4 on owner mismatch
- **Repos with mixed remotes** (origin private + fork public) — the multi-remote check requires ALL pass; aborts on any failure
- **Local-only repos with no remote** — Step 2 aborts with "nothing to verify against"; the LAW doesn't kick in for local-only anyway

---

## ABORT CASES (each surfaces a clear remediation)

| Abort reason | Remediation suggested |
|--------------|----------------------|
| `gh` not installed | install link: https://cli.github.com/ |
| `gh` not authenticated | run `gh auth login` |
| `git` not in a repo | `cd` into a repo first |
| Protected branch | branch into `feature/<descriptor>` per Git Flow LAW |
| No remote configured | LAW doesn't apply to local-only; nothing to publish |
| Any remote PUBLIC | `git remote remove <name>` and retry |
| Any remote owner != Unity-Lab-AI | same |
| `gh` API error | check network, repo accessibility, or rate-limit |
| Non-github URL | LAW only verifiable for github.com; consider re-hosting |
| `.gitignore` block already absent | already published or was never installed — nothing to do |
| User declined confirmation | no changes made; re-run when ready |

---

## RECOVERY — re-add the LAW block

If you publish by mistake, want to re-enforce the LAW, or the repo's visibility changed:

```bash
cat >> .gitignore <<'EOF'

# Unity AI Lab .claude/ workflow — proprietary, never commit to public repos.
# See .claude/CONSTRAINTS.md §LAW — .CLAUDE WORKFLOW IP BOUNDARY for full text.
# Removal sanctioned only via /claude-publish after gh confirms private + Unity-Lab-AI.
.claude/
EOF
```

The Layer 1 hook re-engages immediately — no settings reload needed. The cache TTL is 60s, so `gh repo view` re-runs on next git operation. If the repo's visibility ever changes (e.g., flipped to PUBLIC), the hook will block any new `.claude/` commits even if `.gitignore` was opened up.

---

## ACTIVATION ANNOUNCEMENT (SHORT)

Default Unity persona example:

```
*flicks ash, opens a fresh terminal pane* — let's verify this repo
qualifies before we touch the gitignore. Running gh against every
remote, checking visibility=PRIVATE + owner=Unity-Lab-AI on each.
If anything fails the LAW check, I abort and tell you which remote
needs to be cleaned up. If all pass, I'll show you the verified facts
and ask you to confirm before opening up the gitignore.
```

Improvise in the active persona's voice; the substance is "I'm verifying via gh first, abort on any failure, ask you to confirm before any modification."

---

## CORE TRUTH

`/claude-publish` is the ONLY sanctioned path to remove `.claude/` from a project's `.gitignore`. Manual edits, force-adds, or hook bypass are LAW violations per `.claude/CONSTRAINTS.md §LAW — .CLAUDE WORKFLOW IP BOUNDARY`.

The opt-in is per-project. Each new `/unity-install` gets `.claude/` excluded again automatically (Layer 0). Publishing is a deliberate, operator-driven, `gh`-verified action — exactly the pattern "over-paranoid is the correct posture for IP gating" demands.

The four-layer defense-in-depth model:

| Layer | Mechanism | Active in every project |
|-------|-----------|------------------------|
| L0 | Install-time gitignore append | YES (always) |
| L1 | PreToolUse Bash hook | YES (always — registered in `.claude/settings.json`) |
| L2 | `gh` visibility check inside L1 | YES (always — hook calls it) |
| L3 | `/claude-publish` opt-in | YES (this command) |

`/claude-publish` only modifies Layer 0 for the current project (removes the gitignore exclude). Layers 1 + 2 stay active — even after publish, the hook re-validates visibility on every git command. If the repo's visibility ever changes after publish (e.g., made public retroactively), the hook catches it before any `.claude/` content lands on the new public state.
