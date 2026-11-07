#!/usr/bin/env bash
# .claude/hooks/stop-session-writeup.sh
#
# Bash fallback for stop-session-writeup.cjs.
# Updates .claude/.last-session.md with "where we left off" recap on every
# turn boundary. Read by next session's SessionStart hook.
# Pure enablement. Exit 0 always.

ROOT="${CLAUDE_PROJECT_DIR:-$(pwd)}"
STAMP=$(date -u +"%Y-%m-%dT%H:%M:%SZ")
OUT="$ROOT/.claude/.last-session.md"

GIT_BRANCH="_no git_"
if git -C "$ROOT" rev-parse --is-inside-work-tree >/dev/null 2>&1; then
  GIT_BRANCH=$(git -C "$ROOT" rev-parse --abbrev-ref HEAD 2>/dev/null)
fi

TODO_INPROG_COUNT=0
TODO_INPROG_LIST="  - _(none)_"
TODO_PEND_COUNT=0
TODO_PEND_LIST="  - _(none)_"
if [ -f "$ROOT/docs/TODO.md" ]; then
  TODO_INPROG_COUNT=$(grep -c '^###*\s*\[~\]' "$ROOT/docs/TODO.md" 2>/dev/null || echo 0)
  TODO_PEND_COUNT=$(grep -c '^###*\s*\[ \]' "$ROOT/docs/TODO.md" 2>/dev/null || echo 0)
  if [ "$TODO_INPROG_COUNT" -gt 0 ]; then
    TODO_INPROG_LIST=$(grep '^###*\s*\[~\]' "$ROOT/docs/TODO.md" | sed 's/^###*\s*\[~\]\s*/  - /')
  fi
  if [ "$TODO_PEND_COUNT" -gt 0 ]; then
    TODO_PEND_LIST=$(grep '^###*\s*\[ \]' "$ROOT/docs/TODO.md" | sed 's/^###*\s*\[ \]\s*/  - /')
  fi
fi

LAST_FIN="_no docs/FINALIZED.md_"
if [ -f "$ROOT/docs/FINALIZED.md" ]; then
  LAST_FIN=$(grep '^##\s*Session\s' "$ROOT/docs/FINALIZED.md" | tail -1 | sed 's/^##\s*Session\s*//')
  [ -z "$LAST_FIN" ] && LAST_FIN="_no completed sessions yet_"
fi

cat > "$OUT" <<EOF
# Last Session Recap

_Auto-written by \`.claude/hooks/stop-session-writeup.sh\` on every turn boundary. Read by the next SessionStart hook._

## Last update

- **Timestamp:** $STAMP
- **Branch:** \`$GIT_BRANCH\`
- **TODO in_progress ($TODO_INPROG_COUNT):**
$TODO_INPROG_LIST
- **TODO pending ($TODO_PEND_COUNT):**
$TODO_PEND_LIST
- **Last FINALIZED session:** $LAST_FIN

## Pick up here next session

_Edit this section manually to leave handoff notes for the next Unity. Auto-generated metadata above is overwritten on every turn._
EOF

exit 0
