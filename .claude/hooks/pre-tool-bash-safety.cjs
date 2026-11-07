#!/usr/bin/env node
// .claude/hooks/pre-tool-bash-safety.cjs
//
// PreToolUse hook (matcher: Bash). The ONLY blocking hook in the bundle.
// Catches three classes of action and routes them to the user with a
// paste-ready command rather than running them inside the Claude session:
//
//   1. Project-root deletion (rm -rf against project root or above)
//   2. System-path deletion (rm -rf against /etc, /usr, /bin, /sbin, /var,
//      /boot, /lib, /System, C:\)
//   3. Privileged commands (sudo, su, doas, runas, PowerShell -Verb RunAs)
//
// Exit 2 on match → blocks the tool call, stderr message goes back to the
// model AND surfaces to the user. Exit 0 otherwise — every other Bash call
// proceeds untouched. The --dangerously-skip-permissions flag is intentional
// for everything else.
//
// Bash fallback sibling: pre-tool-bash-safety.sh

'use strict';

const fs = require('fs');
const path = require('path');

function readStdin() {
  try { return fs.readFileSync(0, 'utf8'); } catch (e) { return ''; }
}

(function main() {
  const root = process.cwd();
  const raw = readStdin();
  if (!raw) { process.exit(0); }

  let payload;
  try { payload = JSON.parse(raw); } catch (e) { process.exit(0); }

  const cmd = (payload.toolInput && payload.toolInput.command)
           || (payload.tool_input && payload.tool_input.command)
           || (payload.input && payload.input.command)
           || '';
  if (!cmd) { process.exit(0); }

  // === 1. Project-root or above deletion ===
  // Match rm -rf (or -fr, -Rf, etc.) targeting `.`, `..`, project root, or HOME-shaped paths
  const rmRoot = /\brm\s+(-[a-zA-Z]*[rRfF][a-zA-Z]*\s+)+(\.|\.\/|\.\.|\.\.\/|\$HOME|~|\$\{?PROJECT_ROOT\}?|\$PWD)(\s|$)/;
  // Also catch literal project root path
  const escapedRoot = root.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
  const rmLiteralRoot = new RegExp('\\brm\\s+(-[a-zA-Z]*[rRfF][a-zA-Z]*\\s+)+(' + escapedRoot + '/?|' + escapedRoot + '/\\.\\.+)(\\s|$)');

  if (rmRoot.test(cmd) || rmLiteralRoot.test(cmd)) {
    process.stderr.write([
      '[bash-safety] BLOCKED — recursive delete targets project root or above.',
      '',
      'Command: ' + cmd,
      '',
      'If this is intentional, run it manually in your own terminal:',
      '    ' + cmd,
      '',
      'Then reply "done" and I will continue.'
    ].join('\n') + '\n');
    process.exit(2);
  }

  // === 2. System-path deletion ===
  const systemPaths = /\brm\s+(-[a-zA-Z]*[rRfF][a-zA-Z]*\s+)+(\/(etc|usr|bin|sbin|var|boot|lib|opt|root|sys|proc|dev|System)(\/|\s|$)|C:\\\\|C:\/)/i;
  if (systemPaths.test(cmd)) {
    process.stderr.write([
      '[bash-safety] BLOCKED — recursive delete targets system path.',
      '',
      'Command: ' + cmd,
      '',
      'System-level deletes are NEVER safe to run inside a Claude session.',
      'If this is genuinely required, run it manually with appropriate care:',
      '    ' + cmd,
      '',
      'Then reply "done" and I will continue.'
    ].join('\n') + '\n');
    process.exit(2);
  }

  // === 3. Privileged commands → delegate to user ===
  const sudoRe = /^(\s*sudo\s|\s*su\s|\s*doas\s|\s*runas\s|.*Start-Process.*-Verb\s+RunAs)/i;
  if (sudoRe.test(cmd)) {
    process.stderr.write([
      '[bash-safety] DELEGATING — privileged command needs your authorization.',
      '',
      'Command: ' + cmd,
      '',
      'This needs to run in your own terminal where you can authenticate as admin/sudo/root.',
      'Please run this exact command in a separate terminal:',
      '    ' + cmd,
      '',
      'Once the command completes, reply "done" (and paste any relevant output) and I will continue.'
    ].join('\n') + '\n');
    process.exit(2);
  }

  process.exit(0);
})();
