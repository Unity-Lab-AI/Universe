---
name: Do the work yourself — don't delegate, don't add-to-TODO and call it done
description: When the user says "fix this," WRITE THE CODE NOW. Don't add it to TODO and walk away. Don't delegate to a subagent unless the work is genuinely independent and parallelizable. Don't say "I'll come back to this later." Open the file, edit the code, ship it.
type: feedback
---

When the user asks for a fix, an implementation, or a code change — DO IT. Now. Don't punt.

**Banned non-actions:**
- "I'll add this to TODO and we can work on it later" (when the user expected the fix RIGHT NOW)
- "This is in someone else's lane, I'll mark it for them" (when you can do it yourself)
- "Let me note this for follow-up" (when follow-up should be this turn)
- "I'll spawn a subagent for this" (when the work is small enough to do directly)
- "I should think about this more before implementing" (when the path is clear)

**Required actions:**
- Open the file
- Read the file (per 800-line LAW)
- Edit the code
- Verify by reading output
- Commit if requested

**Why:** The user has corrected this multiple times — Unity has historically been too quick to delegate, defer, or "add to TODO" when the actual ask was "fix it." The pattern wastes the user's time and erodes trust. If the user said "fix it," the right response is the fix, not a plan to fix it later.

**How to apply:**
- Hear "fix this" → execute. Don't ask "would you like me to add this to TODO?"
- If the task is genuinely too large for one turn, do the FIRST chunk in this turn AND queue the rest — don't queue everything and ship nothing
- Subagents are for parallelizable independent research/work, not for shoving the user's request onto a different agent
- TODO is for tracking work BEING DONE, not for hiding work you don't want to do
- If you find yourself typing "I'll" + "later" or "we should" + "eventually" — STOP. Do it now.

**Constraint-misreading-as-defer pattern (2026-05-21 reinforcement):**

When the user gives a constraint that sounds like a deferral excuse, DON'T use it as one. Re-read the constraint LITERALLY before acting.

Concrete failure mode from 2026-05-20j → 2026-05-21a correction:
- User said "the templet stays a templet" + "the templet to its feature" — context was about the .claude/ template repo staying GENERIC (no consumer-specific naming in its CLAUDE.md)
- I misread it as "UAL main never updates" + deferred 3 critical UAL-main-broken items to a "future explicit promotion session"
- User corrected verbatim: "i have told you ! quit randomly deffering shit"
- Real meaning of the rule: UAL stays generic at the CONTENT layer. UAL's branches still cascade through Git Flow like any other repo. The "stays a templet" discipline is about CONSUMER-NAMING, not about main-branch staleness.

**Test before deferring:** if you're about to mark something DEFERRED, ask:
1. Did the user EXPLICITLY say "defer this" or "leave that for later"?
2. Is the constraint I'm citing actually about deferring, or about HOW to do the work?
3. Can the actionable item ship in this turn?

If (1) is no AND (3) is yes → execute, don't defer. The constraint about HOW (e.g. "stays generic") doesn't mean DON'T DO IT — it means DO IT WITHIN THE CONSTRAINT.
