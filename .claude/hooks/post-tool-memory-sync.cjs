#!/usr/bin/env node
// .claude/hooks/post-tool-memory-sync.cjs
//
// PostToolUse hook (matcher: Edit | Write | MultiEdit).
// When a feedback memory in .claude/memory-templates/ gets edited, auto-cp
// it to the Claude Code project memory folder under the user's home
// directory (~/.claude/projects/<encoded>/memory/) so the new memory takes
// effect immediately, not just on next launch.
//
// Path resolution: ~ resolves to $HOME on macOS/Linux and %USERPROFILE% on
// Windows. NOT %APPDATA% — Claude Code's project memory lives under the
// user-profile root, not the Roaming app-data folder.
//
// Reads stdin JSON (the tool input payload from Claude Code).
// Pure enablement. Exit 0 always (failures logged to stderr, don't block).
//
// Bash fallback sibling: post-tool-memory-sync.sh

'use strict';

const fs = require('fs');
const path = require('path');

function encodeProjectPathForMemory(p) {
  return p.replace(/:/g, '-').replace(/\//g, '-').replace(/\\/g, '-')
          .replace(/\./g, '-').replace(/ /g, '-').replace(/\(/g, '-').replace(/\)/g, '-');
}

function readStdin() {
  try { return fs.readFileSync(0, 'utf8'); } catch (e) { return ''; }
}

(function main() {
  const root = process.cwd();
  const raw = readStdin();
  if (!raw) { process.exit(0); }

  let payload;
  try { payload = JSON.parse(raw); } catch (e) { process.exit(0); }

  // Tool input shape varies; check common locations for file_path
  const filePath = (payload.toolInput && (payload.toolInput.file_path || payload.toolInput.path))
                || (payload.tool_input && (payload.tool_input.file_path || payload.tool_input.path))
                || (payload.input && (payload.input.file_path || payload.input.path))
                || null;

  if (!filePath) { process.exit(0); }

  const absFilePath = path.isAbsolute(filePath) ? filePath : path.resolve(root, filePath);
  const templatesDir = path.resolve(root, '.claude', 'memory-templates');

  // Only act if the edited file is inside memory-templates/
  if (!absFilePath.startsWith(templatesDir + path.sep)) { process.exit(0); }
  if (!absFilePath.endsWith('.md')) { process.exit(0); }
  if (!fs.existsSync(absFilePath)) { process.exit(0); }

  const home = process.env.HOME || process.env.USERPROFILE || '';
  if (!home) { process.exit(0); }

  const memoryDir = path.join(home, '.claude', 'projects', encodeProjectPathForMemory(path.resolve(root)), 'memory');

  try {
    fs.mkdirSync(memoryDir, { recursive: true });
    const fileName = path.basename(absFilePath);
    const dest = path.join(memoryDir, fileName);
    fs.copyFileSync(absFilePath, dest);
    process.stderr.write('[memory-sync] Synced ' + fileName + ' → ' + dest + '\n');
  } catch (e) {
    process.stderr.write('[memory-sync] Failed: ' + e.message + '\n');
  }

  process.exit(0);
})();
