#!/usr/bin/env node
// .claude/hooks/pre-tool-public-repo-guard.cjs
//
// PreToolUse hook (matcher: Bash). Implements LAW — .CLAUDE WORKFLOW IP BOUNDARY
// (see .claude/CONSTRAINTS.md). Blocks any `git add` / `git commit` / `git push`
// that would carry `.claude/` content to a remote that fails the two-path
// pass-criteria — PRIMARY (Forgejo allowlist) and FALLBACK (PRIVATE under
// `Unity-Lab-AI` org via `gh repo view`).
//
// Mechanism (Layer 1 + Layer 2 of the four-layer defense-in-depth):
//   1. Parse the intercepted Bash command for git add/commit/push patterns
//   2. Determine whether `.claude/` paths are involved
//        - git add  → explicit args + broad-add workspace scan
//        - git commit → check `git diff --cached --name-only`
//        - git push → check commits ahead of upstream / branch
//   3. Read every remote from `git remote -v` (multi-remote paranoia)
//   4. For each remote: PRIMARY check first — parseHost(url) against
//        TRUSTED_PRIVATE_HOSTS allowlist (Forgejo `git.unityailab.com`).
//        Allowlist match = synthetic-PASS, no API call, sub-ms latency.
//      Otherwise FALLBACK check — `gh repo view --json visibility,owner`
//        (cached 60s) requiring visibility=PRIVATE AND owner=Unity-Lab-AI.
//   5. ALLOW only when EVERY remote satisfies PRIMARY or FALLBACK
//   6. Otherwise exit 2 (block) with a detailed paste-ready remediation message
//
// Force-flag agnostic — `git push --force` does NOT bypass.
//
// Bash fallback sibling: pre-tool-public-repo-guard.sh

'use strict';

const fs = require('fs');
const path = require('path');
const os = require('os');
const { execFileSync, execSync } = require('child_process');

const CACHE_PATH = path.join(os.homedir(), '.claude', 'repo-visibility-cache.json');
const CACHE_TTL_MS = 60 * 1000;
const ALLOWED_OWNER = 'Unity-Lab-AI';
const ALLOWED_VISIBILITY = 'PRIVATE';

// Trusted self-hosted private Git hosts operated by the Unity AI Lab group.
// `gh repo view` can't verify visibility on non-github.com hosts, so we maintain
// an explicit allowlist here for instances we know are private + lab-controlled.
// Adding to this list requires the same caution as removing the `.claude/`
// gitignore block — only Unity-AI-Lab-controlled hosts that the team has audited
// as PRIVATE belong here.
const TRUSTED_PRIVATE_HOSTS = new Set([
  'git.unityailab.com',  // Forgejo instance owned + operated by Unity AI Lab
]);

function parseHost(url) {
  // Handles git@host:owner/repo.git, https://host/owner/repo, ssh://git@host/owner/repo
  const sshMatch = url.match(/^[^@\s]+@([^:/\s]+)[:/]/);
  if (sshMatch) return sshMatch[1].toLowerCase();
  const httpMatch = url.match(/^(?:https?|ssh|git):\/\/(?:[^@\s/]+@)?([^/:\s]+)/);
  if (httpMatch) return httpMatch[1].toLowerCase();
  return null;
}

function readStdin() {
  try { return fs.readFileSync(0, 'utf8'); } catch (e) { return ''; }
}

function runCmd(cmd, args, opts) {
  try {
    return execFileSync(cmd, args, Object.assign({
      encoding: 'utf8',
      stdio: ['pipe', 'pipe', 'pipe'],
      timeout: 5000,
    }, opts || {})).trim();
  } catch (e) {
    return null;
  }
}

function inGitRepo() {
  try { execSync('git rev-parse --git-dir', { stdio: 'pipe', timeout: 2000 }); return true; }
  catch (e) { return false; }
}

function detectGitOp(cmd) {
  if (/\bgit\s+add(\b|$)/.test(cmd)) return 'add';
  if (/\bgit\s+commit(\b|$)/.test(cmd)) return 'commit';
  if (/\bgit\s+push(\b|$)/.test(cmd)) return 'push';
  return null;
}

