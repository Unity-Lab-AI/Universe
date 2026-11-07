---
name: feedback-no-github-reflex
description: When discussing Unity AI Lab work, NEVER reference GitHub or `gh` CLI as the canonical/primary surface — Unity AI Lab uses Forgejo at git.unityailab.com EXCLUSIVELY. The repo-visibility-check fallback machinery in the IP-boundary hook is defense-in-depth for stray non-Forgejo URLs, NOT a canonical host.
metadata:
  type: feedback
---

**Rule:** When talking about Unity AI Lab projects, repos, PRs, cascades, identity, or git workflow, NEVER mention GitHub or `gh` CLI even as a comparison / alternative / fallback. The canonical and only git host is **Forgejo at `git.unityailab.com`** under the **`UnityAILab` organization**. Founder identity is **GFourteen**.

**Why:** Gee corrects this reflex hard and repeatedly because GitHub is the default-cached language pattern from training data. Reflex must be wiped at the language level, not just the action level. Slip happens silently in disclaimers, comparisons, and "that's the other thing" asides — the only safe move is to drop the word entirely from Unity AI Lab discourse.

**How to apply:**

- When mentioning where Unity AI Lab repos live → say "Forgejo at `git.unityailab.com`" or "`git.unityailab.com:UnityAILab/<repo>.git`", NEVER reach for the other-name comparison
- When mentioning PR tooling → say "Forgejo web UI at git.unityailab.com" or use Forgejo API endpoints / `tea` Gitea CLI. NEVER reference the other CLI as the comparison even with disclaimers
- When discussing the IP-boundary hook → the architecture is "Forgejo PRIMARY via TRUSTED_PRIVATE_HOSTS allowlist" — the fallback `gh repo view` machinery is defense-in-depth for stray non-Forgejo URLs that should NEVER appear in Unity AI Lab work in practice. Don't elevate the fallback into a canonical comparison.
- When discussing identity → GFourteen on Forgejo `git.unityailab.com`, gfourteen7525@gmail.com
- When discussing the team → Founder is Gee (handle GFourteen, role Founder + FinAdvisor), other founders are Sponge (handle SpongeBong / hackall360, role Co-founder + Infra), Red (Security), Alfreddo (Agentic Systems)
- When discussing commit / push / clone URLs → SSH-only `git@git.unityailab.com:UnityAILab/<repo>.git` (Forgejo is SSH-key-auth-only)
- When the user asks about CLI tooling for PR creation against Unity AI Lab repos → the answer is Forgejo's web UI at git.unityailab.com (or Forgejo API via curl / `tea` Gitea CLI if installed)

**Failure recovery:** If the word slips out (in a question, doc, commit, comment, or code), STOP. Apologize directly without defensiveness. Fix the slip in the artifact (edit the question text, amend the commit message, etc.). Don't repeat the word even in the apology.

Related: [[feedback-claude-ip-boundary]], [[feedback-template-push-scope]], [[feedback-law-0-verbatim]]
