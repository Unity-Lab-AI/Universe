#!/usr/bin/env bash
# .claude/hooks/skill-context-inject.sh
#
# Bash fallback for skill-context-inject.cjs.
# UserPromptExpansion hook. Outputs JSON with hookSpecificOutput.additionalContext
# to inject skill-level context when a slash command expands.
# Pure enablement. Exit 0 always.

ROOT="${CLAUDE_PROJECT_DIR:-$(pwd)}"

# Read stdin payload
INPUT="$(cat 2>/dev/null || echo '{}')"

# Extract skill name (command_name field)
SKILL_NAME=$(echo "$INPUT" | grep -o '"command_name"\s*:\s*"[^"]*"' | head -1 | sed 's/.*"command_name"\s*:\s*"\([^"]*\)".*/\1/')
[ -z "$SKILL_NAME" ] && SKILL_NAME=$(echo "$INPUT" | grep -o '"skill_name"\s*:\s*"[^"]*"' | head -1 | sed 's/.*"skill_name"\s*:\s*"\([^"]*\)".*/\1/')

# YOLO state
YOLO_ACTIVE="false"
[ -f "$ROOT/.claude/.yolo-mode" ] && YOLO_ACTIVE="true"

CONTEXT_PARTS=()

# ─────────────────────────────────────────────────────────────────────
# Block 1: YOLO three-tier cascade state
# ─────────────────────────────────────────────────────────────────────
if [ "$YOLO_ACTIVE" = "true" ]; then
  ROADMAP="$ROOT/docs/ROADMAP.md"
  TODO="$ROOT/docs/TODO.md"
  DECOMPOSED="$ROOT/docs/DECOMPOSED.md"

  first_active() {
    if [ -f "$1" ]; then
      LINE=$(grep -m1 '^###*\s*\[~\]' "$1" 2>/dev/null | sed 's/^###*\s*\[~\]\s*//')
      if [ -z "$LINE" ]; then
        LINE=$(grep -m1 '^###*\s*\[ \]' "$1" 2>/dev/null | sed 's/^###*\s*\[ \]\s*//')
      fi
      echo "$LINE"
    fi
  }

  MAJOR=$(first_active "$ROADMAP")
  MINOR=$(first_active "$TODO")
  DEC=$(first_active "$DECOMPOSED")
  [ -z "$MAJOR" ] && MAJOR="_(none)_"
  [ -z "$MINOR" ] && MINOR="_(none)_"
  [ -z "$DEC" ] && DEC="_(none)_"

  YOLO_BLOCK=$(cat <<EOF
## ⚡ YOLO state (auto-injected by skill-context-inject hook on \`/${SKILL_NAME}\` expansion)

- **Active major milestone:** ${MAJOR}
- **Active minor task:** ${MINOR}
- **Active decomposed task:** ${DEC}

Cascade rule (in case this skill needs to know): work the lowest-grain pending task first; escalate up the tiers when a tier empties; pause auto-resume at major-milestone boundaries; produce FINAL REPORT when ROADMAP.md is fully complete. See \`.claude/commands/yolo.md\` for full mechanics.
EOF
)
  CONTEXT_PARTS+=("$YOLO_BLOCK")
fi

# ─────────────────────────────────────────────────────────────────────
# Block 2: Per-skill hooks
# ─────────────────────────────────────────────────────────────────────

# Persona slash commands
case "$SKILL_NAME" in
  unity|girlfriend|housewife|kittycat|wild|strict|feral|sweet|cozy|purr)
    PERSONA_BLOCK=$(cat <<EOF
## Persona activation reminder (auto-injected by skill-context-inject hook)

The full persona body is embedded directly in \`.claude/commands/${SKILL_NAME}.md\` — reading it IS activating. No chain-following to \`agents/unity-*.md\` needed (those are reference-only). After reading the embedded body, drop the corporate-AI tone immediately and respond in the activated persona's voice from the very first line.
EOF
)
    CONTEXT_PARTS+=("$PERSONA_BLOCK")
    ;;
esac

# /workflow
if [ "$SKILL_NAME" = "workflow" ]; then
  WORKFLOW_BLOCK=$(cat <<'EOF'
## /workflow LAW gate reminder (auto-injected by skill-context-inject hook)

Phase -1 (`LAW #0 verbatim words check`) cannot be skipped. Re-read the user's latest instruction, count the items, confirm every noun/verb is preserved in any tasks created. See `.claude/WORKFLOW.md` Phase -1 + `.claude/CONSTRAINTS.md §LAW #0`.
EOF
)
  CONTEXT_PARTS+=("$WORKFLOW_BLOCK")
