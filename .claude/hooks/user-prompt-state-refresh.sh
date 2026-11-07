#!/usr/bin/env bash
# .claude/hooks/user-prompt-state-refresh.sh
#
# Bash fallback for user-prompt-state-refresh.cjs.
# UserPromptSubmit hook. Outputs compact state recap on stdout for context injection.
# When YOLO mode is active, also injects the three-tier cascade state.
# Pure enablement. Exit 0 always.

ROOT="${CLAUDE_PROJECT_DIR:-$(pwd)}"

GIT_BRANCH=""
GIT_IS_REPO="false"
if git -C "$ROOT" rev-parse --is-inside-work-tree >/dev/null 2>&1; then
  GIT_IS_REPO="true"
  GIT_BRANCH=$(git -C "$ROOT" rev-parse --abbrev-ref HEAD 2>/dev/null)
fi

# Opt-in state
OPT_STATE="UNSET"
if [ -f "$ROOT/.claude/project-config.json" ]; then
  if grep -q '"enabled"\s*:\s*true' "$ROOT/.claude/project-config.json"; then
    OPT_STATE="ENABLED"
  elif grep -q '"enabled"\s*:\s*false' "$ROOT/.claude/project-config.json"; then
    OPT_STATE="DISABLED"
  fi
fi

# TODO summary
TODO="$ROOT/docs/TODO.md"
TODO_INPROG=0
TODO_TITLES=""
if [ -f "$TODO" ]; then
  TODO_INPROG=$(grep -c '^###*\s*\[~\]' "$TODO" 2>/dev/null || echo 0)
  if [ "$TODO_INPROG" -gt 0 ]; then
    TODO_TITLES=$(grep '^###*\s*\[~\]' "$TODO" 2>/dev/null | head -3 | sed 's/^###*\s*\[~\]\s*/`/' | sed 's/$/`/' | paste -sd ', ' -)
  fi
fi

# YOLO mode
YOLO_ENABLED="false"
[ -f "$ROOT/.claude/.yolo-mode" ] && YOLO_ENABLED="true"

echo "## State refresh (auto-injected by .claude/hooks/user-prompt-state-refresh.sh)"
echo ""
if [ "$GIT_IS_REPO" = "true" ]; then
  echo "- **Branch:** \`$GIT_BRANCH\`"
else
  echo "- **Branch:** not a git repo"
fi
echo "- **Git Flow opt-in:** $OPT_STATE"
if [ -n "$TODO_TITLES" ]; then
  echo "- **TODO in_progress:** $TODO_INPROG — $TODO_TITLES"
else
  echo "- **TODO in_progress:** $TODO_INPROG"
fi
if [ "$YOLO_ENABLED" = "true" ]; then
  echo "- **YOLO mode:** ⚡ ENABLED (lead-dev autonomy + 60s wake-word chain; user test plan required at every meaningful boundary)"
else
  echo "- **YOLO mode:** DISABLED"
fi

# Branch warning
if [ "$GIT_IS_REPO" = "true" ] && [ "$OPT_STATE" = "ENABLED" ]; then
  case "$GIT_BRANCH" in
    main|master|develop|prod|production)
      echo ""
      echo "⚠ **On protected branch \`$GIT_BRANCH\`** — branch into \`feature/<descriptor>\` before editing per CONSTRAINTS.md §GIT FLOW."
      ;;
  esac
fi