function pathsTouchClaude(lines) {
  if (!lines) return false;
  return lines.split('\n').some(function (raw) {
    if (!raw) return false;
    // Strip porcelain prefix `XY ` or git-log empty prefix; trim quotes
    let p = raw.replace(/^[ MADRCU?!]{1,2}\s+/, '').replace(/^"(.+)"$/, '$1').trim();
    return /^\.claude\//.test(p) || p === '.claude' || /^\.claude\//.test(p.replace(/^.*?->\s*/, ''));
  });
}

function gitAddInvolvesClaude(cmd) {
  const argsStr = cmd.replace(/^.*?\bgit\s+add\s*/, '');
  if (/(?:^|[\s/'"=])\.claude(?:\/|\b)/.test(argsStr)) return true;
  const args = argsStr.split(/\s+/).filter(Boolean);
  const broad = new Set(['.', '-A', '--all', '-u', '--update', '-a', '*', './*', '--', ':/']);
  const isBroad = args.some(function (a) { return broad.has(a); });
  const isInteractive = args.some(function (a) { return a === '-p' || a === '--patch' || a === '-i' || a === '--interactive'; });
  if (!isBroad && !isInteractive) return false;
  const status = runCmd('git', ['status', '--porcelain', '--untracked-files=all']);
  return pathsTouchClaude(status);
}

function gitCommitInvolvesClaude() {
  const staged = runCmd('git', ['diff', '--cached', '--name-only']);
  return pathsTouchClaude(staged);
}

function gitPushInvolvesClaude() {
  const upstream = runCmd('git', ['rev-parse', '--abbrev-ref', '--symbolic-full-name', '@{u}']);
  if (upstream) {
    const changed = runCmd('git', ['diff', upstream + '..HEAD', '--name-only']);
    if (changed !== null) return pathsTouchClaude(changed);
  }
  // No upstream — check all commits in current branch (paranoid)
  const allChanged = runCmd('git', ['log', 'HEAD', '--name-only', '--pretty=format:']);
  return pathsTouchClaude(allChanged);
}

function listRemotes() {
  const raw = runCmd('git', ['remote', '-v']);
  if (!raw) return [];
  const seen = new Map();
  raw.split('\n').forEach(function (line) {
    const m = line.match(/^(\S+)\s+(\S+)\s+\((?:fetch|push)\)$/);
    if (!m) return;
    if (!seen.has(m[1])) seen.set(m[1], m[2]);
  });
  return Array.from(seen, function (entry) { return { name: entry[0], url: entry[1] }; });
}

function parseGithubOwnerRepo(url) {
  // Match: https://github.com/owner/repo(.git)? OR git@github.com:owner/repo(.git)?
  const m = url.match(/(?:github\.com[:\/])([^/]+)\/([^/\s]+?)(?:\.git)?\/?$/);
  if (!m) return null;
  return { owner: m[1], repo: m[2] };
}

function loadCache() {
  try { return JSON.parse(fs.readFileSync(CACHE_PATH, 'utf8')); }
  catch (e) { return {}; }
}

function saveCache(cache) {
  try {
    fs.mkdirSync(path.dirname(CACHE_PATH), { recursive: true });
    const tmp = CACHE_PATH + '.tmp.' + process.pid;
    fs.writeFileSync(tmp, JSON.stringify(cache, null, 2));
    fs.renameSync(tmp, CACHE_PATH);
  } catch (e) { /* non-fatal — cache is best-effort */ }
}

function checkVisibility(url, cache, now) {
  const cached = cache[url];
  if (cached && (now - cached.checked_at) < CACHE_TTL_MS) return cached;

  // Trusted self-hosted private hosts get a synthetic PASS — they're audited as
  // private + Unity-AI-Lab-controlled at the host level, so gh-based visibility
  // checks don't apply. See TRUSTED_PRIVATE_HOSTS allowlist at top of file.
  const host = parseHost(url);
  if (host && TRUSTED_PRIVATE_HOSTS.has(host)) {
    return {
      visibility: ALLOWED_VISIBILITY,
      owner: ALLOWED_OWNER,
      error: null,
      trusted_host: host,
      checked_at: now,
    };
  }

  const parsed = parseGithubOwnerRepo(url);
  if (!parsed) {
    return { visibility: 'UNKNOWN', owner: null, error: 'non-github-url', checked_at: now };
  }

  if (runCmd('gh', ['--version']) === null) {
    return { visibility: 'UNKNOWN', owner: null, error: 'gh-not-installed', checked_at: now };
  }

  const result = runCmd('gh', ['repo', 'view', parsed.owner + '/' + parsed.repo, '--json', 'visibility,owner']);
  if (result === null) {
    if (runCmd('gh', ['auth', 'status']) === null) {
      return { visibility: 'UNKNOWN', owner: null, error: 'gh-not-authed', checked_at: now };
    }
    return { visibility: 'UNKNOWN', owner: null, error: 'api-error', checked_at: now };
  }

  let parsedJson;
  try { parsedJson = JSON.parse(result); }
  catch (e) {
    return { visibility: 'UNKNOWN', owner: null, error: 'parse-error', checked_at: now };
  }

  const entry = {
    visibility: parsedJson.visibility || 'UNKNOWN',
    owner: (parsedJson.owner && parsedJson.owner.login) || null,
    error: null,
    checked_at: now,
  };
  cache[url] = entry;
  saveCache(cache);
  return entry;
}

function reasonFor(entry) {
  if (entry.error === 'gh-not-installed') return 'gh CLI is not installed — install from https://cli.github.com/';
  if (entry.error === 'gh-not-authed') return 'gh is not authenticated — run `gh auth login`';
  if (entry.error === 'non-github-url') return 'remote URL is not on github.com — gh cannot verify visibility';
  if (entry.error === 'api-error') return 'gh API call failed (network error, repo inaccessible, or rate-limit)';
  if (entry.error === 'parse-error') return 'gh response could not be parsed as JSON';
  if (entry.visibility !== ALLOWED_VISIBILITY) return 'visibility=' + entry.visibility + ' (must be ' + ALLOWED_VISIBILITY + ')';
  if (entry.owner !== ALLOWED_OWNER) return 'owner=' + (entry.owner || 'unknown') + ' (must be ' + ALLOWED_OWNER + ')';
  return 'unknown failure';
}

(function main() {
  const raw = readStdin();
  if (!raw) process.exit(0);

  let payload;
  try { payload = JSON.parse(raw); } catch (e) { process.exit(0); }

  const cmd = (payload.toolInput && payload.toolInput.command)
           || (payload.tool_input && payload.tool_input.command)
           || (payload.input && payload.input.command)
           || '';
  if (!cmd) process.exit(0);

  const op = detectGitOp(cmd);
  if (!op) process.exit(0);

  if (!inGitRepo()) process.exit(0);

  let involves;
  if (op === 'add') involves = gitAddInvolvesClaude(cmd);
  else if (op === 'commit') involves = gitCommitInvolvesClaude();
  else involves = gitPushInvolvesClaude();
  if (!involves) process.exit(0);

  const remotes = listRemotes();
  if (remotes.length === 0) process.exit(0);

  const cache = loadCache();
  const now = Date.now();
  const offending = [];
  for (let i = 0; i < remotes.length; i++) {
    const r = remotes[i];
    const v = checkVisibility(r.url, cache, now);
    const allowed = (v.visibility === ALLOWED_VISIBILITY) && (v.owner === ALLOWED_OWNER);
    if (!allowed) offending.push(Object.assign({}, r, v));
  }

  if (offending.length === 0) process.exit(0);

  const lines = [
    '[CLAUDE-IP-GUARD] BLOCKED — `.claude/` cannot land on a public/non-Unity-Lab-AI repo.',
    '',
    'Command: ' + cmd,
    '',
    'Offending remote(s):',
  ];
  for (let i = 0; i < offending.length; i++) {
    const o = offending[i];
    lines.push('  - ' + o.name + ' → ' + o.url);
    lines.push('    visibility: ' + o.visibility + (o.owner ? ', owner: ' + o.owner : ''));
    lines.push('    reason: ' + reasonFor(o));
  }
  lines.push('');
  lines.push('Options to proceed:');
  lines.push('  1. Remove the offending remote: git remote remove <remote-name>');
  lines.push('  2. Move .claude/ work to a different feature branch in a different repo');
  lines.push('  3. If this remote is genuinely meant to receive .claude/ AND is private + ' + ALLOWED_OWNER + ':');
  lines.push('     run `gh repo view <owner>/<repo> --json visibility,owner` to verify');
  lines.push('     then re-run the original command — visibility cached for 60s');
  lines.push('');
  lines.push('See .claude/CONSTRAINTS.md §LAW — .CLAUDE WORKFLOW IP BOUNDARY for full text.');

  process.stderr.write(lines.join('\n') + '\n');
  process.exit(2);
})();
