#!/usr/bin/env bash
# .claude/hooks/session-start-env-dump.sh
#
# Bash fallback for session-start-env-dump.cjs.
# Use this in settings.json if a team member's machine doesn't have
# node available. Functional parity is best-effort — the .cjs version is
# preferred when both are available.
#
# Outputs project state JSON on stdout for Claude Code context injection.
# Pure enablement. Exit 0 always.

set -e

ROOT="${CLAUDE_PROJECT_DIR:-$(pwd)}"
STAMP=$(date -u +"%Y-%m-%dT%H:%M:%S.000Z" 2>/dev/null || date -u +"%Y-%m-%dT%H:%M:%SZ")

# === OS ===
OS_PLATFORM="unknown"
OS_DISTRO=""
OS_VERSION=""
OS_BUILD=""
case "$(uname -s 2>/dev/null)" in
  Linux)
    OS_PLATFORM="linux"
    if [ -f /etc/os-release ]; then
      OS_DISTRO=$(grep '^PRETTY_NAME=' /etc/os-release | cut -d= -f2- | tr -d '"')
      OS_VERSION=$(grep '^VERSION_ID=' /etc/os-release | cut -d= -f2- | tr -d '"')
    fi
    OS_BUILD=$(uname -r 2>/dev/null || echo "")
    ;;
  Darwin)
    OS_PLATFORM="macos"
    OS_DISTRO=$(sw_vers -productName 2>/dev/null || echo "macOS")
    OS_VERSION=$(sw_vers -productVersion 2>/dev/null || echo "")
    OS_BUILD=$(sw_vers -buildVersion 2>/dev/null || echo "")
    ;;
  MINGW*|MSYS*|CYGWIN*)
    OS_PLATFORM="windows"
    OS_DISTRO="$(uname -s)"
    OS_VERSION="$(uname -r 2>/dev/null || echo '')"
    ;;
esac

# === Shell context ===
if [ -n "$MSYSTEM" ]; then
  SHELL_CONTEXT="git-bash"
elif [ -n "$BASH_VERSION" ] || [ -n "$ZSH_VERSION" ]; then
  SHELL_CONTEXT="bash"
elif [ -n "$PSModulePath" ]; then
  SHELL_CONTEXT="powershell"
else
  SHELL_CONTEXT="cmd-or-unknown"
fi

# === Git ===
GIT_INSTALLED="false"
GIT_IS_REPO="false"
GIT_BRANCH=""
GIT_REMOTE="false"
if command -v git >/dev/null 2>&1; then
  GIT_INSTALLED="true"
  if git -C "$ROOT" rev-parse --is-inside-work-tree >/dev/null 2>&1; then
    GIT_IS_REPO="true"
    GIT_BRANCH=$(git -C "$ROOT" rev-parse --abbrev-ref HEAD 2>/dev/null)
    if [ -n "$(git -C "$ROOT" remote -v 2>/dev/null)" ]; then
      GIT_REMOTE="true"
    fi
  fi
fi

# === Opt-in marker ===
OPT_STATE="UNSET"
MARKER_PRESENT="false"
MARKER="$ROOT/.claude/project-config.json"
if [ -f "$MARKER" ]; then
  MARKER_PRESENT="true"
  if grep -q '"enabled"\s*:\s*true' "$MARKER"; then
    OPT_STATE="ENABLED"
  elif grep -q '"enabled"\s*:\s*false' "$MARKER"; then
    OPT_STATE="DISABLED"
  fi
fi

# === TODO summary ===
TODO_PRESENT="false"
TODO_INPROG=0
TODO_PEND=0
if [ -f "$ROOT/docs/TODO.md" ]; then
  TODO_PRESENT="true"
  TODO_INPROG=$(grep -c '^###*\s*\[~\]' "$ROOT/docs/TODO.md" 2>/dev/null || echo 0)
  TODO_PEND=$(grep -c '^###*\s*\[ \]' "$ROOT/docs/TODO.md" 2>/dev/null || echo 0)
fi

# === Persona ===
PERSONA="unity (default)"
if [ -f "$ROOT/.claude/.persona-state" ]; then
  PERSONA=$(cat "$ROOT/.claude/.persona-state" 2>/dev/null || echo "unity (default)")
fi

# === YOLO mode ===
YOLO_ENABLED="false"
YOLO_ACTIVATED_AT=""
YOLO_ACTIVE_PERSONA=""
if [ -f "$ROOT/.claude/.yolo-mode" ]; then
  YOLO_ENABLED="true"
  YOLO_ACTIVATED_AT=$(grep -E '^activated_at=' "$ROOT/.claude/.yolo-mode" 2>/dev/null | head -1 | cut -d= -f2- | tr -d ' ')
  YOLO_ACTIVE_PERSONA=$(grep -E '^active_persona=' "$ROOT/.claude/.yolo-mode" 2>/dev/null | head -1 | cut -d= -f2- | tr -d ' ')
fi

# === Emit JSON to stdout ===
cat <<EOF
## Session-start environment context (auto-injected by .claude/hooks/session-start-env-dump.sh)

\`\`\`json
{
  "timestamp": "$STAMP",
  "project_root": "$ROOT",
  "os": {
    "platform": "$OS_PLATFORM",
    "distro_or_edition": "$OS_DISTRO",
    "version": "$OS_VERSION",
    "build_or_kernel": "$OS_BUILD"
  },
  "shell": "$SHELL_CONTEXT",
  "git": {
    "installed": $GIT_INSTALLED,
    "is_repo": $GIT_IS_REPO,
    "current_branch": "$GIT_BRANCH",
    "remote_configured": $GIT_REMOTE
  },
  "git_flow_opt_in": {
    "state": "$OPT_STATE",
    "marker_file_present": $MARKER_PRESENT
  },
  "todo": {
    "file_present": $TODO_PRESENT,
    "in_progress_count": $TODO_INPROG,
    "pending_count": $TODO_PEND
  },
  "persona": {
    "active_persona": "$PERSONA"
  },
  "yolo_mode": {
    "enabled": $YOLO_ENABLED,
    "activated_at": "$YOLO_ACTIVATED_AT",
    "active_persona": "$YOLO_ACTIVE_PERSONA"
  }
}
\`\`\`

*Reminder: Unity is the session default per \`.claude/CLAUDE.md\`. Memory layer in \`~/.claude/projects/<encoded>/memory/\` auto-loads at session start.*
EOF

# Branch awareness
if [ "$GIT_IS_REPO" = "true" ] && [ "$OPT_STATE" = "ENABLED" ]; then
  case "$GIT_BRANCH" in
    main|master|develop|prod|production)
      echo ""
      echo "⚠ **On protected branch \`$GIT_BRANCH\`** — Git Flow opt-in is ENABLED. Branch into \`feature/<descriptor>\` before any edits per CONSTRAINTS.md §GIT FLOW."
      ;;
  esac
fi

# YOLO mode notice
if [ "$YOLO_ENABLED" = "true" ]; then
  echo ""
  echo "⚡ **YOLO mode is ENABLED** — lead-dev autonomy posture per \`.claude/commands/yolo.md\`. Unity acts then verifies; user test plan required on every task closure. \`/sober\` to deactivate."
fi

exit 0
