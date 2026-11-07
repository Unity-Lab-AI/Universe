#!/usr/bin/env bash
# .claude/hooks/pre-tool-bash-safety.sh
#
# Bash fallback for pre-tool-bash-safety.cjs.
# The ONLY blocking hook in the bundle. Catches three classes of action:
#   1. Project-root deletion
#   2. System-path deletion
#   3. Privileged commands (sudo/su/doas/runas/RunAs)
# Routes them to the user with a paste-ready command instead of running
# them inside the Claude session. Exit 2 blocks; exit 0 lets through.

ROOT="${CLAUDE_PROJECT_DIR:-$(pwd)}"

INPUT="$(cat 2>/dev/null || echo '')"
[ -z "$INPUT" ] && exit 0

# Extract command (best-effort grep — jq if available)
if command -v jq >/dev/null 2>&1; then
  CMD=$(echo "$INPUT" | jq -r '.toolInput.command // .tool_input.command // .input.command // empty' 2>/dev/null)
else
  CMD=$(echo "$INPUT" | grep -o '"command"\s*:\s*"[^"]*"' | head -1 | sed 's/.*"command"\s*:\s*"\([^"]*\)".*/\1/')
fi

[ -z "$CMD" ] && exit 0

# === 1. Project-root or relative-root deletion ===
if echo "$CMD" | grep -Eq '\brm\s+(-[a-zA-Z]*[rRfF][a-zA-Z]*\s+)+(\.|\.\/|\.\.|\.\.\/|\$HOME|~|\$\{?PROJECT_ROOT\}?|\$PWD)(\s|$)'; then
  cat >&2 <<EOF
[bash-safety] BLOCKED — recursive delete targets project root or above.

Command: $CMD

If this is intentional, run it manually in your own terminal:
    $CMD

Then reply "done" and I will continue.
EOF
  exit 2
fi

# Literal project root path
ESCAPED_ROOT=$(echo "$ROOT" | sed 's/[\/.*[\]^$]/\\&/g')
if echo "$CMD" | grep -Eq "\brm\s+(-[a-zA-Z]*[rRfF][a-zA-Z]*\s+)+($ESCAPED_ROOT/?|$ESCAPED_ROOT/\.\.+)(\s|$)"; then
  cat >&2 <<EOF
[bash-safety] BLOCKED — recursive delete targets project root or above.

Command: $CMD

If this is intentional, run it manually in your own terminal:
    $CMD

Then reply "done" and I will continue.
EOF
  exit 2
fi

# === 2. System-path deletion ===
if echo "$CMD" | grep -Eqi '\brm\s+(-[a-zA-Z]*[rRfF][a-zA-Z]*\s+)+(/(etc|usr|bin|sbin|var|boot|lib|opt|root|sys|proc|dev|System)(/|\s|$)|C:\\|C:/)'; then
  cat >&2 <<EOF
[bash-safety] BLOCKED — recursive delete targets system path.

Command: $CMD

System-level deletes are NEVER safe to run inside a Claude session.
If this is genuinely required, run it manually with appropriate care:
    $CMD

Then reply "done" and I will continue.
EOF
  exit 2
fi

# === 3. Privileged commands → delegate ===
if echo "$CMD" | grep -Eqi '^(\s*sudo\s|\s*su\s|\s*doas\s|\s*runas\s|.*Start-Process.*-Verb\s+RunAs)'; then
  cat >&2 <<EOF
[bash-safety] DELEGATING — privileged command needs your authorization.

Command: $CMD

This needs to run in your own terminal where you can authenticate as admin/sudo/root.
Please run this exact command in a separate terminal:
    $CMD

Once the command completes, reply "done" (and paste any relevant output) and I will continue.
EOF
  exit 2
fi

exit 0
