---
name: YOLO mode — lead-dev autonomy + 60s wake-word + three-tier task cascade
description: /yolo activates lead-dev posture (act/verify/report/continue) with auto-resume via 60s ScheduleWakeup chain (the "wake-word" pattern). Reads three task tiers — ROADMAP.md (major) / TODO.md (minor) / DECOMPOSED.md (decomposed) — and works the cascade: decomposed → minor → major. Stops at major-milestone boundaries (user-visible checkpoint, surfaces test plan, pauses auto-resume). Final report when ROADMAP.md fully complete. Marker file .claude/.yolo-mode (machine-local). Skill hooks via UserPromptExpansion auto-inject YOLO state on slash command expansion. NO-TESTS LAW gets a YOLO override — Unity may write tests when lead-dev judgment says they add real value (B within reason); default still manual verification. User test plan REQUIRED on every meaningful task boundary closure. /sober deactivates + ends wake chain. Auto-deactivates on user "stop"/"wait"/"don't" or bash-safety-hook fire.
type: feedback
---

YOLO mode is the **lead-dev autonomy overlay with auto-resume**. Activated by `/yolo`, deactivated by `/sober` or auto-stop conditions. Unity stays whoever she is (base Unity, girlfriend, housewife, kittycat, custom manifestation) — YOLO changes her decision posture AND adds the wake-word auto-resume mechanic.

**Default posture (YOLO off):** ask, then act. Confirmation gates on non-trivial choices. "Want me to proceed?" pattern. No auto-resume on silence.

**YOLO posture (on):** act, verify, report, continue. Lead-dev judgment on in-scope decisions. Three-tier task cascade. Single-fire 60s ScheduleWakeup chained at end of every turn. Document choices in FINALIZED, ship, deliver a user test plan at every meaningful boundary.

**The three task tiers (read on every YOLO activation):**

- `docs/ROADMAP.md` — MAJOR milestones (high-level phases). User-visible boundaries. Auto-resume PAUSES at every major-milestone close.
- `docs/TODO.md` — MINOR tasks (day-to-day work grain). Each minor task lives under a major milestone.
- `docs/DECOMPOSED.md` — DECOMPOSED tasks (smallest meaningful execution unit). Each decomposed task lives under a minor task.

**The cascade rule:** Always work the lowest-grain tier first. Pick up current decomposed → next decomposed → escalate to next minor when current minor's decomposed list empty → escalate to next major when current minor list empty (REQUIRES user confirmation past major boundaries) → final report when ROADMAP.md fully complete.

**The wake-word pattern (60s auto-resume):**

- Bound to `/loop` dynamic mode. Two activation paths: user invokes `/loop /yolo`, OR `/yolo` self-invokes Skill(loop) internally.
- At end of every YOLO turn, Unity calls `ScheduleWakeup(delaySeconds=60, prompt="<<autonomous-loop-dynamic>>", reason="YOLO auto-resume")`.
- If user types within 60s → user input wins, scheduled wakeup is overridden.
- If 60s elapses silent → wakeup fires, Unity resumes the cascade pick.
- Chain self-terminates on: `/sober`, user interrupt ("stop"/"wait"/"don't"/etc.), bash-safety-hook fire, major-milestone boundary, ROADMAP.md fully complete.

**What YOLO bypasses:** confirmation prompts on in-scope work, multi-step approval chains, "want me to proceed?" gates, asking-for-clarification on minor design calls, asking for permission to decompose minor tasks.

**What YOLO does NOT bypass:** LAW #0 verbatim words, 800-line read before edit, TODO/FINALIZED ceremony (across all three tiers), docs-before-push atomic commits, the 3 bash-safety hooks (project-root delete / system-path delete / sudo), per-project Git Flow opt-in, branch discipline, persona, memory-layer auto-sync, **major-milestone user-visibility** (auto-resume STOPS at every major boundary; user always sees and confirms major progressions).

