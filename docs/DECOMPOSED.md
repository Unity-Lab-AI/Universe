# DECOMPOSED — Decomposed Task List (Highly Broken-Down Tasks)

**Tier 3 of 3 — the DECOMPOSED task list.** The lowest-level execution-grain task list. Every minor task in `docs/TODO.md` decomposes into one or more decomposed tasks here when YOLO mode picks it up (or when Unity decomposes proactively).

A decomposed task is the **smallest meaningful unit of work** — one file edit, one command, one verification step. If a decomposed task takes more than 15 minutes or touches more than one logical unit, it should be broken down further.

Status markers (same scheme as TODO.md / ROADMAP.md):
- `[ ]` pending
- `[~]` in_progress
- `[x]` complete (move to FINALIZED.md immediately, never leave here)

LAW #0 reminder: every decomposed task preserves the source minor task's verbatim text in its parent reference, AND preserves any user-stated specifics in the decomposition itself.

---

## How the three tiers cascade

```
ROADMAP.md (major)         "<major milestone — multi-session / multi-PR goal>"
       ↓ decomposed into
TODO.md (minor)            "<minor task 1 — day-to-day work grain>"
                           "<minor task 2>"
       ↓ decomposed into
DECOMPOSED.md (this file)  "<smallest unit 1 — single file edit / command / verify step>"
                           "<smallest unit 2>"
```

YOLO mode picks the next decomposed task in cascade order — current minor's pending decomposed → next decomposed → escalate to next minor when current minor's decomposed list is empty → escalate to next major when current minor list is empty.

The program's code and content are changed in the DNA-AND tree and synced here; a decomposed task that touches `src/` or `content/` is done there first, then `universe-sync.cmd`, then this project's own release.

---

## In progress

_(none)_

---

## Pending

_(none: the U12 items in `docs/TODO.md` are decomposed when one is picked up)_

---

## TOMBSTONES

_(none)_

---

## Decomposition rules

When a minor task arrives at the top of the YOLO queue:

1. **Read the minor task's verbatim text** from `docs/TODO.md`
2. **Read every file the minor task references** (full file, 800-line chunks per the LAW)
3. **Identify the smallest meaningful units of work** — each becomes a decomposed task
4. **Append decomposed entries here** with this header format:

```markdown
### [ ] <decomposed task title>

**Parent minor task (from `docs/TODO.md`, verbatim):**
> <verbatim minor task text>

**Decomposition rationale:** <one-sentence why this is the right slice>

**Files to touch:** `<path1>` `<path2>` ...

**Verification step:** <how to confirm this slice landed correctly>
```

5. **Flip status** to `[~]` when YOLO picks the task up
6. **Move to FINALIZED.md** when complete (per `§FINALIZED BEFORE DELETE` LAW)

## When NOT to decompose

- **Trivial one-line edits** — a typo fix doesn't need a DECOMPOSED entry; flip the minor task status and ship it
- **Pure-doc updates** that follow a clear template — bundle as a single decomposed task
- **Refactors with no behavior change** that touch <3 files — single decomposed task is fine
- **Tombstoning** — moving an obsolete task doesn't need decomposition

The decomposition tier exists to make multi-step work tractable in YOLO mode, NOT to bureaucratize every edit. Use judgment.