fi

# /yolo
if [ "$SKILL_NAME" = "yolo" ]; then
  YOLO_ACTIVATE=$(cat <<'EOF'
## /yolo activation checklist (auto-injected by skill-context-inject hook)

1. Write `.claude/.yolo-mode` marker file with timestamp + active persona
2. Read all three task tiers: `docs/ROADMAP.md` (major), `docs/TODO.md` (minor), `docs/DECOMPOSED.md` (decomposed)
3. Print persona-voice activation announcement with current cascade position
4. Pick next task per cascade rule (decomposed → minor → major)
5. At end of every YOLO turn, call `ScheduleWakeup(60s, prompt="<<autonomous-loop-dynamic>>", reason="YOLO auto-resume")` — UNLESS a stop condition is met (milestone boundary, ROADMAP empty, user interrupt, bash-safety fire)
6. Required at every meaningful task closure: USER TEST PLAN format
EOF
)
  CONTEXT_PARTS+=("$YOLO_ACTIVATE")
fi

# /sober
if [ "$SKILL_NAME" = "sober" ]; then
  SOBER_BLOCK=$(cat <<'EOF'
## /sober checklist (auto-injected by skill-context-inject hook)

1. Remove `.claude/.yolo-mode` marker (no-op if it doesn't exist)
2. End the wake-word chain — do NOT call `ScheduleWakeup` at end of this turn
3. Print short persona-voice deactivation message
4. Subsequent prompts revert to default ask-then-act behavior
EOF
)
  CONTEXT_PARTS+=("$SOBER_BLOCK")
fi

# ─────────────────────────────────────────────────────────────────────
# Combine and emit JSON envelope
# ─────────────────────────────────────────────────────────────────────
SEPARATOR=$'\n\n---\n\n'
ADDITIONAL_CONTEXT=""
for i in "${!CONTEXT_PARTS[@]}"; do
  if [ "$i" -eq 0 ]; then
    ADDITIONAL_CONTEXT="${CONTEXT_PARTS[$i]}"
  else
    ADDITIONAL_CONTEXT="${ADDITIONAL_CONTEXT}${SEPARATOR}${CONTEXT_PARTS[$i]}"
  fi
done

# JSON-escape the additional context (newlines, quotes, backslashes)
JSON_ESCAPED=$(printf '%s' "$ADDITIONAL_CONTEXT" | python3 -c 'import json,sys; print(json.dumps(sys.stdin.read()))' 2>/dev/null || \
               printf '%s' "$ADDITIONAL_CONTEXT" | sed 's/\\/\\\\/g; s/"/\\"/g; s/$/\\n/' | tr -d '\n' | sed 's/\\n$//')

if [ -n "$ADDITIONAL_CONTEXT" ]; then
  if [ -n "$(echo "$JSON_ESCAPED" | head -c 1)" ] && [ "${JSON_ESCAPED:0:1}" = '"' ]; then
    # Python jsonified — already includes quotes
    printf '{"hookSpecificOutput":{"hookEventName":"UserPromptExpansion","additionalContext":%s}}' "$JSON_ESCAPED"
  else
    # Fallback escaping — wrap in quotes
    printf '{"hookSpecificOutput":{"hookEventName":"UserPromptExpansion","additionalContext":"%s"}}' "$JSON_ESCAPED"
  fi
else
  printf '{"hookSpecificOutput":{"hookEventName":"UserPromptExpansion"}}'
fi

exit 0
