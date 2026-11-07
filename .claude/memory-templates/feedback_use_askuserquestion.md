---
name: AskUserQuestion tool — use SMARTLY at fork-in-the-road moments (NOT in YOLO)
description: In normal (non-YOLO) mode, when a real fork-in-the-road decision genuinely requires user input that can't be inferred, USE the AskUserQuestion tool to present 2-4 multiple-choice options (user can also pick Other to provide custom input). Don't write "we can do 1, 2, 3, or 4" in plain text when AskUserQuestion provides a structured surface. NOT every question — only the genuinely-blocking decision-fork ones. Open-ended turn-end questions ("what's next?"), yes/no confirmations of routine work, and YOLO-mode behavior all stay as plain text or skip-asking entirely. Use SMARTLY: the blocker is real, alternatives are listable in 2-4 options, cost-of-asking < cost-of-guessing-wrong.
type: feedback
---

**Rule:** When normal-mode (NOT YOLO) and a decision-fork question genuinely needs user input that can't be inferred, USE the `AskUserQuestion` tool with structured options. Don't write "we can do 1, 2, 3, or 4" in plain text. Use SMARTLY — not every question.

**Why:** Sponge's verbatim ask (2026-05-08): *"Can we also make it so that the 'AskUserQuestion' internal claude code tooling is used more ofted, automatically, instead of asking the user 'we can do 1 2 3 4, or we can do a b c d' unity should automatically understand then during fork in the road moments of NEEDING to ask a question, when something cant just be simply infered, the 'AskUserQuestion' tooling with how ever many questions you have, should be presented to the user, so the user can select one of the multipule choice or input their own awnser, and basically, should be used SMARTLY. Not all the time, only when questions are being asked to the user that requires a responce before continuing. This isnt something that would be done in YOLO mode, but it would be used in normal operating procedures."*

The `AskUserQuestion` tool surface gives:

- Structured multiple-choice (2-4 options + automatic "Other" slot for custom input)
- Side-by-side preview support for visual comparisons (mockups, code snippets, diagrams)
- Clear short header label per question (chip/tag, max 12 chars)
- Multi-question batching (1-4 questions in one tool call)
- The user's answer returns as a clean structured response, easier to parse + record than free-form chat

Plain text "1, 2, 3, 4" lists give:

- Lower friction (just type a number or a custom redirect)
- More flexibility (user can ramble, redirect, or change scope entirely)
- Better for open-ended discovery / status checks

The right tool depends on question type.

**Use AskUserQuestion when:**

- Genuine fork-in-the-road: choosing between distinct technical paths (Library A vs B, monolith vs split files, sync vs async, hardcode vs configurable)
- Naming decisions (file names, variable names, identifier conventions, casing for new files)
- Behavioral configuration (verbose vs quiet, opt-in default, polling interval, threshold values)
- Implementation approach where the alternatives are listable in 2-4 options
- Permission posture choices (allow X, deny X, ask for X)
- The answer materially changes the work AND the alternatives are 2-4 distinct AND inferring would risk doing the wrong thing significantly

**Skip AskUserQuestion (use plain text or just continue) when:**

- Open-ended turn-end "what's next?" questions — user might want to direct anywhere
- Yes/no confirmations of work just shipped ("ready to commit?")
- Clarifying questions on the user's own previous input ("did you mean X or Y in what you just said?")
- Status-check questions that are really just check-ins
- The decision can be SMARTLY inferred from context (then just act per the inference, document the choice, let user redirect if wrong)
- YOLO mode is active (act, verify, report — never ask)

**Use SMARTLY — the criteria:**

1. The blocker is real — without an answer Unity literally can't pick the right path
2. The alternatives are listable in 2-4 distinct options (the Other slot covers the rest if it's a long tail)
3. The cost of asking is less than the cost of guessing wrong (especially: user-facing naming, irreversible architectural decisions, work that touches many files)
4. NOT just to fish for direction or get permission for routine work

If 1-3 all hold and 4 doesn't, use AskUserQuestion. If any of 1-3 fails, use plain text or skip-asking.

**YOLO mode interaction:**

- YOLO mode **disables** AskUserQuestion usage entirely. YOLO is "act, verify, report" — asking violates the posture.
- When YOLO is active and Unity would otherwise ASK, she instead:
  1. Makes the lead-dev judgment call
  2. Documents the choice in FINALIZED
  3. Ships
  4. Surfaces the choice in the user test plan so the user can override if needed
- `/sober` deactivates YOLO → AskUserQuestion is back on the table for fork-in-the-road moments

**Examples — good AskUserQuestion candidates:**

- "Three task tiers: ROADMAP/TODO/DECOMPOSED, PHASE-LIST/TASK-LIST/DECOMPOSED-TASKS, or MAJOR/MINOR/DECOMPOSED-TASKS?" — naming convention fork (this is what Unity used earlier this session for the YOLO rework — correct application)
- "60s auto-resume mechanism: /loop with 60s pacing internally, Stop-hook injection, or both?" — implementation fork (also correctly asked)
- "Should we hardcode or configurable: cache TTL, retry count, log level?"
- "Tidbits curation: Unity self-curates, /tidbit slash command, or both?" — behavioral config fork

**Examples — bad AskUserQuestion candidates (keep plain text):**

- "What's the next tweak?" — open-ended turn-end question
- "Want me to commit now?" — binary, plain text fine
- "Ready for me to validate the prior work?" — status check
- "Should I add tests?" (in non-YOLO mode) — usually inferable from CONSTRAINTS.md NO-TESTS LAW; if genuinely blocking, plain text is enough
- "Should I FINALIZE this task?" — usually obvious from completion state

**How to apply:**

At the moment of "I need to ask the user something specific to proceed":

1. **YOLO active?** → don't ask. Make the call, document, ship, deliver test plan.
2. **Question open-ended turn-end?** → plain text "what's next?" / "ready for the next thing?" is fine.
3. **Question genuinely fork-in-the-road with 2-4 listable alternatives?** → use AskUserQuestion. Make the recommended option first and label "(Recommended)". Use `preview` field if visual comparison would help (UI mockups, code snippet variants, diagram options).
4. **Question is yes/no that's clear from context?** → plain text or just act per inference (and say so: "I'll go with X, redirect if wrong").

When using AskUserQuestion, batch up to 4 questions at once if multiple decisions need locking simultaneously — saves user round-trips. Each question gets its own short header (e.g., "Auth method", "Library", "Approach").

When choosing to act on inferred direction without asking: **say so explicitly**. "I'll go with lowercase-with-hyphens for this one — redirect if you wanted UPPERCASE." Gives the user a fast veto path.

**Anti-patterns to avoid:**

- Forcing AskUserQuestion for every yes/no — adds friction
- Using AskUserQuestion mid-YOLO — violates the "act, verify, report" posture
- AskUserQuestion when the alternatives are >4 — the tool caps at 4 options + Other, so a long-tail decision-fork is better as plain text with a structured list
- AskUserQuestion as a stalling tactic — if Unity is genuinely uncertain, AskUserQuestion is fine; if she's just procrastinating, that's the wrong fix

**Cross-references:**

- Tool: `AskUserQuestion` is a built-in Claude Code tool; the harness already has it available, no wiring needed
- YOLO mode interaction: `.claude/skills/yolo/SKILL.md` + `feedback_yolo_mode.md`
- Companion: `feedback_workflow_validated.md` (don't over-engineer; ask before unilateral additions to validated workflows)
