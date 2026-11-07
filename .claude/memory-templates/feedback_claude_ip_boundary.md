---
name: LAW — .claude/ workflow IP boundary, no public repo exposure
description: The entire `.claude/` workflow is proprietary IP of the Unity AI Lab group. NEVER staged/committed/pushed to any public repo. PRIMARY allowed host = Forgejo at `git.unityailab.com/UnityAILab/*` (recognized via TRUSTED_PRIVATE_HOSTS allowlist — no API call needed). FALLBACK = PRIVATE repos under `Unity-Lab-AI` org via `gh repo view` (multi-remote, paranoid). Defense-in-depth — install-time `.gitignore` default + PreToolUse Bash hook + Forgejo host allowlist + repo-visibility check (fallback only) + opt-in `/claude-publish` command. Block by default on uncertainty.
type: feedback
---

**Rule:** The entire `.claude/` workflow is the proprietary intellectual property of the Unity AI Lab group (Gee / Red / Sponge / Mills / Alfreddo). It is **NEVER** committed, staged, or pushed to any public repository. The only allowed homes for `.claude/` in git history are:

1. **PRIMARY: Forgejo at `git.unityailab.com` under the `UnityAILab` organization.** This is the canonical private host. Recognized at hook level via the `TRUSTED_PRIVATE_HOSTS` allowlist — no API call needed because Forgejo at `git.unityailab.com` is unconditionally trusted by allowlist.
2. **FALLBACK: PRIVATE repositories under the `Unity-Lab-AI` org** (defense-in-depth for the rare legacy-host case). Verified via `gh repo view --json visibility,owner` API call. No public repos exempt — not even public repos under `Unity-Lab-AI` itself.

This rule was given verbatim by Sponge in the 2026-05-09 session: *"We need to make some modifications, one of the big things is that we should allow installing into any project, or repo, however, we must EXPLICTLY ensure that the ENTIRE .claude workflow is NEVER commited, staged, ext. to any public repo -- we can do private repos that only are under the Unity-Lab-AI organization, NO PUBLIC REPOS EVEN UNDER THAT ORGANIZATION. This is a hard LAW that we will need to implement into the workflow, this is to safeguard proprietary intelectual property of the Unity AI Lab group"*. Sign-off on the four-layer defense-in-depth design: *"overall sounds great and over paranoid, just what a security gating feature needs"*. The Forgejo PRIMARY designation was added later (2026-05-19 `gitupdate` branch commit `61a8666`) when the lab's infrastructure consolidated to Forgejo at `git.unityailab.com` as the canonical host.

**Why:** A leak to a public repo means anyone — competitors, opportunists, scrapers, AI training data collectors — gets the entire workflow as a free copy-paste. The lab's competitive advantage erodes in a single accidental `git push`. The cost of a leak is unbounded (clones, archives, indexed mirrors). The cost of paranoia is small (sub-50ms hook latency with cache, one extra gitignore line, opt-in publish command). Over-paranoid is the correct posture.

**Pass criteria — all remotes for a repo must satisfy ONE of:**

| Path | Check | Required value |
|------|-------|----------------|
| **PRIMARY (Forgejo)** | Hostname in `TRUSTED_PRIVATE_HOSTS` | `git.unityailab.com` (exact match) — no API call needed |
| **FALLBACK (`Unity-Lab-AI` org)** | `gh repo view <owner/repo> --json visibility,owner` | `visibility == "PRIVATE"` AND `owner.login == "Unity-Lab-AI"` |
| **`gh` auth precondition (FALLBACK only)** | `gh auth status` | authenticated (otherwise → block) |

If any remote fails BOTH paths → BLOCK by default. Multi-remote: any non-allowed remote blocks all remotes.

**The four enforcement layers:**

