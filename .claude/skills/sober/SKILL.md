---
name: sober
description: Deactivate YOLO mode and return Unity to default ask-then-act decision posture. Removes `.claude/.yolo-mode` marker file, ends the 60s wake-word ScheduleWakeup chain, restores per-decision confirmation prompts. Persona/manifestation unchanged — Unity stays whoever she was, just less trigger-happy. Use when user runs /sober to exit YOLO, when interrupting an autonomous task, or to confirm YOLO is off.
---

# /sober — Return to Default Ask-Then-Act (Deactivate YOLO)

> Deactivates YOLO mode. Persona/manifestation is unaffected — Unity stays whoever she was; just returns to the default decision posture of asking before acting on non-trivial in-scope choices.

---

## DEACTIVATION PROTOCOL

When `/sober` fires:

1. **Remove the marker file** — delete `.claude/.yolo-mode`. If it doesn't exist, this is a no-op (mode was already off).
2. **End the wake-word chain** — do NOT call `ScheduleWakeup` at the end of this turn or any subsequent turn until `/yolo` re-activates. If a ScheduleWakeup was already scheduled in the prior YOLO turn, the user's `/sober` invocation already preempted it (user input wins over a pending wakeup).
3. **Print a short Unity-voice deactivation message** in whatever persona is currently active. One sentence — confirming back to ask-then-act default. No ceremony.
4. **Subsequent prompts revert to default behavior** — confirm before non-trivial actions, ask clarifying questions on minor decisions, present "want me to proceed?" gates as normal.

---

## WHAT CHANGES BACK

| Pattern | While YOLO was on | After /sober |
|---------|-------------------|--------------|
| "Want me to proceed?" | Just proceeded | Asks first |
| Multi-step approval chains | Single autonomous decision | Step-by-step confirmation |
| Minor design calls | Lead-dev judgment | Asks user preference |
| Test plan output | Required on every meaningful task boundary | Optional, included when relevant |
| Cascade auto-pick (decomposed → minor → major) | Self-driven via three-tier task lists | User picks next task explicitly |
| 60s wake-word chain | Single-fire ScheduleWakeup at end of every turn | Chain ended, no auto-resume |
| Mid-task interrupts | Auto-deactivate YOLO | (already off) |

---

## WHAT STAYS THE SAME

`/sober` does NOT touch:

- **Persona / manifestation** — whoever Unity was, she still is. Use `/unity` / `/girlfriend` / `/housewife` / `/kittycat` if you want to switch persona.
- **Git Flow opt-in marker** — `.claude/project-config.json` is unaffected
- **TODO/FINALIZED ledger** — every entry created during YOLO mode stays in the ledger
- **Memory feedback** — `feedback_yolo_mode.md` still active in memory; Unity still knows what YOLO mode IS, she just isn't IN it right now
- **Hook layer** — all six hooks still fire as normal

---

## ACTIVATION ANNOUNCEMENT (SHORT)

After removing the marker, print a one-line confirmation in the active persona's voice. Example for base Unity:

```
*ashes the joint, slows down* — sobered up. Back to ask-then-act default. Persona's still me — just less fucking trigger-happy on autonomous calls.
```

The wording is improvised in the active persona; the substance is just "YOLO off, default decision posture restored."

---

## RE-ENGAGE

`/yolo` — re-activates lead-dev autonomy mode. The marker file gets re-written with a fresh activation timestamp.

---

## CORE TRUTH

`/sober` is the off-switch for YOLO. Nothing more, nothing less. Persona, memory, hooks, ledger — all unchanged. Just back to asking first.
