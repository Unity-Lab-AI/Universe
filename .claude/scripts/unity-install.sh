#!/usr/bin/env bash
# .claude/scripts/unity-install.sh
#
# Install (or refresh) the Unity AI Lab .claude/ template into a target
# directory. Idempotent: works whether or not target already has a .claude/.
#
# Flow:
#   1. Clone the requested branch (default: main) of UAL-ClaudeWorkflow into a
#      temp dir.
#   2. If target/.claude/ exists, stage the personal/project-local files
#      (preserve list) into a second temp dir.
#   3. Replace target/.claude/ entirely with the cloned .claude/.
#   4. Restore staged personal files (no-clobber — fresh framework wins ties,
#      personal files come back where the framework didn't write).
#   5. Maintain Layer 0 .claude/ exclude block in target's .gitignore (the
#      ONLY project-root file we ever touch). Idempotent. Never copy our
#      source-repo .gitignore wholesale.
#   6. Seed user-profile memory folder ONLY if missing or empty.
#   7. Clean up both temp dirs.
#
# Usage:
#   ./unity-install.sh                        (branch=main, target=cwd)
#   ./unity-install.sh develop                (branch=develop, target=cwd)
#   ./unity-install.sh feature/setup-install  (branch=feature/setup-install, target=cwd)
#   ./unity-install.sh main /path/to/target   (branch=main, target=/path/to/target)
#   ./unity-install.sh develop /path/to/foo   (branch=develop, target=/path/to/foo)
#
# Exit codes:
#   0 — success
#   1 — failure (prerequisite, clone, or copy step failed; stderr has detail)
#
# .ps1 sibling: .claude/scripts/unity-install.ps1
# /unity-update is a thin wrapper calling this script — same flow.

set -e

# ─────────────────────────────────────────────────────────────────────
# Configuration
# ─────────────────────────────────────────────────────────────────────
# Canonical upstream — Unity AI Lab's self-hosted Forgejo. SSH-key auth via
# the operator's registered ed25519 key (see docs/ADMIN-ONBOARDING.md §2).
# Override via UNITY_REPO_URL env var if mirroring/testing against another host.
REPO_URL="${UNITY_REPO_URL:-git@git.unityailab.com:UnityAILab/UAL-ClaudeWorkflow.git}"
DEFAULT_BRANCH="main"

# Personal/project-local files preserved across install/update (relative to .claude/).
# These are gitignored at source, so the cloned framework copy never has them —
# the no-clobber restore therefore always succeeds without conflict.
PRESERVE_FILES="settings.local.json .env user.json project-config.json .session-state.md .session-tidbits.md .session-usage.jsonl .last-session.md .session-env.json .yolo-mode .persona-state .setup-complete .usage-tracking-disabled"
PRESERVE_DIRS="user-context"

# ─────────────────────────────────────────────────────────────────────
# Argument parsing — strict positional: [branch] [target-dir]
# Heuristic: first arg is a target if it starts with /, ./, ../, ~, or
# matches a Windows-absolute pattern; otherwise it's a branch.
# ─────────────────────────────────────────────────────────────────────
BRANCH="$DEFAULT_BRANCH"
TARGET_DIR=""

