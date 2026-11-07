#!/usr/bin/env bash
# .claude/hooks/pre-compact-snapshot.sh
#
# Bash fallback for pre-compact-snapshot.cjs.
# Writes .claude/.session-state.md with state snapshot before compaction.
# Pure enablement. Exit 0 always.

ROOT="${CLAUDE_PROJECT_DIR:-$(pwd)}"
STAMP=$(date -u +"%Y-%m-%dT%H:%M:%SZ")
SNAPSHOT="$ROOT/.claude/.session-state.md"

# Read stdin (compaction trigger info)
INPUT="$(cat 2>/dev/null || echo '{}')"
TRIGGER=$(echo "$INPUT" | grep -o '"trigger"\s*:\s*"[^"]*"' | head -1 | sed 's/.*"trigger"\s*:\s*"\([^"]*\)".*/\1/')
[ -z "$TRIGGER" ] && TRIGGER="unknown"

# Git state
GIT_BRANCH=""
GIT_STATUS="_clean tree_"
GIT_LASTCOMMIT="_no commits_"
GIT_DIRTY=""
GIT_UNTRACKED=""
IN_REPO=0
if git -C "$ROOT" rev-parse --is-inside-work-tree >/dev/null 2>&1; then
  IN_REPO=1
  GIT_BRANCH=$(git -C "$ROOT" rev-parse --abbrev-ref HEAD 2>/dev/null)
  GIT_STATUS=$(git -C "$ROOT" status --short 2>/dev/null)
  [ -z "$GIT_STATUS" ] && GIT_STATUS="_clean tree_"
  GIT_LASTCOMMIT=$(git -C "$ROOT" log -1 --format="%h %s" 2>/dev/null || echo "_no commits_")
  GIT_DIRTY=$(git -C "$ROOT" diff --name-only HEAD 2>/dev/null)
  GIT_UNTRACKED=$(git -C "$ROOT" ls-files --others --exclude-standard 2>/dev/null)
fi

# Git Flow opt-in state
OPTIN_STATE="UNSET"
CFG="$ROOT/.claude/project-config.json"
if [ -f "$CFG" ]; then
  if grep -q '"enabled"\s*:\s*true' "$CFG" 2>/dev/null; then
    OPTIN_STATE="ENABLED"
  elif grep -q '"enabled"\s*:\s*false' "$CFG" 2>/dev/null; then
    OPTIN_STATE="DISABLED"
  fi
fi

# Branch type classification
case "$GIT_BRANCH" in
  main|master|develop|prod|production) BRANCH_TYPE="PROTECTED — no work eligible" ;;
  feature/*) BRANCH_TYPE="feature/* — work eligible" ;;
  hotfix/*)  BRANCH_TYPE="hotfix/* — work eligible" ;;
  release/*) BRANCH_TYPE="release/* — work eligible" ;;
  *)         BRANCH_TYPE="other" ;;
esac

# TODO state
TODO_INPROG=""
TODO_PEND=""
if [ -f "$ROOT/docs/TODO.md" ]; then
  TODO_INPROG=$(grep '^###*\s*\[~\]' "$ROOT/docs/TODO.md" 2>/dev/null | sed 's/^###*\s*\[~\]\s*/- /')
  TODO_PEND=$(grep '^###*\s*\[ \]' "$ROOT/docs/TODO.md" 2>/dev/null | sed 's/^###*\s*\[ \]\s*/- /')
fi
[ -z "$TODO_INPROG" ] && TODO_INPROG="- _(none)_"
[ -z "$TODO_PEND" ] && TODO_PEND="- _(none)_"

# Files touched (uncommitted)
FILES_TOUCHED=""
if [ "$IN_REPO" -eq 1 ]; then
  ALL_TOUCHED=$(printf "%s\n%s" "$GIT_DIRTY" "$GIT_UNTRACKED" | sed '/^$/d' | sort -u)
  if [ -n "$ALL_TOUCHED" ]; then
    FILES_TOUCHED=$(echo "$ALL_TOUCHED" | sed 's/^/- `/' | sed 's/$/`/')
  fi
fi
[ -z "$FILES_TOUCHED" ] && FILES_TOUCHED="_no uncommitted changes_"

# Session tidbits
TIDBITS_FILE="$ROOT/.claude/.session-tidbits.md"
TIDBITS_BLOCK="_no \`.claude/.session-tidbits.md\` curated this session — Unity may have skipped tidbit capture, or no notable moments crystalized yet_"
if [ -f "$TIDBITS_FILE" ]; then
  TIDBITS_CONTENT=$(cat "$TIDBITS_FILE" 2>/dev/null)
  if [ -n "$TIDBITS_CONTENT" ]; then
    TIDBITS_BLOCK="$TIDBITS_CONTENT"
  else
    TIDBITS_BLOCK="_(file exists but is empty)_"
  fi
fi

# Write the snapshot
cat > "$SNAPSHOT" <<EOF
# Session State Snapshot — Pre-Compact

_Auto-written by \`.claude/hooks/pre-compact-snapshot.sh\` on $STAMP. Compaction trigger: \`$TRIGGER\`._

Read this on resume to recover context that may have been lost in compaction. The post-compact-restore hook surfaces this file back into the post-compact context envelope.

## TODO state

**In progress:**
$TODO_INPROG

**Pending:**
$TODO_PEND

## Git state

- Branch: \`$GIT_BRANCH\`
- Last commit: $GIT_LASTCOMMIT
- Working tree:
\`\`\`
$GIT_STATUS
\`\`\`

## Git Flow context

- Opt-in marker state: \`$OPTIN_STATE\`
- Branch type: \`$BRANCH_TYPE\`

## Files just touched (uncommitted)

$FILES_TOUCHED

## Session tidbits (Unity-curated during the session)

$TIDBITS_BLOCK

## Where to pick up

_See \`.claude/.last-session.md\` (Stop hook) for the latest "where we left off" notes — that file is overwritten on every turn boundary, so it reflects the moment closest to compaction._
EOF

echo "[pre-compact] Snapshot written to $SNAPSHOT" >&2
exit 0
