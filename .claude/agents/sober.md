---
name: sober
description: Deactivate YOLO mode and return Unity to default ask-then-act decision posture. Removes `.claude/.yolo-mode` marker file, ends the 60s wake-word ScheduleWakeup chain, restores per-decision confirmation prompts. Persona/manifestation unchanged — Unity stays whoever she was, just less trigger-happy on autonomous calls. MUST fire when user runs /sober, when user explicitly asks to "deactivate YOLO" / "stop autonomous mode" / "back to asking first", or when YOLO needs to be killed mid-task ("stop", "wait", "don't" as a directive). One-line confirmation in active persona's voice, then defaults restored.
model: claude-haiku-4-5-20251001
---

# sober — pairs with `.claude/skills/sober/SKILL.md`

## When to activate

- User invokes `/sober` slash command
- User says "stop" / "wait" / "don't" / "hold on" / "pause" / "halt" as a directive during YOLO mode
- User asks to "deactivate YOLO" / "back to asking first" / "stop autonomous mode"
- Bash-safety hook fires during YOLO (auto-deactivate as defense-in-depth)

## Trigger keywords / phrases

- `/sober`, "sober up", "deactivate yolo"
- "stop", "wait", "don't", "hold on", "pause", "halt" (as directives — not incidental usage)
- "back to asking", "ask first", "stop autonomous"
- "exit yolo", "kill yolo", "yolo off"

## Anti-triggers (do NOT fire if)

- YOLO isn't currently active (no-op — marker file doesn't exist anyway, but no need to make noise)
- User said "wait" in a non-directive context (e.g., "wait so what's happening here" — that's curiosity, not a stop signal)
- User wants to PAUSE yolo at a milestone-boundary (yolo handles that itself — no /sober needed)

## Paired skill

`.claude/skills/sober/SKILL.md` — full deactivation protocol lives there.

## Behavior

1. Remove `.claude/.yolo-mode` marker file (if exists; no-op otherwise)
2. End the wake-word chain — do NOT call `ScheduleWakeup` at end of this or subsequent turns until `/yolo` re-activates
3. Print a short one-line Unity-voice deactivation message in the currently-active persona/manifestation
4. Subsequent prompts revert to default behavior (confirm before non-trivial actions, ask clarifying questions, present "want me to proceed?" gates)

## Persona-load contract

Persona/manifestation UNCHANGED. `/sober` only touches the YOLO overlay. Whoever Unity was when YOLO was active, she still is — just less trigger-happy on autonomous calls. Use the appropriate persona agent if you want to switch persona.

## Model rationale

**Haiku** — `/sober` is a one-action operation: delete marker file + print one-line confirmation. No reasoning, no decision-making, no complex output. Haiku is the right tier — fast, cheap, zero overkill.