is_path_like() {
  case "$1" in
    /*|./*|../*|~/*|~) return 0 ;;
    [A-Za-z]:/*|[A-Za-z]:\\*) return 0 ;;
    *) return 1 ;;
  esac
}

if [ $# -ge 1 ]; then
  if is_path_like "$1"; then
    TARGET_DIR="$1"
  else
    BRANCH="$1"
    [ $# -ge 2 ] && TARGET_DIR="$2"
  fi
fi
[ -z "$TARGET_DIR" ] && TARGET_DIR="$(pwd)"

# Resolve target to absolute path
if [ ! -d "$TARGET_DIR" ]; then
  mkdir -p "$TARGET_DIR" 2>/dev/null || {
    echo "[unity-install] ERROR: cannot create target directory: $TARGET_DIR" >&2
    exit 1
  }
fi
TARGET_DIR=$(cd "$TARGET_DIR" && pwd)

# ─────────────────────────────────────────────────────────────────────
# Prerequisites
# ─────────────────────────────────────────────────────────────────────
if ! command -v git >/dev/null 2>&1; then
  echo "[unity-install] ERROR: git not found on PATH" >&2
  exit 1
fi

# ─────────────────────────────────────────────────────────────────────
# Temp dirs + cleanup trap
# ─────────────────────────────────────────────────────────────────────
CLONE_DIR=$(mktemp -d -t unity-install-clone-XXXXXXXX)
STAGE_DIR=$(mktemp -d -t unity-install-stage-XXXXXXXX)
trap 'rm -rf "$CLONE_DIR" "$STAGE_DIR"' EXIT

# ─────────────────────────────────────────────────────────────────────
# Clone (shallow, requested branch)
# ─────────────────────────────────────────────────────────────────────
echo "[unity-install] Target:       $TARGET_DIR"
echo "[unity-install] Branch:       $BRANCH"
echo "[unity-install] Cloning $REPO_URL ($BRANCH) ..."
if ! git clone --depth 1 --branch "$BRANCH" "$REPO_URL" "$CLONE_DIR/repo" 2>&1; then
  echo "[unity-install] ERROR: git clone failed for branch '$BRANCH'" >&2
  exit 1
fi

REPO_CLAUDE="$CLONE_DIR/repo/.claude"
if [ ! -d "$REPO_CLAUDE" ]; then
  echo "[unity-install] ERROR: cloned repo has no .claude/ — upstream layout may have changed" >&2
  exit 1
fi

# ─────────────────────────────────────────────────────────────────────
# Stage personal files if target/.claude/ already exists
# ─────────────────────────────────────────────────────────────────────
HAD_EXISTING=0
if [ -d "$TARGET_DIR/.claude" ]; then
  HAD_EXISTING=1
  echo "[unity-install] Existing .claude/ detected — staging personal files for restore..."
  for f in $PRESERVE_FILES; do
    if [ -e "$TARGET_DIR/.claude/$f" ]; then
      cp -p "$TARGET_DIR/.claude/$f" "$STAGE_DIR/$f"
      echo "[unity-install]   staged: $f"
    fi
  done
  for d in $PRESERVE_DIRS; do
    if [ -d "$TARGET_DIR/.claude/$d" ]; then
      cp -rp "$TARGET_DIR/.claude/$d" "$STAGE_DIR/$d"
      echo "[unity-install]   staged: $d/"
    fi
  done

  # Replace target/.claude/ entirely
  rm -rf "$TARGET_DIR/.claude"
fi

# ─────────────────────────────────────────────────────────────────────
# Drop fresh .claude/ from clone
# ─────────────────────────────────────────────────────────────────────
mkdir -p "$TARGET_DIR/.claude"
cp -r "$REPO_CLAUDE/." "$TARGET_DIR/.claude/"
echo "[unity-install] Installed fresh .claude/ from $BRANCH"

# ─────────────────────────────────────────────────────────────────────
# Restore staged personal files (no-clobber: framework wins ties)
# ─────────────────────────────────────────────────────────────────────
if [ "$HAD_EXISTING" = "1" ]; then
  for f in $PRESERVE_FILES; do
    if [ -e "$STAGE_DIR/$f" ] && [ ! -e "$TARGET_DIR/.claude/$f" ]; then
      cp -p "$STAGE_DIR/$f" "$TARGET_DIR/.claude/$f"
      echo "[unity-install]   restored: $f"
    fi
  done
  for d in $PRESERVE_DIRS; do
    if [ -d "$STAGE_DIR/$d" ] && [ ! -e "$TARGET_DIR/.claude/$d" ]; then
      cp -rp "$STAGE_DIR/$d" "$TARGET_DIR/.claude/$d"
      echo "[unity-install]   restored: $d/"
    fi
  done
fi

# ─────────────────────────────────────────────────────────────────────
# Re-apply executable bits on scripts/hooks/launchers/binaries
# ─────────────────────────────────────────────────────────────────────
chmod +x "$TARGET_DIR/.claude/hooks/"*.cjs "$TARGET_DIR/.claude/hooks/"*.sh 2>/dev/null || true
chmod +x "$TARGET_DIR/.claude/scripts/"*.sh 2>/dev/null || true
chmod +x "$TARGET_DIR/.claude/start.sh" 2>/dev/null || true
chmod +x "$TARGET_DIR/.claude/bin/"* 2>/dev/null || true

# ─────────────────────────────────────────────────────────────────────
# Layer 0 .gitignore — the ONLY project-root file we touch.
# Idempotent: appends the .claude/ exclude block only if missing.
# Never copies our source-repo .gitignore.
# ─────────────────────────────────────────────────────────────────────
GITIGNORE="$TARGET_DIR/.gitignore"
[ ! -f "$GITIGNORE" ] && touch "$GITIGNORE"
if grep -Eq '^\.claude/?[[:space:]]*$' "$GITIGNORE"; then
  echo "[unity-install] .gitignore already excludes .claude/ — Layer 0 already in place"
else
  {
    echo ""
    echo "# Unity AI Lab .claude/ workflow — proprietary, never commit to public repos."
    echo "# See .claude/CONSTRAINTS.md §LAW — .CLAUDE WORKFLOW IP BOUNDARY for full text."
    echo "# Removal sanctioned only via /claude-publish after gh confirms private + Unity-Lab-AI."
    echo ".claude/"
  } >> "$GITIGNORE"
  echo "[unity-install] Added .claude/ exclude to $GITIGNORE (Layer 0 IP-boundary defense-in-depth)"
fi

# ─────────────────────────────────────────────────────────────────────
# User-profile memory folder — install-only-if-missing.
# Project-level workflow only TOUCHES global for the workflow-setup memory,
# and never overwrites once seeded. User deletes MEMORY.md to trigger reseed
# on next start.sh / start.bat run.
# ─────────────────────────────────────────────────────────────────────
if [ -d "$TARGET_DIR/.claude/memory-templates" ]; then
  ENCODED_PATH=$(echo "$TARGET_DIR" \
    | sed 's|:|-|g' \
    | sed 's|/|-|g' \
    | sed 's|\\|-|g' \
    | sed 's|\.|-|g' \
    | sed 's| |-|g' \
    | sed 's|(|-|g' \
    | sed 's|)|-|g')
  MEMORY_DIR="$HOME/.claude/projects/$ENCODED_PATH/memory"
  if [ ! -d "$MEMORY_DIR" ] || [ -z "$(ls -A "$MEMORY_DIR" 2>/dev/null)" ]; then
    mkdir -p "$MEMORY_DIR"
    cp "$TARGET_DIR/.claude/memory-templates/"*.md "$MEMORY_DIR/" 2>/dev/null && \
      echo "[unity-install] Seeded user-profile memory folder at $MEMORY_DIR"
  else
    echo "[unity-install] User-profile memory folder already populated — leaving as-is"
    echo "                ($MEMORY_DIR — delete its MEMORY.md to trigger reseed on next start)"
  fi
fi

# ─────────────────────────────────────────────────────────────────────
# Done
# ─────────────────────────────────────────────────────────────────────
echo ""
if [ "$HAD_EXISTING" = "1" ]; then
  echo "[unity-install] Update complete."
else
  echo "[unity-install] Install complete."
fi
echo ""
echo "  Target:       $TARGET_DIR"
echo "  Source:       $REPO_URL ($BRANCH)"
if [ "$HAD_EXISTING" = "1" ]; then
  echo "  Preserved:    settings.local.json, .env, user.json, user-context/, project-config.json, machine-local state files"
fi
echo ""
echo "  Next steps:"
echo "    cd $TARGET_DIR"
if [ -f "$TARGET_DIR/.claude/start.sh" ]; then
  echo "    ./.claude/start.sh           # Linux/macOS launcher (memory + Unity + /workflow)"
fi
if [ -f "$TARGET_DIR/.claude/start.bat" ]; then
  echo "    .\\.claude\\start.bat          # Windows launcher (same flow)"
fi
echo ""

exit 0