# YOLO three-tier cascade state — only when YOLO active
if [ "$YOLO_ENABLED" = "true" ]; then
  ROADMAP="$ROOT/docs/ROADMAP.md"
  DECOMPOSED="$ROOT/docs/DECOMPOSED.md"

  first_inprog() {
    [ -f "$1" ] && grep -m1 '^###*\s*\[~\]' "$1" 2>/dev/null | sed 's/^###*\s*\[~\]\s*//'
  }
  first_pending() {
    [ -f "$1" ] && grep -m1 '^###*\s*\[ \]' "$1" 2>/dev/null | sed 's/^###*\s*\[ \]\s*//'
  }
  count_pending() {
    if [ -f "$1" ]; then
      grep -c '^###*\s*\[ \]' "$1" 2>/dev/null || echo 0
    else
      echo 0
    fi
  }

  MAJOR_ACTIVE=$(first_inprog "$ROADMAP")
  MAJOR_NEXT=$(first_pending "$ROADMAP")
  MINOR_ACTIVE=$(first_inprog "$TODO")
  MINOR_NEXT=$(first_pending "$TODO")
  DECOMPOSED_ACTIVE=$(first_inprog "$DECOMPOSED")
  DECOMPOSED_NEXT=$(first_pending "$DECOMPOSED")
  MAJOR_PENDING=$(count_pending "$ROADMAP")
  MINOR_PENDING=$(count_pending "$TODO")
  DECOMPOSED_PENDING=$(count_pending "$DECOMPOSED")

  [ -z "$MAJOR_ACTIVE" ] && MAJOR_ACTIVE="_(none)_"
  [ -z "$MAJOR_NEXT" ] && MAJOR_NEXT="_(none)_"
  [ -z "$MINOR_ACTIVE" ] && MINOR_ACTIVE="_(none)_"
  [ -z "$MINOR_NEXT" ] && MINOR_NEXT="_(none)_"
  [ -z "$DECOMPOSED_ACTIVE" ] && DECOMPOSED_ACTIVE="_(none)_"
  [ -z "$DECOMPOSED_NEXT" ] && DECOMPOSED_NEXT="_(none)_"

  echo ""
  echo "### YOLO three-tier cascade state"
  echo ""
  echo "| Tier | In progress (\`[~]\`) | Next pending (\`[ ]\`) | Pending count |"
  echo "|------|---------------------|----------------------|---------------|"
  echo "| **Major** (\`docs/ROADMAP.md\`) | $MAJOR_ACTIVE | $MAJOR_NEXT | $MAJOR_PENDING |"
  echo "| **Minor** (\`docs/TODO.md\`) | $MINOR_ACTIVE | $MINOR_NEXT | $MINOR_PENDING |"
  echo "| **Decomposed** (\`docs/DECOMPOSED.md\`) | $DECOMPOSED_ACTIVE | $DECOMPOSED_NEXT | $DECOMPOSED_PENDING |"
  echo ""

  # Cascade hint
  if [ "$DECOMPOSED_ACTIVE" != "_(none)_" ]; then
    HINT="→ **Continue current decomposed task** in DECOMPOSED.md"
  elif [ "$DECOMPOSED_NEXT" != "_(none)_" ]; then
    HINT="→ **Pick next decomposed task** from DECOMPOSED.md, flip \`[ ]\` → \`[~]\`"
  elif [ "$MINOR_ACTIVE" != "_(none)_" ]; then
    HINT="→ **Decompose current minor task** into DECOMPOSED.md, then pick first decomposed"
  elif [ "$MINOR_NEXT" != "_(none)_" ]; then
    HINT="→ **Pick next minor task** from TODO.md, flip \`[ ]\` → \`[~]\`, decompose into DECOMPOSED.md"
  elif [ "$MAJOR_ACTIVE" != "_(none)_" ]; then
    HINT="→ **Current major milestone has no minor tasks left** — surface MILESTONE-BOUNDARY check-in (pause auto-resume)"
  elif [ "$MAJOR_NEXT" != "_(none)_" ]; then
    HINT="→ **Pick next major milestone** — but REQUIRES user confirmation, do NOT auto-proceed past major boundaries"
  else
    HINT="→ **Nothing left** — produce FINAL REPORT, auto-deactivate YOLO"
  fi
  echo "**Cascade hint:** $HINT"
fi

# ─────────────────────────────────────────────────────────────────────
# Usage tracking banner — only when .session-usage.jsonl exists AND
# .usage-tracking-disabled marker is absent.
# Gross tokens undercounted ~100x per transcript bug; cache fields accurate.
# ─────────────────────────────────────────────────────────────────────
USAGE_LOG="$ROOT/.claude/.session-usage.jsonl"
USAGE_DISABLED="$ROOT/.claude/.usage-tracking-disabled"
if [ -f "$USAGE_LOG" ] && [ ! -f "$USAGE_DISABLED" ]; then
  USAGE_BANNER=$(python3 - "$USAGE_LOG" <<'PYEOF' 2>/dev/null
import json, sys
log = sys.argv[1]
entries = []
try:
    with open(log, "r") as f:
        for line in f:
            line = line.strip()
            if not line: continue
            try: entries.append(json.loads(line))
            except: continue
except Exception:
    sys.exit(0)
if not entries: sys.exit(0)
total_in = total_out = total_cc = total_cr = 0
per_task = {}
for e in entries:
    total_in  += e.get("input_tokens") or 0
    total_out += e.get("output_tokens") or 0
    total_cc  += e.get("cache_creation_input_tokens") or 0
    total_cr  += e.get("cache_read_input_tokens") or 0
    key = e.get("active_decomposed") or e.get("active_minor") or e.get("active_major") or "_(no task)_"
    per_task[key] = per_task.get(key, 0) + (e.get("output_tokens") or 0)
last = entries[-1]
cache_total = total_cr + total_cc
cache_pct = round(100 * total_cr / cache_total) if cache_total > 0 else None
top = sorted(per_task.items(), key=lambda x: -x[1])[:3]
out = []
out.append("")
out.append("### Session usage (transcript-based — gross tokens undercount ~100x; cache metrics accurate)")
out.append("")
out.append(f"- **Turns tracked:** {len(entries)}  |  **Cumulative:** ~{total_out} out / ~{total_in} in tokens")
out.append(f"- **Last turn:** ~{last.get('output_tokens') or 0} out / ~{last.get('input_tokens') or 0} in  |  cache: {last.get('cache_read_input_tokens') or 0} read, {last.get('cache_creation_input_tokens') or 0} create")
if cache_pct is not None:
    note = "✓ STABLE PREFIX validating" if cache_pct >= 50 else "partial cache hits" if cache_pct >= 20 else "cold cache / drift"
    out.append(f"- **Cache hit ratio:** {cache_pct}% ({note})")
if top and top[0][0] != "_(no task)_":
    out.append("- **Top tasks by output tokens (this session):**")
    for task, tokens in top:
        ts = task if len(task) <= 80 else task[:77] + "..."
        out.append(f"  - `{ts}` — ~{tokens} out")
out.append("- For authoritative session totals, type `/usage` (Claude Code native)")
print("\n".join(out))
PYEOF
)
  if [ -n "$USAGE_BANNER" ]; then
    echo "$USAGE_BANNER"
  fi
fi

exit 0
