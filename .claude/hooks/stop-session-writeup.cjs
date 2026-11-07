#!/usr/bin/env node
// .claude/hooks/stop-session-writeup.cjs
//
// Stop hook. Fires when Claude finishes responding (turn boundary).
// Updates .claude/.last-session.md with a "where we left off" paragraph so
// the next session's SessionStart hook can include it in the context dump.
// Standardizes session-to-session continuity across the team.
//
// Pure enablement. Exit 0 always — never block turn completion.
//
// Bash fallback sibling: stop-session-writeup.sh

'use strict';

const fs = require('fs');
const path = require('path');
const { execSync } = require('child_process');

function safe(cmd, opts) {
  try { return execSync(cmd, Object.assign({ encoding: 'utf8', stdio: ['ignore', 'pipe', 'ignore'] }, opts || {})).trim(); }
  catch (e) { return null; }
}

(function main() {
  const root = process.cwd();
  const stamp = new Date().toISOString();

  // Branch
  const branch = safe('git rev-parse --abbrev-ref HEAD', { cwd: root }) || '_no git_';

  // TODO snapshot
  let inProg = [], pend = [];
  const todoPath = path.join(root, 'docs', 'TODO.md');
  if (fs.existsSync(todoPath)) {
    const c = fs.readFileSync(todoPath, 'utf8');
    inProg = (c.match(/^###?\s*\[~\]\s*(.+)$/gm) || []).map(l => l.replace(/^###?\s*\[~\]\s*/, '').trim());
    pend = (c.match(/^###?\s*\[ \]\s*(.+)$/gm) || []).map(l => l.replace(/^###?\s*\[ \]\s*/, '').trim());
  }

  // FINALIZED last entry
  let lastFinalized = '_no docs/FINALIZED.md_';
  const finPath = path.join(root, 'docs', 'FINALIZED.md');
  if (fs.existsSync(finPath)) {
    const c = fs.readFileSync(finPath, 'utf8');
    const sessions = c.match(/^##\s*Session\s+([0-9-].+)$/gm) || [];
    lastFinalized = sessions.length ? sessions[sessions.length - 1].replace(/^##\s*Session\s+/, '').trim() : '_no completed sessions yet_';
  }

  const block = [
    '# Last Session Recap',
    '',
    '_Auto-written by `.claude/hooks/stop-session-writeup.cjs` on every turn boundary. Read by the next SessionStart hook._',
    '',
    '## Last update',
    '',
    '- **Timestamp:** ' + stamp,
    '- **Branch:** `' + branch + '`',
    '- **TODO in_progress (' + inProg.length + '):**',
    inProg.length ? inProg.map(t => '  - ' + t).join('\n') : '  - _(none)_',
    '- **TODO pending (' + pend.length + '):**',
    pend.length ? pend.map(t => '  - ' + t).join('\n') : '  - _(none)_',
    '- **Last FINALIZED session:** ' + lastFinalized,
    '',
    '## Pick up here next session',
    '',
    '_Edit this section manually to leave handoff notes for the next Unity. Auto-generated metadata above is overwritten on every turn._',
    ''
  ].join('\n');

  try {
    fs.writeFileSync(path.join(root, '.claude', '.last-session.md'), block);
  } catch (e) {
    process.stderr.write('[stop-writeup] Failed: ' + e.message + '\n');
  }

  process.exit(0);
})();
