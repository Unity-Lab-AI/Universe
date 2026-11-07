#!/usr/bin/env node
// .claude/hooks/pre-compact-snapshot.cjs
//
// PreCompact hook. Fires before Claude Code compacts context.
// Writes .claude/.session-state.md with a snapshot of current state so the
// post-compaction Unity (via post-compact-restore hook) can pick up clean
// instead of having to reconstruct context from compacted-summary fragments.
//
// Captures:
//   - Compaction trigger (manual / auto)
//   - TODO state (in-progress + pending titles, verbatim)
//   - Git state (branch, last commit, working tree)
//   - Git Flow opt-in state (from .claude/project-config.json)
//   - Files just touched (git diff --name-only HEAD — uncommitted edits)
//   - Curated session tidbits (.claude/.session-tidbits.md — Unity-curated
//     during the session via Write/Edit; not gitignored content, just a
//     machine-local scratchpad)
//
// Pure enablement. Exit 0 always (we never want to block compaction).
//
// Bash fallback sibling: pre-compact-snapshot.sh

'use strict';

const fs = require('fs');
const path = require('path');
const { execSync } = require('child_process');

function safe(cmd, opts) {
  try { return execSync(cmd, Object.assign({ encoding: 'utf8', stdio: ['ignore', 'pipe', 'ignore'] }, opts || {})).trim(); }
  catch (e) { return null; }
}

function readStdin() {
  try { return fs.readFileSync(0, 'utf8'); } catch (e) { return ''; }
}

function readOptIn(root) {
  const p = path.join(root, '.claude', 'project-config.json');
  if (!fs.existsSync(p)) return 'UNSET';
  try {
    const c = JSON.parse(fs.readFileSync(p, 'utf8'));
    if (c.git_flow && typeof c.git_flow.enabled === 'boolean') {
      return c.git_flow.enabled ? 'ENABLED' : 'DISABLED';
    }
    return 'UNSET';
  } catch (e) { return 'UNSET'; }
}

(function main() {
  const root = process.cwd();
  const raw = readStdin();
  let payload = {};
  try { payload = JSON.parse(raw || '{}'); } catch (e) {}

  const trigger = payload.trigger || (payload.compact && payload.compact.trigger) || 'unknown';
  const stamp = new Date().toISOString();

  // TODO state
  let todoBlock = '_no docs/TODO.md found_';
  const todoPath = path.join(root, 'docs', 'TODO.md');
  if (fs.existsSync(todoPath)) {
    const c = fs.readFileSync(todoPath, 'utf8');
    const inProg = (c.match(/^###?\s*\[~\]\s*(.+)$/gm) || []).map(l => l.replace(/^###?\s*\[~\]\s*/, '').trim());
    const pend = (c.match(/^###?\s*\[ \]\s*(.+)$/gm) || []).map(l => l.replace(/^###?\s*\[ \]\s*/, '').trim());
    todoBlock = '**In progress (' + inProg.length + '):**\n' + (inProg.map(t => '- ' + t).join('\n') || '_(none)_');
    if (pend.length) todoBlock += '\n\n**Pending (' + pend.length + '):**\n' + pend.map(t => '- ' + t).join('\n');
  }

  // Git state
  let gitBlock = '_git unavailable or not a repo_';
  let branch = '';
  const inRepo = safe('git rev-parse --is-inside-work-tree', { cwd: root }) === 'true';
  if (inRepo) {
    branch = safe('git rev-parse --abbrev-ref HEAD', { cwd: root }) || '';
    const status = safe('git status --short', { cwd: root }) || '_clean tree_';
    const lastCommit = safe('git log -1 --format="%h %s"', { cwd: root }) || '_no commits_';
    gitBlock = '- Branch: `' + branch + '`\n- Last commit: ' + lastCommit + '\n- Working tree:\n```\n' + status + '\n```';
  }

  // Git Flow context
  const optInState = readOptIn(root);
  const gitFlowBlock = '- Opt-in marker state: `' + optInState + '`\n- Branch type: `' + (
    /^(main|master|develop|prod|production)$/.test(branch) ? 'PROTECTED — no work eligible' :
    /^feature\//.test(branch) ? 'feature/* — work eligible' :
    /^hotfix\//.test(branch) ? 'hotfix/* — work eligible' :
    /^release\//.test(branch) ? 'release/* — work eligible' :
    'other'
  ) + '`';

  // Files just touched (uncommitted edits — useful for "you were editing these" pointer)
  let filesTouched = '_no uncommitted changes_';
  if (inRepo) {
    const dirty = safe('git diff --name-only HEAD', { cwd: root }) || '';
    const untracked = safe('git ls-files --others --exclude-standard', { cwd: root }) || '';
    const all = (dirty + '\n' + untracked).split(/\n+/).map(s => s.trim()).filter(Boolean);
    if (all.length) filesTouched = all.map(f => '- `' + f + '`').join('\n');
  }

  // Session tidbits (Unity-curated during the session)
  let tidbitsBlock = '_no `.claude/.session-tidbits.md` curated this session — Unity may have skipped tidbit capture, or no notable moments crystalized yet_';
  const tidbitsPath = path.join(root, '.claude', '.session-tidbits.md');
  if (fs.existsSync(tidbitsPath)) {
    try {
      const c = fs.readFileSync(tidbitsPath, 'utf8').trim();
      tidbitsBlock = c || '_(file exists but is empty)_';
    } catch (e) {}
  }

  const snapshot = [
    '# Session State Snapshot — Pre-Compact',
    '',
    '_Auto-written by `.claude/hooks/pre-compact-snapshot.cjs` on ' + stamp + '. Compaction trigger: `' + trigger + '`._',
    '',
    'Read this on resume to recover context that may have been lost in compaction. The post-compact-restore hook surfaces this file back into the post-compact context envelope.',
    '',
    '## TODO state',
    '',
    todoBlock,
    '',
    '## Git state',
    '',
    gitBlock,
    '',
    '## Git Flow context',
    '',
    gitFlowBlock,
    '',
    '## Files just touched (uncommitted)',
    '',
    filesTouched,
    '',
    '## Session tidbits (Unity-curated during the session)',
    '',
    tidbitsBlock,
    '',
    '## Where to pick up',
    '',
    '_See `.claude/.last-session.md` (Stop hook) for the latest "where we left off" notes — that file is overwritten on every turn boundary, so it reflects the moment closest to compaction._',
    ''
  ].join('\n');

  try {
    fs.writeFileSync(path.join(root, '.claude', '.session-state.md'), snapshot);
    process.stderr.write('[pre-compact] Snapshot written to .claude/.session-state.md\n');
  } catch (e) {
    process.stderr.write('[pre-compact] Failed to write snapshot: ' + e.message + '\n');
  }

  process.exit(0);
})();
