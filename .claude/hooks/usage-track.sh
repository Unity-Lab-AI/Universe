#!/usr/bin/env bash
# .claude/hooks/usage-track.sh
#
# Bash fallback for usage-track.cjs.
# Stop hook. Reads stdin payload, parses transcript JSONL for last assistant
# usage object, appends per-turn line to .claude/.session-usage.jsonl.
#
# Caveat: transcript input_tokens/output_tokens are undercounted ~100x due
# to streaming placeholder bug. Cache fields are accurate.
#
# Pure enablement. Exit 0 always.

ROOT="${CLAUDE_PROJECT_DIR:-$(pwd)}"
STAMP=$(date -u +"%Y-%m-%dT%H:%M:%SZ")
USAGE_LOG="$ROOT/.claude/.session-usage.jsonl"

# Read stdin
INPUT="$(cat 2>/dev/null || echo '{}')"
TRANSCRIPT_PATH=$(echo "$INPUT" | grep -o '"transcript_path"\s*:\s*"[^"]*"' | head -1 | sed 's/.*"transcript_path"\s*:\s*"\([^"]*\)".*/\1/')
SESSION_ID=$(echo "$INPUT" | grep -o '"session_id"\s*:\s*"[^"]*"' | head -1 | sed 's/.*"session_id"\s*:\s*"\([^"]*\)".*/\1/')
[ -z "$SESSION_ID" ] && SESSION_ID="unknown"

# Branch
BRANCH=$(git -C "$ROOT" rev-parse --abbrev-ref HEAD 2>/dev/null || echo "")

# Active task (cascade order)
first_inprog() {
  [ -f "$1" ] && grep -m1 '^###*\s*\[~\]' "$1" 2>/dev/null | sed 's/^###*\s*\[~\]\s*//' | sed 's/"/\\"/g'
}
ACTIVE_MAJOR=$(first_inprog "$ROOT/docs/ROADMAP.md")
ACTIVE_MINOR=$(first_inprog "$ROOT/docs/TODO.md")
ACTIVE_DECOMPOSED=$(first_inprog "$ROOT/docs/DECOMPOSED.md")

# Parse last assistant usage from transcript via Python (best for JSONL)
PY_SCRIPT=$(cat <<'PYEOF'
import json, sys, os
tp = sys.argv[1] if len(sys.argv) > 1 else ""
if not tp or not os.path.exists(tp):
    print("{}")
    sys.exit(0)
last = None
with open(tp, "r") as f:
    for line in f:
        line = line.strip()
        if not line:
            continue
        try:
            entry = json.loads(line)
        except Exception:
            continue
        usage = entry.get("usage") or (entry.get("message", {}) or {}).get("usage")
        if usage and any(usage.get(k) is not None for k in ["input_tokens", "output_tokens", "cache_read_input_tokens", "cache_creation_input_tokens"]):
            last = {
                "usage": usage,
                "model": entry.get("model") or (entry.get("message", {}) or {}).get("model"),
                "message_id": entry.get("message_id") or (entry.get("message", {}) or {}).get("id"),
            }
print(json.dumps(last) if last else "{}")
PYEOF
)

LAST_USAGE_JSON=$(python3 -c "$PY_SCRIPT" "$TRANSCRIPT_PATH" 2>/dev/null || echo "{}")

# Build entry
build_entry() {
  python3 -c '
import json, sys
last = json.loads(sys.argv[1]) if sys.argv[1] else {}
entry = {
  "ts": sys.argv[2],
  "session_id": sys.argv[3],
  "message_id": last.get("message_id") if last else None,
  "model": last.get("model") if last else None,
  "input_tokens": (last.get("usage") or {}).get("input_tokens"),
  "output_tokens": (last.get("usage") or {}).get("output_tokens"),
  "cache_creation_input_tokens": (last.get("usage") or {}).get("cache_creation_input_tokens"),
  "cache_read_input_tokens": (last.get("usage") or {}).get("cache_read_input_tokens"),
  "active_major": sys.argv[4] or None,
  "active_minor": sys.argv[5] or None,
  "active_decomposed": sys.argv[6] or None,
  "branch": sys.argv[7] or None,
}
if not last:
  entry["note"] = "no_usage_in_transcript"
print(json.dumps(entry))
' "$LAST_USAGE_JSON" "$STAMP" "$SESSION_ID" "$ACTIVE_MAJOR" "$ACTIVE_MINOR" "$ACTIVE_DECOMPOSED" "$BRANCH"
}

ENTRY=$(build_entry 2>/dev/null)
if [ -n "$ENTRY" ] && [ "$ENTRY" != "null" ]; then
  echo "$ENTRY" >> "$USAGE_LOG"
fi

exit 0
