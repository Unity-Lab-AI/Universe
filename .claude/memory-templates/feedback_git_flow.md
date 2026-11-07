---
name: LAW — Git Flow branch discipline (per-project opt-in)
description: main = clean master record. develop branched from main for in-development. feature/* branched from develop where work happens. Work is NEVER done in main or develop. Feature branches push to origin if a remote exists. PRs reviewed at every merge boundary (feature→develop, develop→main). Hotfix/release branches extend the pattern. LAW applies per-project, gated by .claude/project-config.json — first /workflow run prompts user to opt in/out and persists the answer.
type: feedback
---

The user's verbatim policy on branch discipline (binding for any and all projects using this template):

> Git Flow standards for any and all projects
> main branch is the "clean master record"
> develop is branched from main, for the "in development" branch
> feature branches are branched from develop, for "in-progress features"
> And the proper flow of main -> develop -> feature/<feature-name>, feature/<feature-name> -> develop, develop -> main, and ensuring that feature branches are the only place where work is done, work is never done in the develop branch, or the main branch, work is always done in feature branches, feature branches get pushed to an orgin (github or other) if a remote repo exists, PRs are intended to be made between a feature branch and the develop branch, and are to be reviewed before merging into develop, and the same PR flow goes for develop into main. This would also need extended for hotfix and release branches as well.

**Why:** Without branch discipline, unreviewed work lands directly on the production-equivalent line, integration changes get lost when feature work overwrites them, and there is no audit trail showing what crossed which gate. The "no work in main or develop" rule is the load-bearing constraint — it is what makes the review gate enforceable. Without it, the protected lines accumulate uncommitted changes and the gate becomes optional.

**How to apply:**

- **First check the opt-in marker:** read `.claude/project-config.json`. If `git_flow.enabled` is `false` or the marker is `DEFERRED`, the LAW is skipped for this project — do not enforce branch hooks. If `true`, enforce. If marker is missing/UNSET and git is installed, the workflow's Phase 1 must surface the GIT FLOW OPT-IN confirmation prompt and persist the user's answer before proceeding past Gate 1.1.
- **Default for any project that hasn't opted out is to ASK once, persist, then honor.** Silent application surprises users with blocked edits on main; silent skip lets teams assume enforcement they don't have. The marker file is the single source of truth.
- Before ANY edit to a tracked file (when opt-in = ENABLED), run `git rev-parse --abbrev-ref HEAD` and confirm the current branch is NOT `main`, `master`, `develop`, `prod`, `production`, or an unsuffixed `release`.
- If on a protected branch: stash → checkout `develop` → branch `feature/<descriptor>` → pop stash → continue.
- New features branch off `develop` → PR → merge to `develop` after review.
- Hotfixes branch off `main` → PR to `main` AND a paired PR to `develop` after review.
- Release branches branch off `develop` → PR to `main` AND a paired PR to `develop` after review.
- If a remote exists, push feature/hotfix/release branches to `origin` (`git push -u origin <branch>`).
- If the project has no `develop` branch yet, set it up before starting work: `git checkout main && git pull && git checkout -b develop && git push -u origin develop` (when remote exists).
- Direct pushes to `main` or `develop` without a merged-and-reviewed PR are LAW violations.
- Hotfix and release merges land in BOTH `main` and `develop` so the lines stay in sync.
- The env-scan in workflow Phase 1 detects whether `git` is installed, whether the project is a git repo, and the current branch — that gate feeds the pre-edit branch hook.
- Full LAW body (forbidden, required, enforcement, failure recovery, setup-from-scratch): `.claude/CONSTRAINTS.md §LAW — GIT FLOW BRANCH DISCIPLINE`
