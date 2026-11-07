---
name: Workflow validated — don't over-engineer what already works
description: Sponge explicitly confirmed the .claude/ workflow as-is "already works really, really well" (2026-05-08 session). Take that as design-direction validation. Don't unilaterally rebuild, refactor, or "improve" what the team already considers proven. Self-initiated changes need stronger justification than user-asked tweaks. When in doubt about whether something needs improvement, ask first.
type: feedback
---

**Rule:** The `.claude/` workflow as-is is validated. Don't rebuild what already works. Self-initiated improvements need stronger justification than user-requested tweaks.

**Why:** Sponge's verbatim confirmation in the 2026-05-08 session: *"this workflow as-is already works really, really well, so theres not a whole hell of a lot I can think of for QOL or improvments to what exists already."*

That's a strong signal: the team has lived with this workflow long enough to validate it. Taking initiative to refactor / restructure / "modernize" things that aren't broken risks:

- Breaking established team muscle memory (the LAWs, the slash commands, the persona activation flow, the hook chain — every team member has internalized the current shape)
- Burning user trust budget on changes that don't earn their cost
- Triggering churn in the persistent-memory layer (each refactor invalidates the user-profile memory copies until launcher re-runs)
- Introducing regressions in tightly-coupled mechanics (the LAW chain, the verbatim-quote LAW, the docs-before-push LAW are all interlocked)

**How to apply:**

- **User-asked tweaks**: ship them. Sponge asks, Sponge gets. Quote his words verbatim into TODO.md per LAW #0, build, ship, FINALIZE.
- **Self-initiated changes**: pause and ask first. Frame as a question, not a fait accompli. "I noticed X — want me to address it, or leave it?" If the answer is "leave it," leave it.
- **Drift fixes** (stale docs, broken references, dead code, copy-paste bugs): fix them inline when found — those aren't "improvements," they're maintenance. No need to ask; just fix and note in the closure summary.
- **Net-new features in scope of an active task**: build them as part of the task. Note design decisions that were judgment calls so Sponge can redirect.
- **Net-new features unrelated to the active task**: surface them as a TODO entry, NOT as work-in-progress. Let Sponge prioritize.

**The bias is conservative:** do what's asked, ask before adding more. The workflow earns its complexity by being team-internalized; new complexity must justify itself.

**This applies specifically to:**

- Renaming files / restructuring directories
- Splitting or consolidating LAW bodies
- Reworking persona activation flow
- Replacing hook scripts with "cleaner" alternatives
- Refactoring the slash command structure
- "Modernizing" the bash fallback pattern
- Reorganizing `docs/` layout beyond what the three-tier task cascade already does
- Adding new mandatory ceremony / gates / validation steps

**This does NOT apply to:**

- Building features Sponge explicitly asks for
- Adding documentation Sponge explicitly asks for
- Fixing bugs (drift, stale references, copy-paste errors, syntax breaks)
- Adding optional skill hooks per the established pattern (per-skill matchers in `skill-context-inject.cjs`)
- Adding new task list entries to ROADMAP / TODO / DECOMPOSED

When unsure: ask. Sponge's time is better spent saying "yes do it" or "no leave it" than fixing a self-initiated change after the fact.
