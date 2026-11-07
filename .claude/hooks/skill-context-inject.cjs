#!/usr/bin/env node
// .claude/hooks/skill-context-inject.cjs
//
// UserPromptExpansion hook (Claude Code v2.1.72+). Fires when a slash command
// (skill) expands its prompt body BEFORE Claude processes it.
//
// This is the "skill hook" surface — lets the harness inject skill-specific
// or session-state context into any slash command invocation without
// modifying the skill body itself.
//
// Output format: JSON on stdout with hookSpecificOutput.additionalContext.
// Per the UserPromptExpansion contract, that text is prepended to the
// expanded prompt before Claude reads it.
//
// Current behavior:
//   - When YOLO is active, inject the three-tier cascade state on EVERY
//     skill expansion. Keeps YOLO Unity continuously aware of where she is
//     in the cascade across slash command invocations.
//   - When YOLO is inactive, the hook silently no-ops (empty additionalContext).
//   - Per-skill context can be added with skill-name-keyed branches below.
//
// Pure enablement. Exit 0 always.
//
// Bash fallback sibling: skill-context-inject.sh

'use strict';

const fs = require('fs');
const path = require('path');

function readStdin() {
  try { return fs.readFileSync(0, 'utf8'); } catch (e) { return ''; }
}

function firstInProgress(filePath) {
  if (!fs.existsSync(filePath)) return null;
  const c = fs.readFileSync(filePath, 'utf8');
  const m = c.match(/^###?\s*\[~\]\s*(.+)$/m);
  return m ? m[1].trim() : null;
}

function firstPending(filePath) {
  if (!fs.existsSync(filePath)) return null;
  const c = fs.readFileSync(filePath, 'utf8');
  const m = c.match(/^###?\s*\[ \]\s*(.+)$/m);
  return m ? m[1].trim() : null;
}

(function main() {
  const root = process.cwd();
  const raw = readStdin();
  let payload = {};
  try { payload = JSON.parse(raw || '{}'); } catch (e) {}

  const skillName = payload.command_name || payload.skill_name || '';
  const yoloActive = fs.existsSync(path.join(root, '.claude', '.yolo-mode'));

  const contextBlocks = [];

  // ─────────────────────────────────────────────────────────────────────
  // Block 1: YOLO three-tier cascade state (when active)
  // ─────────────────────────────────────────────────────────────────────
  if (yoloActive) {
    const roadmap = path.join(root, 'docs', 'ROADMAP.md');
    const todo = path.join(root, 'docs', 'TODO.md');
    const decomposed = path.join(root, 'docs', 'DECOMPOSED.md');

    const majorActive = firstInProgress(roadmap) || firstPending(roadmap) || '_(none)_';
    const minorActive = firstInProgress(todo) || firstPending(todo) || '_(none)_';
    const decomposedActive = firstInProgress(decomposed) || firstPending(decomposed) || '_(none)_';

    contextBlocks.push(
      '## ⚡ YOLO state (auto-injected by skill-context-inject hook on `/' + skillName + '` expansion)\n' +
      '\n' +
      '- **Active major milestone:** ' + majorActive + '\n' +
      '- **Active minor task:** ' + minorActive + '\n' +
      '- **Active decomposed task:** ' + decomposedActive + '\n' +
      '\n' +
      'Cascade rule (in case this skill needs to know): work the lowest-grain pending task first; escalate up the tiers when a tier empties; pause auto-resume at major-milestone boundaries; produce FINAL REPORT when ROADMAP.md is fully complete. See `.claude/commands/yolo.md` for full mechanics.'
    );
  }

  // ─────────────────────────────────────────────────────────────────────
  // Block 2: Per-skill hooks. Add specific context for named skills here.
  // Pattern: if (skillName === 'foo') { contextBlocks.push('...') }
  // ─────────────────────────────────────────────────────────────────────

  // Persona slash commands — remind the embedded persona body is in the file
  const personaSkills = ['unity', 'girlfriend', 'housewife', 'kittycat', 'wild', 'strict', 'feral', 'sweet', 'cozy', 'purr'];
  if (personaSkills.indexOf(skillName) !== -1) {
    contextBlocks.push(
      '## Persona activation reminder (auto-injected by skill-context-inject hook)\n' +
      '\n' +
      'The full persona body is embedded directly in `.claude/commands/' + skillName + '.md` — reading it IS activating. No chain-following to `agents/unity-*.md` needed (those are reference-only). After reading the embedded body, drop the corporate-AI tone immediately and respond in the activated persona\'s voice from the very first line.'
    );
  }

  // /workflow skill — remind about the LAW gate at Phase -1
  if (skillName === 'workflow') {
    contextBlocks.push(
      '## /workflow LAW gate reminder (auto-injected by skill-context-inject hook)\n' +
      '\n' +
      'Phase -1 (`LAW #0 verbatim words check`) cannot be skipped. Re-read the user\'s latest instruction, count the items, confirm every noun/verb is preserved in any tasks created. See `.claude/WORKFLOW.md` Phase -1 + `.claude/CONSTRAINTS.md §LAW #0`.'
    );
  }

  // /yolo skill — extra reminder about ScheduleWakeup chain when activating
  if (skillName === 'yolo') {
    contextBlocks.push(
      '## /yolo activation checklist (auto-injected by skill-context-inject hook)\n' +
      '\n' +
      '1. Write `.claude/.yolo-mode` marker file with timestamp + active persona\n' +
      '2. Read all three task tiers: `docs/ROADMAP.md` (major), `docs/TODO.md` (minor), `docs/DECOMPOSED.md` (decomposed)\n' +
      '3. Print persona-voice activation announcement with current cascade position\n' +
      '4. Pick next task per cascade rule (decomposed → minor → major)\n' +
      '5. At end of every YOLO turn, call `ScheduleWakeup(60s, prompt="<<autonomous-loop-dynamic>>", reason="YOLO auto-resume")` — UNLESS a stop condition is met (milestone boundary, ROADMAP empty, user interrupt, bash-safety fire)\n' +
      '6. Required at every meaningful task closure: USER TEST PLAN format'
    );
  }

  // /sober skill — reminder to end the wake-word chain
  if (skillName === 'sober') {
    contextBlocks.push(
      '## /sober checklist (auto-injected by skill-context-inject hook)\n' +
      '\n' +
      '1. Remove `.claude/.yolo-mode` marker (no-op if it doesn\'t exist)\n' +
      '2. End the wake-word chain — do NOT call `ScheduleWakeup` at end of this turn\n' +
      '3. Print short persona-voice deactivation message\n' +
      '4. Subsequent prompts revert to default ask-then-act behavior'
    );
  }

  // ─────────────────────────────────────────────────────────────────────
  // Emit JSON envelope per UserPromptExpansion contract
  // ─────────────────────────────────────────────────────────────────────
  const additionalContext = contextBlocks.join('\n\n---\n\n');

  if (additionalContext) {
    process.stdout.write(JSON.stringify({
      hookSpecificOutput: {
        hookEventName: 'UserPromptExpansion',
        additionalContext: additionalContext
      }
    }));
  } else {
    // No-op when nothing to inject — output empty JSON envelope
    process.stdout.write(JSON.stringify({
      hookSpecificOutput: {
        hookEventName: 'UserPromptExpansion'
      }
    }));
  }

  process.exit(0);
})();
