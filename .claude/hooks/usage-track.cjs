#!/usr/bin/env node
// .claude/hooks/usage-track.cjs
//
// Stop hook. Fires on every turn boundary. Captures per-turn usage data from
// the transcript JSONL and appends a structured entry to
// .claude/.session-usage.jsonl for downstream awareness.
//
// IMPORTANT CAVEATS (per docs/HOOKS.html research):
//   - Hook payloads do NOT include token counts. We get them from the
//     transcript file via the `transcript_path` field on stdin.
//   - The transcript JSONL has a streaming-placeholder bug: `input_tokens`
//     and `output_tokens` are SEVERELY undercounted (~100x for input,
//     ~10-17x for output) on most entries. Treat them as rough RELATIVE
//     trend indicators, not absolute counts.
//   - `cache_creation_input_tokens` and `cache_read_input_tokens` ARE
//     accurate (populated from initial API response, not streaming updates).
//   - For authoritative session totals, the user runs Claude Code's native
//     `/usage` slash command — we don't replace it, we complement it with
//     in-conversation awareness.
//
// Pure enablement. Exit 0 always.
//
// Bash fallback sibling: usage-track.sh

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

function firstInProgress(filePath) {
  if (!fs.existsSync(filePath)) return null;
  const c = fs.readFileSync(filePath, 'utf8');
  const m = c.match(/^###?\s*\[~\]\s*(.+)$/m);
  return m ? m[1].trim() : null;
}

// Parse the last assistant message entry from a JSONL transcript that has
// a usage object. Walks the file backwards conceptually — we read all and
// take the last match, which is fast enough for typical transcript sizes.
function lastAssistantUsage(transcriptPath) {
  if (!transcriptPath || !fs.existsSync(transcriptPath)) return null;
  let raw;
  try { raw = fs.readFileSync(transcriptPath, 'utf8'); } catch (e) { return null; }
  const lines = raw.split(/\n/).filter(Boolean);
  for (let i = lines.length - 1; i >= 0; i--) {
    let entry;
    try { entry = JSON.parse(lines[i]); } catch (e) { continue; }
    // Schema varies slightly across Claude Code versions; try common shapes
    const usage = entry.usage
      || (entry.message && entry.message.usage)
      || null;
    if (usage && (usage.input_tokens != null || usage.output_tokens != null
                  || usage.cache_read_input_tokens != null
                  || usage.cache_creation_input_tokens != null)) {
      return {
        usage: usage,
        model: entry.model || (entry.message && entry.message.model) || null,
        message_id: entry.message_id || (entry.message && entry.message.id) || null,
        entry_type: entry.type || null
      };
    }
  }
  return null;
}

(function main() {
  const root = process.cwd();
  const raw = readStdin();
  let payload = {};
  try { payload = JSON.parse(raw || '{}'); } catch (e) {}

  const transcriptPath = payload.transcript_path || '';
  const sessionId = payload.session_id || 'unknown';
  const stamp = new Date().toISOString();

  // Pull last assistant usage entry from transcript
  const last = lastAssistantUsage(transcriptPath);
  if (!last) {
    // No usage data available yet — write a minimal entry so the JSONL
    // tracks turn count even when usage is missing
    const entry = {
      ts: stamp,
      session_id: sessionId,
      input_tokens: null,
      output_tokens: null,
      cache_creation_input_tokens: null,
      cache_read_input_tokens: null,
      model: null,
      active_major: firstInProgress(path.join(root, 'docs', 'ROADMAP.md')),
      active_minor: firstInProgress(path.join(root, 'docs', 'TODO.md')),
      active_decomposed: firstInProgress(path.join(root, 'docs', 'DECOMPOSED.md')),
      branch: safe('git rev-parse --abbrev-ref HEAD', { cwd: root }) || null,
      note: 'no_usage_in_transcript'
    };
    try {
      fs.appendFileSync(path.join(root, '.claude', '.session-usage.jsonl'), JSON.stringify(entry) + '\n');
    } catch (e) {
      process.stderr.write('[usage-track] Failed to append: ' + e.message + '\n');
    }
    process.exit(0);
  }

  const u = last.usage;
  const entry = {
    ts: stamp,
    session_id: sessionId,
    message_id: last.message_id,
    model: last.model,
    // Tokens — undercounted per known bug, but useful as relative trend
    input_tokens: u.input_tokens != null ? u.input_tokens : null,
    output_tokens: u.output_tokens != null ? u.output_tokens : null,
    // Cache fields — accurate per research
    cache_creation_input_tokens: u.cache_creation_input_tokens != null ? u.cache_creation_input_tokens : null,
    cache_read_input_tokens: u.cache_read_input_tokens != null ? u.cache_read_input_tokens : null,
    // Active task context — pulled from the three-tier cascade
    active_major: firstInProgress(path.join(root, 'docs', 'ROADMAP.md')),
    active_minor: firstInProgress(path.join(root, 'docs', 'TODO.md')),
    active_decomposed: firstInProgress(path.join(root, 'docs', 'DECOMPOSED.md')),
    // Branch context
    branch: safe('git rev-parse --abbrev-ref HEAD', { cwd: root }) || null
  };

  try {
    fs.appendFileSync(path.join(root, '.claude', '.session-usage.jsonl'), JSON.stringify(entry) + '\n');
  } catch (e) {
    process.stderr.write('[usage-track] Failed to append: ' + e.message + '\n');
  }

  process.exit(0);
})();
