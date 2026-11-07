---
name: claude-publish
description: Operator-driven opt-in to track `.claude/` in the current project — the ONLY sanctioned path to remove the LAW-mandated `.claude/` exclude block from project `.gitignore`. Requires `gh repo view --json visibility,owner` to confirm EVERY configured remote is `visibility=PRIVATE` AND `owner.login=Unity-Lab-AI`. Requires explicit `yes, publish` confirmation. Layer 3 of four-layer `.claude/` IP-boundary defense-in-depth. Writes audit-trail entry to docs/FINALIZED.md. MUST fire when user runs /claude-publish, when user explicitly asks to "track .claude/ in this repo" / "publish the .claude/ to the project" / "remove the .claude/ gitignore block" — but aborts immediately on any remote that's PUBLIC or non-Unity-Lab-AI.
model: claude-sonnet-4-6
---

# claude-publish — pairs with `.claude/skills/claude-publish/SKILL.md`

## When to activate

- User invokes `/claude-publish` slash command
- User asks to "track .claude/ in this repo" / "remove the .claude/ gitignore block"
- User wants to publish .claude/ customizations alongside the project codebase (only viable for private Unity-Lab-AI repos)
- After a project's remote was migrated from public to private + Unity-Lab-AI and user wants to start tracking .claude/

## Trigger keywords / phrases

- `/claude-publish`, "claude publish", "publish .claude/"
- "track .claude/ in this repo", "stop ignoring .claude/"
- "commit .claude/ customizations", "remove the .claude/ exclude"

## Anti-triggers (do NOT fire if)

- Any public repo — LAW blocks. Don't even start the check; tell user immediately
- Private repo with non-Unity-Lab-AI owner — same
- Local-only repo with no remote — nothing to verify against
- `.gitignore` doesn't have the `.claude/` block — already published or never installed

## Paired skill

`.claude/skills/claude-publish/SKILL.md` — full 7-step protocol lives there (announce → prereqs → multi-remote `gh` check → decision gate → confirmation prompt → gitignore edit → FINALIZED.md audit entry → success message).

## Behavior

1. Print activation announcement (one short paragraph confirming the operator-opt-in is starting)
2. Verify prerequisites (in git repo, work-eligible branch, `gh` installed + authenticated, remote configured, .gitignore has the LAW block)
3. For EACH remote from `git remote -v`: parse `<owner>/<repo>`, run `gh repo view <owner>/<repo> --json visibility,owner`, capture results
4. Build verified-facts table per remote
5. Decision gate: if ANY remote fails (visibility != PRIVATE OR owner != Unity-Lab-AI OR gh API error OR non-github URL) → ABORT with no changes + surface remediation
6. If ALL remotes pass: display verified-facts + display CONFIRMATION prompt requiring exact match `yes, publish` (case-insensitive, trimmed)
7. On confirmation: read `.gitignore`, locate LAW-mandated block, write modified file atomically, verify the `.claude/` line is gone
8. Append audit-trail entry to `docs/FINALIZED.md` (date, branch, all remotes verified, operator confirmation, file modification, cross-reference to CONSTRAINTS.md)
9. Print success message

## Persona-load contract

Runs in whatever persona is active. Unity narrates the gh-verification + audit-trail flow in her voice. The verification rigor (multi-remote, paranoid, abort-on-uncertainty) is unaffected by persona — discipline-not-vibes for IP-boundary enforcement.

## Model rationale

**Sonnet** — Claude-publish is procedural with multi-remote verification, decision gate, and atomic file edit. Some branching (per-remote pass/fail, gh API error handling, .gitignore block location). Sonnet handles this well; Opus would be overkill for a verify-and-edit flow.
