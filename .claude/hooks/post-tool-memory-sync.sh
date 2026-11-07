#!/usr/bin/env bash
# .claude/hooks/post-tool-memory-sync.sh
#
# Bash fallback for post-tool-memory-sync.cjs.
# When a feedback memory in .claude/memory-templates/ is edited, auto-cp
# it to the Claude Code project memory folder under the user's home
# directory (~/.claude/projects/<encoded>/memory/) so the new memory takes
# effect immediately. ~ = $HOME on Linux/macOS, %USERPROFILE% on Windows.
#
# Reads tool input JSON from stdin. Pure enablement. Exit 0 always.

ROOT="${CLAUDE_PROJECT_DIR:-$(pwd)}"
TEMPLATES="$ROOT/.claude/memory-templates"

INPUT="$(cat 2>/dev/null || echo '')"
[ -z "$INPUT" ] && exit 0

# Extract file_path from tool input JSON (best-effort grep — jq if available)
if command -v jq >/dev/null 2>&1; then
  FILE_PATH=$(echo "$INPUT" | jq -r '.toolInput.file_path // .tool_input.file_path // .input.file_path // empty' 2>/dev/null)
else
  FILE_PATH=$(echo "$INPUT" | grep -o '"file_path"\s*:\s*"[^"]*"' | head -1 | sed 's/.*"file_path"\s*:\s*"\([^"]*\)".*/\1/')
fi

[ -z "$FILE_PATH" ] && exit 0

# Resolve to absolute path
case "$FILE_PATH" in
  /*) ABS_PATH="$FILE_PATH" ;;
  *)  ABS_PATH="$ROOT/$FILE_PATH" ;;
esac

# Only act if the file is inside memory-templates/ and ends with .md
case "$ABS_PATH" in
  "$TEMPLATES"/*.md) ;;
  *) exit 0 ;;
esac

[ ! -f "$ABS_PATH" ] && exit 0

# Encode project path for the memory folder name (Claude Code's encoding scheme)
ENCODED="${ROOT//:/-}"
ENCODED="${ENCODED//\//-}"
ENCODED="${ENCODED//\\/-}"
ENCODED="${ENCODED//./-}"
ENCODED="${ENCODED// /-}"
ENCODED="${ENCODED//(/-}"
ENCODED="${ENCODED//)/-}"

HOME_DIR="${HOME:-$USERPROFILE}"
[ -z "$HOME_DIR" ] && exit 0
MEMORY_DIR="$HOME_DIR/.claude/projects/$ENCODED/memory"

mkdir -p "$MEMORY_DIR" 2>/dev/null
FILENAME=$(basename "$ABS_PATH")
if cp "$ABS_PATH" "$MEMORY_DIR/$FILENAME" 2>/dev/null; then
  echo "[memory-sync] Synced $FILENAME → $MEMORY_DIR/$FILENAME" >&2
else
  echo "[memory-sync] Failed to sync $FILENAME" >&2
fi

exit 0