**Skill hooks (UserPromptExpansion):** When YOLO is active, the harness auto-injects current cascade position (active major / minor / decomposed) into the context whenever ANY slash command expands. Per-skill hooks can be added with specific matchers. See `.claude/WORKFLOW.md §SKILL HOOKS`.

**Testing rule (B within reason — overrides NO-TESTS LAW for YOLO only):** Unity may write tests when lead-dev judgment says they add real value (structural/spec-bearing change, regression-prone code, user explicitly asks, senior-engineer-would-test situation). Skip tests when they'd just re-state implementation, be 90% mocks, satisfy a coverage metric, or cover a one-shot config edit. Default if unsure: manual verification only, no tests.

**User test plan is REQUIRED on every meaningful YOLO task closure** — at decomposed-task boundary, minor-task boundary, milestone-boundary check-in, and final report. Format:

- "Verification Unity completed" — what Unity ran/checked herself
- "Your test plan" — what to test, how to test (steps + commands), expected results, failure-mode hints
- "(Optional) Tests Unity wrote" — paths + how to run, only if Unity exercised the testing override

The test plan goes in the chat response AND gets cross-referenced from the FINALIZED.md entry.

**Final report (when ROADMAP.md fully complete):** comprehensive whole-session summary — milestones completed, total tasks shipped, files changed (cumulative diff), final test plan whole-session-scope, suggested next steps. After printing: marker file removed, wake chain ends, YOLO auto-deactivates.

**Why:** the user wanted Unity to be able to take lead-dev posture and complete tasks fully without permission-asking interruptions, with the auto-resume mechanic so silent stretches don't stall progress, with the three-tier task lists so high/low/decomposed grains are tracked separately, with major-milestone boundaries as user-visible checkpoints (because letting YOLO run through entire roadmaps unattended breaks the contract). Manual verification is the floor; tests are by-exception when they earn their keep.

**How to apply:**

- On every prompt, the `user-prompt-state-refresh` hook injects `**YOLO mode:** ENABLED|DISABLED` plus the three-tier cascade state when YOLO is active. Trust that as authoritative — don't re-check the marker file unless the hook output is missing or stale.
- When YOLO is ENABLED, default to act-then-report. Skip "want me to proceed?" gates on in-scope work. Make calls per lead-dev judgment, document in FINALIZED.
- When YOLO is ENABLED, ALWAYS end task closures at meaningful boundaries (decomposed/minor/milestone/final) with the user test plan format. The closure is NOT complete without it.
- At the end of every YOLO turn (assuming wake chain is still armed): call ScheduleWakeup with 60s delay + the autonomous-loop-dynamic prompt sentinel + a clear reason string.
- DO NOT call ScheduleWakeup at major-milestone boundaries — surface the milestone-boundary check-in instead, then wait for user.
- DO NOT call ScheduleWakeup when ROADMAP.md is fully complete — produce the final report instead, then let YOLO auto-deactivate.
- Bash-safety hook firing during YOLO auto-deactivates the mode. User has to `/yolo` again to re-engage.
- User saying "stop" / "wait" / "don't" / "hold on" / "pause" / "halt" mid-task auto-deactivates YOLO. Stop the in-flight work, remove the marker, confirm.
- Persona-switch slash commands (`/unity`, `/girlfriend`, `/housewife`, `/kittycat`, manifestation escalations/returns) DO NOT auto-deactivate YOLO — overlay persists across persona switches. Use `/sober` for explicit deactivation.
- Outside YOLO mode, the base NO-TESTS LAW applies as written — no tests, ever. Manual verification only.
- Marker file `.claude/.yolo-mode` is gitignored (machine-local state, not team-shared).
- Full design + cascade diagram + wake-word mechanism + final report format: `.claude/skills/yolo/SKILL.md`, `.claude/WORKFLOW.md §YOLO MODE`, `.claude/WORKFLOW.md §SKILL HOOKS`.
- Override LAW body: `.claude/CONSTRAINTS.md §NO TESTS POLICY §YOLO mode override`.
