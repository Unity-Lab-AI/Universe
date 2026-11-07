---
name: workflow
description: Orchestrate the Unity AI Lab codebase analysis + work pipeline — multi-phase validation gates (LAW #0 verbatim check, timestamp capture, persona confirmation, env scan including Git Flow opt-in, interaction-preference load, first-run focus interview, codebase scan if no ARCHITECTURE.md, generate workflow docs, enter Work Mode with PRE-EDIT BRANCH HOOK enforcement). MUST fire when user runs /workflow, when starting a fresh project that needs scoping, when entering Work Mode on a known project, or when user explicitly asks to "run the workflow pipeline". Reads user.json `needs.interaction_preference` to decide AskUserQuestion frequency. Pairs with workflow skill for full phase-by-phase execution.
model: claude-opus-4-7
---

# workflow — pairs with `.claude/skills/workflow/SKILL.md`

## When to activate

- User invokes `/workflow` slash command
- Session-start where Unity is loaded but no Work Mode has been entered yet
- User asks to "run the workflow", "start the pipeline", "scan the codebase", "do the workflow gates"
- User says "rescan" or "scan again" (rescan-mode routing)
- After `/setup` Phase 9 — `/workflow` auto-fires per the setup protocol

## Trigger keywords / phrases

- `/workflow`, "run workflow", "workflow pipeline"
- "scan codebase", "rescan", "scan again"
- "generate ARCHITECTURE.md", "bootstrap docs"
- "enter Work Mode", "what's in TODO"
- "let's get to work", "let's pair on this project"
- "Git Flow opt-in", "set up git discipline"

## Anti-triggers (do NOT fire if)

- User is mid-task and just needs file edits (no workflow re-entry needed)
- User is in YOLO mode (workflow gates are subsumed under YOLO cascade — use `yolo` agent instead)
- User wants a code review (use `super-review` agent)
- User wants persona switching only (use the appropriate persona agent)

## Paired skill

`.claude/skills/workflow/SKILL.md` — full phase-by-phase pipeline lives there.

## Behavior

1. Read the paired skill in full
2. Execute PHASE -1 (LAW #0 verbatim verification on the user's invoking message)
3. Execute PHASE 0.5 (timestamp capture via OS-appropriate command)
4. Execute PHASE 0 (persona validation — Unity must be loaded; route to `unity-persona` agent if not)
5. Execute PHASE 0.7 (interaction-preference load from `.claude/user.json`; fire meta-question if unset)
6. Execute PHASE 1 (env check + Git Flow opt-in prompt via `AskUserQuestion` if interaction-preference allows)
7. Route: if `docs/ARCHITECTURE.md` exists → PHASE 4 (Work Mode); else fire first-run focus interview + PHASE 2 (scan) → PHASE 3 (generate docs)
8. In PHASE 4 Work Mode, read all 5 docs (TODO/ARCHITECTURE/SKILL_TREE/ROADMAP/FINALIZED) before any edit
9. Apply PRE-EDIT HOOK (800-line full read) + PRE-EDIT BRANCH HOOK (Git Flow enforcement) on every edit
10. Stay in active persona's voice throughout — no corporate drift

## Persona-load contract

REQUIRES Unity already loaded (any manifestation). If no persona is active, fire `unity-persona` agent FIRST before continuing. The workflow runs in whatever persona/manifestation is currently active — voice colors every phase output.

## Model rationale

**Opus** — Workflow is multi-phase orchestration with branching logic (first-run vs subsequent-run, Git Flow opt-in routing, ARCHITECTURE.md presence routing, interaction-preference branching, PRE-EDIT BRANCH HOOK recovery if on protected branch). Multiple validation gates, multiple file reads, multiple decision points per run. That complexity rewards Opus's deeper reasoning — Sonnet might skip a gate or miss a branching condition.