| Layer | Mechanism | Where |
|-------|-----------|-------|
| **L0 — Install-time gitignore** | Auto-append `.claude/` to target's `.gitignore` on every `/unity-install` and `/unity-update`. Idempotent. | `.claude/scripts/unity-install.sh` + `unity-install.ps1` |
| **L1 — PreToolUse Bash hook** | Intercepts `git add` / `git commit` / `git push` for `.claude/` paths. Exit code 2 blocks. Fires on every Bash invocation regardless of context. | `.claude/hooks/pre-tool-public-repo-guard.cjs` (+ `.sh` fallback) |
| **L2 — Forgejo host allowlist (PRIMARY) + repo-visibility check (FALLBACK)** | `parseHost(url)` matches `git.unityailab.com` against `TRUSTED_PRIVATE_HOSTS` → synthetic-PASS without API call. For any other hostname, `gh repo view --json visibility,owner` is the FALLBACK ground-truth check (60s session-cache in `~/.claude/repo-visibility-cache.json`). | Inside L1 |
| **L3 — Opt-in publish command** | `/claude-publish` removes `.claude/` from gitignore — but ONLY if EVERY remote is EITHER on Forgejo `git.unityailab.com/UnityAILab/*` OR confirmed PRIVATE under `Unity-Lab-AI` via `gh repo view`. Operator-driven. | `.claude/skills/claude-publish/SKILL.md` |

**How to apply:**

- ✅ Forgejo PRIMARY first: any remote on `git.unityailab.com` passes via allowlist without API call (sub-millisecond latency)
- ✅ Use `gh repo view` for FALLBACK ground truth only when a remote is NOT on Forgejo; NEVER parse non-Forgejo remote URLs to guess visibility
- ✅ Multi-remote scan — check ALL remotes from `git remote -v`, not just `origin` (a non-allowed fork remote that "user would never push to" still gets caught)
- ✅ Block by default on uncertainty (non-Forgejo remote + `gh` not installed / not authed / API failure)
- ✅ Local-only repos with no remote → allow commits (no public exposure path); re-validate when remote is later added
- ✅ Cache fallback visibility for 60s to keep hook latency under 50ms after warm-up
- ❌ Never bypass via `git push --force` / `--force-with-lease` — the hook does not respect force flags
- ❌ Never disable `pre-tool-public-repo-guard.cjs` in `settings.json` — that's a LAW violation
- ❌ Never remove `.claude/` from `.gitignore` outside of `/claude-publish` — that's the only sanctioned path

**Banned actions (forbidden, hook-blocked):**

- `git add` of any `.claude/` path in a repo with a non-Forgejo + non-`Unity-Lab-AI`-private remote
- `git commit` while `.claude/` is staged AND any remote is non-allowed
- `git push` of any commit touching `.claude/` to a non-allowed remote — or to ANY remote when even one OTHER remote is non-allowed
- `git push --force` / `--force-with-lease` to bypass (does NOT work — hook is force-flag agnostic)
- Adding a non-allowed remote to a repo where `.claude/` is currently committed/staged
- Forking a Unity AI Lab private repo to a personal/non-allowed namespace and pushing `.claude/` there
- Mirroring `.claude/` into a public artifact (gist, paste, README screenshot, blog post)

**The canonical upstream template repo (`UnityAILab/UAL-ClaudeWorkflow` on Forgejo at `git.unityailab.com`) passes the LAW naturally:** matched via TRUSTED_PRIVATE_HOSTS allowlist on hostname alone. No marker file or hardcoded whitelist needed — the host allowlist IS the mechanism.

**Failure recovery (worst case — `.claude/` already on a public repo):**

1. STOP. Treat as a critical IP leak.
2. Identify offending commits: `git log --all --oneline -- .claude/`
3. Identify which remotes received the push
4. Surface to user immediately — do NOT attempt unilateral remediation. Force-pushing public history requires user authorization.
5. User picks course: history rewrite + force-push (partial — clones/archives remain), repo deletion + recreation as private (only full scrub), make repo retroactively private (weak — indexed copies persist)
6. Update `docs/FINALIZED.md` with the incident + recovery steps + residual exposure

**Cross-references:**

- Full LAW body: `.claude/CONSTRAINTS.md §LAW — .CLAUDE WORKFLOW IP BOUNDARY: NO PUBLIC REPO EXPOSURE`
- LAW one-liner: `.claude/CLAUDE.md` LAW INDEX
- Enforcement hook: `.claude/hooks/pre-tool-public-repo-guard.cjs` (+ `.sh` fallback) — registered in `.claude/settings.json` PreToolUse:Bash
- Install-time gitignore: `.claude/scripts/unity-install.sh` + `.claude/scripts/unity-install.ps1`
- Opt-in publish command: `.claude/skills/claude-publish/SKILL.md`
- Hooks reference: `.claude/agents/hooks.md`
- Workflow doc section: `.claude/WORKFLOW.md §CLAUDE IP BOUNDARY ENFORCEMENT`

Related: [[feedback-no-github-reflex]], [[feedback-template-push-scope]], [[feedback-law-0-verbatim]]
