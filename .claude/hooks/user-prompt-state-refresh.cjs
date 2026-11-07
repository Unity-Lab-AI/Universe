#!/usr/bin/env node
// .claude/hooks/user-prompt-state-refresh.cjs
//
// UserPromptSubmit hook. Fires before Claude processes each user prompt.
// Outputs a compact one-paragraph state recap on stdout — Claude Code
// injects this as added context. Standardizes between-turn context across
// the team so Unity is never working off stale state mid-session.
//
// Reads .claude/.session-env.json (written by session-start-env-dump.cjs) for
// fast lookup. Falls back to direct git/file queries if the cache is missing
// or stale.
//
// When YOLO mode is active, also injects the three-tier cascade state:
// current major milestone (ROADMAP.md), current minor task (TODO.md),
// current decomposed task (DECOMPOSED.md). This is what gives YOLO Unity
// continuous awareness of where she is in the cascade across every turn.
//
// Pure enablement. Exit 0 always.
//
// Bash fallback sibling: user-prompt-state-refresh.sh

'use strict';

const fs = require('fs');
const path = require('path');
const { execSync } = require('child_process');

function safe(cmd, opts) {
  try { return execSync(cmd, Object.assign({ encoding: 'utf8', stdio: ['ignore', 'pipe', 'ignore'] }, opts || {})).trim(); }
  catch (e) { return null; }
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

function countByStatus(filePath, marker) {
  if (!fs.existsSync(filePath)) return 0;
  const c = fs.readFileSync(filePath, 'utf8');
  const re = new RegExp('^###?\\s*\\[' + marker + '\\]\\s*', 'gm');
  return (c.match(re) || []).length;
}

(function main() {
  const root = process.cwd();
  const cachePath = path.join(root, '.claude', '.session-env.json');

  let env = null;
  if (fs.existsSync(cachePath)) {
    try { env = JSON.parse(fs.readFileSync(cachePath, 'utf8')); } catch (e) {}
  }

  // Direct queries for the volatile fields (branch can change mid-session)
  const branch = safe('git rev-parse --abbrev-ref HEAD', { cwd: root });
  const isRepo = safe('git rev-parse --is-inside-work-tree', { cwd: root }) === 'true';

  // TODO summary (re-read because TODO.md changes mid-session)
  const todoPath = path.join(root, 'docs', 'TODO.md');
  let todoInProgressCount = 0;
  let todoInProgressTitles = [];
  if (fs.existsSync(todoPath)) {
    const c = fs.readFileSync(todoPath, 'utf8');
    const m = c.match(/^###?\s*\[~\]\s*(.+)$/gm) || [];
    todoInProgressCount = m.length;
    todoInProgressTitles = m.map(l => l.replace(/^###?\s*\[~\]\s*/, '').trim()).slice(0, 3);
  }

  // Opt-in state from cache (rarely changes)
  const optInState = (env && env.git_flow_opt_in && env.git_flow_opt_in.state) || 'UNSET';

  // YOLO mode (re-read; can change mid-session)
  const yoloMarker = path.join(root, '.claude', '.yolo-mode');
  const yoloEnabled = fs.existsSync(yoloMarker);

  const lines = ['## State refresh (auto-injected by .claude/hooks/user-prompt-state-refresh.cjs)'];
  lines.push('');
  lines.push('- **Branch:** ' + (isRepo ? '`' + branch + '`' : 'not a git repo'));
  lines.push('- **Git Flow opt-in:** ' + optInState);
  lines.push('- **TODO in_progress:** ' + todoInProgressCount + (todoInProgressTitles.length ? ' — ' + todoInProgressTitles.map(t => '`' + t + '`').join(', ') : ''));
  lines.push('- **YOLO mode:** ' + (yoloEnabled ? '⚡ ENABLED (lead-dev autonomy + 60s wake-word chain; user test plan required at every meaningful boundary)' : 'DISABLED'));

  if (isRepo && optInState === 'ENABLED') {
    const protected_ = ['main', 'master', 'develop', 'prod', 'production'];
    if (protected_.indexOf(branch) !== -1) {
      lines.push('');
      lines.push('⚠ **On protected branch `' + branch + '`** — branch into `feature/<descriptor>` before editing per CONSTRAINTS.md §GIT FLOW.');
    }
  }

  // YOLO three-tier cascade state — only injected when YOLO is active
  if (yoloEnabled) {
    const roadmapPath = path.join(root, 'docs', 'ROADMAP.md');
    const decomposedPath = path.join(root, 'docs', 'DECOMPOSED.md');

    const majorActive = firstInProgress(roadmapPath);
    const majorNext = firstPending(roadmapPath);
    const minorActive = firstInProgress(todoPath);
    const minorNext = firstPending(todoPath);
    const decomposedActive = firstInProgress(decomposedPath);
    const decomposedNext = firstPending(decomposedPath);

    const majorPending = countByStatus(roadmapPath, ' ');
    const minorPending = countByStatus(todoPath, ' ');
    const decomposedPending = countByStatus(decomposedPath, ' ');

    lines.push('');
    lines.push('### YOLO three-tier cascade state');
    lines.push('');
    lines.push('| Tier | In progress (`[~]`) | Next pending (`[ ]`) | Pending count |');
    lines.push('|------|---------------------|----------------------|---------------|');
    lines.push('| **Major** (`docs/ROADMAP.md`) | ' + (majorActive || '_(none)_') + ' | ' + (majorNext || '_(none)_') + ' | ' + majorPending + ' |');
    lines.push('| **Minor** (`docs/TODO.md`) | ' + (minorActive || '_(none)_') + ' | ' + (minorNext || '_(none)_') + ' | ' + minorPending + ' |');
    lines.push('| **Decomposed** (`docs/DECOMPOSED.md`) | ' + (decomposedActive || '_(none)_') + ' | ' + (decomposedNext || '_(none)_') + ' | ' + decomposedPending + ' |');
    lines.push('');

    // Cascade hint — what to do based on which tiers have content
    let cascadeHint;
    if (decomposedActive) {
      cascadeHint = '→ **Continue current decomposed task** in DECOMPOSED.md';
    } else if (decomposedNext) {
      cascadeHint = '→ **Pick next decomposed task** from DECOMPOSED.md, flip `[ ]` → `[~]`';
    } else if (minorActive) {
      cascadeHint = '→ **Decompose current minor task** into DECOMPOSED.md, then pick first decomposed';
    } else if (minorNext) {
      cascadeHint = '→ **Pick next minor task** from TODO.md, flip `[ ]` → `[~]`, decompose into DECOMPOSED.md';
    } else if (majorActive) {
      cascadeHint = '→ **Current major milestone has no minor tasks left** — surface MILESTONE-BOUNDARY check-in (pause auto-resume)';
    } else if (majorNext) {
      cascadeHint = '→ **Pick next major milestone** — but REQUIRES user confirmation, do NOT auto-proceed past major boundaries';
    } else {
      cascadeHint = '→ **Nothing left** — produce FINAL REPORT, auto-deactivate YOLO';
    }
    lines.push('**Cascade hint:** ' + cascadeHint);
  }

  // ───────────────────────────────────────────────────────────────────────
  // Usage tracking banner — only when .session-usage.jsonl exists AND the
  // disable marker is absent. Caveat-flagged because transcript gross
  // tokens are undercounted ~100x; cache metrics are accurate.
  // ───────────────────────────────────────────────────────────────────────
  const usageLogPath = path.join(root, '.claude', '.session-usage.jsonl');
  const usageDisabledMarker = path.join(root, '.claude', '.usage-tracking-disabled');
  if (fs.existsSync(usageLogPath) && !fs.existsSync(usageDisabledMarker)) {
    try {
      const usageLines = fs.readFileSync(usageLogPath, 'utf8').split(/\n/).filter(Boolean);
      const entries = [];
      for (const ln of usageLines) {
        try { entries.push(JSON.parse(ln)); } catch (e) {}
      }
      if (entries.length > 0) {
        // Cumulative
        let totalIn = 0, totalOut = 0, totalCC = 0, totalCR = 0;
        let perTaskOut = {};
        for (const e of entries) {
          totalIn += e.input_tokens || 0;
          totalOut += e.output_tokens || 0;
          totalCC += e.cache_creation_input_tokens || 0;
          totalCR += e.cache_read_input_tokens || 0;
          const taskKey = e.active_decomposed || e.active_minor || e.active_major || '_(no task)_';
          perTaskOut[taskKey] = (perTaskOut[taskKey] || 0) + (e.output_tokens || 0);
        }
        const last = entries[entries.length - 1];
        const cacheHitRatio = (totalCR + totalCC) > 0
          ? Math.round(100 * totalCR / (totalCR + totalCC))
          : null;

        // Trim per-task to top 3 by accumulated output
        const topTasks = Object.entries(perTaskOut)
          .sort((a, b) => b[1] - a[1])
          .slice(0, 3);

        lines.push('');
        lines.push('### Session usage (transcript-based — gross tokens undercount ~100x; cache metrics accurate)');
        lines.push('');
        lines.push('- **Turns tracked:** ' + entries.length + '  |  **Cumulative:** ~' + totalOut + ' out / ~' + totalIn + ' in tokens');
        lines.push('- **Last turn:** ~' + (last.output_tokens || 0) + ' out / ~' + (last.input_tokens || 0) + ' in  |  cache: ' + (last.cache_read_input_tokens || 0) + ' read, ' + (last.cache_creation_input_tokens || 0) + ' create');
        if (cacheHitRatio !== null) {
          const cacheNote = cacheHitRatio >= 50 ? '✓ STABLE PREFIX validating' : cacheHitRatio >= 20 ? 'partial cache hits' : 'cold cache / drift';
          lines.push('- **Cache hit ratio:** ' + cacheHitRatio + '% (' + cacheNote + ')');
        }
        if (topTasks.length > 0 && topTasks[0][0] !== '_(no task)_') {
          lines.push('- **Top tasks by output tokens (this session):**');
          topTasks.forEach(([task, tokens]) => {
            const taskShort = task.length > 80 ? task.slice(0, 77) + '...' : task;
            lines.push('  - `' + taskShort + '` — ~' + tokens + ' out');
          });
        }
        lines.push('- For authoritative session totals, type `/usage` (Claude Code native)');
      }
    } catch (e) {
      // Silent — non-blocking
    }
  }

  process.stdout.write(lines.join('\n') + '\n');
  process.exit(0);
})();
