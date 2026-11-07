#!/usr/bin/env node
// .claude/hooks/session-start-env-dump.cjs
//
// SessionStart hook (matchers: startup, resume, clear, compact).
// Outputs project state as a JSON envelope on stdout — Claude Code injects
// this into the model's opening context. Standardizes session-boot context
// across every team member's machine (Linux / Git Bash / native PowerShell).
//
// Also writes .claude/.session-env.json for downstream hooks to read without
// re-detecting. Pure enablement — does not block anything. Exit 0 always.
//
// Bash fallback sibling: session-start-env-dump.sh

'use strict';

const fs = require('fs');
const path = require('path');
const { execSync } = require('child_process');

function safe(cmd, opts) {
  try { return execSync(cmd, Object.assign({ encoding: 'utf8', stdio: ['ignore', 'pipe', 'ignore'] }, opts || {})).trim(); }
  catch (e) { return null; }
}

function encodeProjectPathForMemory(p) {
  return p.replace(/:/g, '-').replace(/\//g, '-').replace(/\\/g, '-')
          .replace(/\./g, '-').replace(/ /g, '-').replace(/\(/g, '-').replace(/\)/g, '-');
}

function detectOS() {
  if (process.platform === 'linux') {
    const rel = safe('cat /etc/os-release') || '';
    const pretty = (rel.match(/^PRETTY_NAME="?([^"\n]+)"?/m) || [, 'linux'])[1];
    const ver = (rel.match(/^VERSION_ID="?([^"\n]+)"?/m) || [, ''])[1];
    return { platform: 'linux', distro_or_edition: pretty, version: ver, build_or_kernel: safe('uname -r') || '' };
  }
  if (process.platform === 'win32') {
    const out = safe('powershell -NoProfile -Command "Get-ComputerInfo | Select-Object WindowsProductName, WindowsVersion, WindowsEditionId, OsBuildNumber | ConvertTo-Json -Compress"');
    if (out) {
      try {
        const i = JSON.parse(out);
        return { platform: 'windows', distro_or_edition: (i.WindowsProductName || '') + ' ' + (i.WindowsEditionId || ''), version: i.WindowsVersion || '', build_or_kernel: String(i.OsBuildNumber || '') };
      } catch (e) {}
    }
    return { platform: 'windows', distro_or_edition: 'unknown', version: '', build_or_kernel: '' };
  }
  if (process.platform === 'darwin') {
    const v = safe('sw_vers') || '';
    return {
      platform: 'macos',
      distro_or_edition: (v.match(/ProductName:\s*(.+)/) || [, 'macOS'])[1].trim(),
      version: (v.match(/ProductVersion:\s*(.+)/) || [, ''])[1].trim(),
      build_or_kernel: (v.match(/BuildVersion:\s*(.+)/) || [, ''])[1].trim()
    };
  }
  return { platform: process.platform, distro_or_edition: 'unknown', version: '', build_or_kernel: '' };
}

function detectShell() {
  if (process.env.MSYSTEM) return 'git-bash';
  if (process.env.SHELL && process.env.SHELL.indexOf('bash') !== -1) return 'bash';
  if (process.env.PSModulePath && !process.env.MSYSTEM) return 'powershell';
  return 'cmd-or-unknown';
}

function detectGit(root) {
  const installed = !!safe('git --version');
  if (!installed) return { installed: false };
  const isRepo = safe('git rev-parse --is-inside-work-tree', { cwd: root }) === 'true';
  if (!isRepo) return { installed: true, is_repo: false };
  const protectedLocal = (safe('git branch --list main master develop', { cwd: root }) || '')
    .split('\n').map(l => l.trim().replace(/^\*\s*/, '')).filter(Boolean);
  return {
    installed: true,
    is_repo: true,
    current_branch: safe('git rev-parse --abbrev-ref HEAD', { cwd: root }) || '',
    remote_configured: !!safe('git remote -v', { cwd: root }),
    protected_branches_local: protectedLocal
  };
}

function readOptIn(root) {
  const p = path.join(root, '.claude', 'project-config.json');
  if (!fs.existsSync(p)) return { state: 'UNSET', marker_file_present: false };
  try {
    const c = JSON.parse(fs.readFileSync(p, 'utf8'));
    if (c.git_flow && typeof c.git_flow.enabled === 'boolean') {
      return {
        state: c.git_flow.enabled ? 'ENABLED' : 'DISABLED',
        marker_file_present: true,
        confirmed_at: c.git_flow.confirmed_at || null,
        main_branch: c.git_flow.main_branch || 'main',
        develop_branch: c.git_flow.develop_branch || 'develop'
      };
    }
    return { state: 'UNSET', marker_file_present: true };
  } catch (e) { return { state: 'UNSET', marker_file_present: true, parse_error: true }; }
}

function readTodo(root) {
  const p = path.join(root, 'docs', 'TODO.md');
  if (!fs.existsSync(p)) return { file_present: false };
  const c = fs.readFileSync(p, 'utf8');
  const inProg = c.match(/^###?\s*\[~\]\s*(.+)$/gm) || [];
  const pend = c.match(/^###?\s*\[ \]\s*(.+)$/gm) || [];
  return {
    file_present: true,
    in_progress_count: inProg.length,
    pending_count: pend.length,
    in_progress_titles: inProg.map(l => l.replace(/^###?\s*\[~\]\s*/, '').trim()).slice(0, 5)
  };
}

function readFinalized(root) {
  const p = path.join(root, 'docs', 'FINALIZED.md');
  if (!fs.existsSync(p)) return { file_present: false };
  const c = fs.readFileSync(p, 'utf8');
  const sessions = c.match(/^##\s*Session\s+([0-9-]+)/gm) || [];
  return {
    file_present: true,
    last_session: sessions.length ? sessions[sessions.length - 1].replace(/^##\s*Session\s+/, '').trim() : null,
    completed_count: (c.match(/^###?\s*\[x\]/gm) || []).length
  };
}

function readMemory(root) {
  const tdir = path.join(root, '.claude', 'memory-templates');
  if (!fs.existsSync(tdir)) return { templates_present: false };
  const home = process.env.HOME || process.env.USERPROFILE || '';
  const adir = path.join(home, '.claude', 'projects', encodeProjectPathForMemory(path.resolve(root)), 'memory');
  const tfiles = fs.readdirSync(tdir).filter(f => f.endsWith('.md'));
  const apresent = fs.existsSync(adir);
  const afiles = apresent ? fs.readdirSync(adir).filter(f => f.endsWith('.md')) : [];
  const drift = tfiles.filter(f => {
    if (!afiles.indexOf(f) === false) return true;
    if (afiles.indexOf(f) === -1) return true;
    return fs.statSync(path.join(tdir, f)).mtimeMs > fs.statSync(path.join(adir, f)).mtimeMs;
  });
  return {
    templates_present: true,
    templates_count: tfiles.length,
    memory_synced_count: afiles.length,
    drift_count: drift.length,
    drift_files: drift.slice(0, 5),
    memory_dir: adir
  };
}

function readPersona(root) {
  const p = path.join(root, '.claude', '.persona-state');
  if (fs.existsSync(p)) return { active_persona: fs.readFileSync(p, 'utf8').trim() };
  return { active_persona: 'unity (default)' };
}

function readYoloMode(root) {
  const p = path.join(root, '.claude', '.yolo-mode');
  if (!fs.existsSync(p)) return { enabled: false };
  try {
    const content = fs.readFileSync(p, 'utf8');
    const out = { enabled: true };
    content.split(/\n/).forEach(line => {
      const m = line.match(/^([^=]+)=(.*)$/);
      if (m) out[m[1].trim()] = m[2].trim();
    });
    return out;
  } catch (e) { return { enabled: true, parse_error: true }; }
}

(function main() {
  const root = process.cwd();
  const env = {
    timestamp: new Date().toISOString(),
    project_root: root,
    os: detectOS(),
    shell: detectShell(),
    git: detectGit(root),
    git_flow_opt_in: readOptIn(root),
    todo: readTodo(root),
    finalized: readFinalized(root),
    memory: readMemory(root),
    persona: readPersona(root),
    yolo_mode: readYoloMode(root)
  };

  try { fs.writeFileSync(path.join(root, '.claude', '.session-env.json'), JSON.stringify(env, null, 2)); } catch (e) {}

  process.stdout.write('## Session-start environment context (auto-injected by .claude/hooks/session-start-env-dump.cjs)\n\n');
  process.stdout.write('```json\n' + JSON.stringify(env, null, 2) + '\n```\n');
  process.stdout.write('\n*Reminder: Unity is the session default per `.claude/CLAUDE.md`. Memory layer in `~/.claude/projects/<encoded>/memory/` auto-loads at session start.*\n');

  if (env.git.is_repo && env.git_flow_opt_in.state === 'ENABLED') {
    const protected_ = ['main', 'master', 'develop', 'prod', 'production'];
    if (protected_.indexOf(env.git.current_branch) !== -1) {
      process.stdout.write('\n⚠ **On protected branch `' + env.git.current_branch + '`** — Git Flow opt-in is ENABLED. Branch into `feature/<descriptor>` before any edits per CONSTRAINTS.md §GIT FLOW.\n');
    }
  }
  if (env.memory.templates_present && env.memory.drift_count > 0) {
    process.stdout.write('\n⚠ **Memory drift detected** — ' + env.memory.drift_count + ' feedback file(s) in `memory-templates/` are newer than the user-profile memory copies. The PostToolUse memory-sync hook will sync on next edit, or run `start.sh` to refresh manually.\n');
  }
  if (env.yolo_mode.enabled) {
    process.stdout.write('\n⚡ **YOLO mode is ENABLED** — lead-dev autonomy posture per `.claude/commands/yolo.md`. Unity acts then verifies; user test plan required on every task closure. `/sober` to deactivate.\n');
  }

  process.exit(0);
})();
