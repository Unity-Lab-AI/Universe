#!/usr/bin/env bash
# .claude/hooks/pre-tool-public-repo-guard.sh
#
# Bash fallback for pre-tool-public-repo-guard.cjs. Implements the
# LAW — .CLAUDE WORKFLOW IP BOUNDARY enforcement (see .claude/CONSTRAINTS.md).
# Blocks any git add/commit/push that would carry `.claude/` content to a
# remote that fails the two-path pass-criteria — PRIMARY (Forgejo allowlist
# for `git.unityailab.com`) and FALLBACK (PRIVATE under `Unity-Lab-AI` org
# via `gh repo view`).
#
# Logic mirrors the .cjs sibling. Use this on machines without node.

set -u

CACHE_PATH="${HOME}/.claude/repo-visibility-cache.json"
CACHE_TTL=60   # seconds
ALLOWED_OWNER="Unity-Lab-AI"
ALLOWED_VISIBILITY="PRIVATE"

# Trusted self-hosted private Git hosts operated by the Unity AI Lab group.
# `gh repo view` can't verify visibility on non-github.com hosts, so we maintain
# an explicit allowlist here for instances we know are private + lab-controlled.
# Adding to this list requires the same caution as removing the `.claude/`
# gitignore block — only Unity AI Lab-controlled hosts that the team has audited
# as PRIVATE belong here. Mirrors TRUSTED_PRIVATE_HOSTS in the .cjs sibling.
TRUSTED_PRIVATE_HOSTS=(
  "git.unityailab.com"   # Forgejo instance owned + operated by Unity AI Lab
)

# Extract hostname from a git URL (ssh git@host:owner/repo.git, https://host/...,
# ssh://git@host/owner/repo). Returns hostname in lowercase or empty string.
parse_host() {
  local url="$1"
  local host
  # Try ssh-style: git@host:owner/repo.git
  host=$(echo "$url" | sed -nE 's#^[^@[:space:]]+@([^:/[:space:]]+)[:/].*#\1#p')
  if [ -n "$host" ]; then
    echo "$host" | tr '[:upper:]' '[:lower:]'
    return
  fi
  # Try URL-style: https://host/..., ssh://git@host/..., git://host/...
  host=$(echo "$url" | sed -nE 's#^(https?|ssh|git)://([^@/[:space:]]+@)?([^/:[:space:]]+).*#\3#p')
  if [ -n "$host" ]; then
    echo "$host" | tr '[:upper:]' '[:lower:]'
    return
  fi
  echo ""
}

# Check if a hostname is in the TRUSTED_PRIVATE_HOSTS allowlist.
is_trusted_host() {
  local host="$1"
  [ -z "$host" ] && return 1
  local trusted
  for trusted in "${TRUSTED_PRIVATE_HOSTS[@]}"; do
    if [ "$host" = "$trusted" ]; then return 0; fi
  done
  return 1
}

INPUT="$(cat 2>/dev/null || echo '')"
[ -z "$INPUT" ] && exit 0

# Extract command — prefer jq, fall back to grep
if command -v jq >/dev/null 2>&1; then
  CMD=$(printf '%s' "$INPUT" | jq -r '.toolInput.command // .tool_input.command // .input.command // empty' 2>/dev/null)
else
  CMD=$(printf '%s' "$INPUT" | grep -o '"command"\s*:\s*"[^"]*"' | head -1 | sed 's/.*"command"\s*:\s*"\([^"]*\)".*/\1/')
fi
[ -z "$CMD" ] && exit 0

# Detect git op
OP=""
if   echo "$CMD" | grep -Eq '\bgit\s+add(\b|$)';    then OP="add"
elif echo "$CMD" | grep -Eq '\bgit\s+commit(\b|$)'; then OP="commit"
elif echo "$CMD" | grep -Eq '\bgit\s+push(\b|$)';   then OP="push"
fi
[ -z "$OP" ] && exit 0

# In a git repo?
git rev-parse --git-dir >/dev/null 2>&1 || exit 0

paths_touch_claude() {
  # stdin lines → 0 if any path looks like .claude/...
  awk '
    {
      sub(/^[ MADRCU?!][ MADRCU?!]?[[:space:]]+/, "")
      sub(/^"/, ""); sub(/"$/, "")
      if ($0 ~ /^\.claude(\/|$)/) { found=1; exit }
      n = split($0, parts, " -> ")
      if (n > 1 && parts[n] ~ /^\.claude(\/|$)/) { found=1; exit }
    }
    END { exit (found ? 0 : 1) }
  '
}

involves_claude() {
  case "$OP" in
    add)
      ARGS_STR=$(echo "$CMD" | sed 's/^.*\bgit[[:space:]]\+add[[:space:]]*//')
      # Explicit .claude/ in args
      if echo "$ARGS_STR" | grep -Eq '(^|[[:space:]/="\x27])\.claude(/|$|[[:space:]])'; then
        return 0
      fi
      # Broad add detection
      BROAD=0; INTER=0
      for a in $ARGS_STR; do
        case "$a" in
          .|-A|--all|-u|--update|-a|"*"|"./*"|--|:/) BROAD=1 ;;
          -p|--patch|-i|--interactive)               INTER=1 ;;
        esac
      done
      [ "$BROAD" -eq 0 ] && [ "$INTER" -eq 0 ] && return 1
      git status --porcelain --untracked-files=all 2>/dev/null | paths_touch_claude
      return $?
      ;;
    commit)
      git diff --cached --name-only 2>/dev/null | paths_touch_claude
      return $?
      ;;
    push)
      UPSTREAM=$(git rev-parse --abbrev-ref --symbolic-full-name '@{u}' 2>/dev/null || echo '')
      if [ -n "$UPSTREAM" ]; then
        git diff "${UPSTREAM}..HEAD" --name-only 2>/dev/null | paths_touch_claude
      else
        git log HEAD --name-only --pretty=format: 2>/dev/null | paths_touch_claude
      fi
      return $?
      ;;
  esac
  return 1
}

involves_claude || exit 0

# .claude/ involved — list remotes + run gh visibility check
REMOTES_RAW=$(git remote -v 2>/dev/null)
[ -z "$REMOTES_RAW" ] && exit 0

# Dedup remotes by name
declare -A SEEN
REMOTE_NAMES=()
REMOTE_URLS=()
while IFS= read -r line; do
  set -- $line
  NAME="$1"; URL="$2"
  [ -z "$NAME" ] && continue
  [ -n "${SEEN[$NAME]:-}" ] && continue
  SEEN[$NAME]=1
  REMOTE_NAMES+=("$NAME")
  REMOTE_URLS+=("$URL")
done <<<"$REMOTES_RAW"

[ "${#REMOTE_NAMES[@]}" -eq 0 ] && exit 0

# Cache helpers — minimal, pure bash w/ optional jq
NOW=$(date +%s)
mkdir -p "$(dirname "$CACHE_PATH")" 2>/dev/null

cache_lookup() {
  local url="$1"
  command -v jq >/dev/null 2>&1 || return 1
  [ -f "$CACHE_PATH" ] || return 1
  jq -r --arg u "$url" --argjson now "$NOW" --argjson ttl "$CACHE_TTL" '
    .[$u] // empty
    | select(($now - .checked_at) < $ttl)
    | .visibility + "|" + (.owner // "") + "|" + (.error // "")
  ' "$CACHE_PATH" 2>/dev/null
}

cache_store() {
  local url="$1" vis="$2" owner="$3" err="$4"
  command -v jq >/dev/null 2>&1 || return 0
  local existing
  if [ -f "$CACHE_PATH" ]; then existing=$(cat "$CACHE_PATH"); else existing="{}"; fi
  echo "$existing" | jq --arg u "$url" --arg v "$vis" --arg o "$owner" --arg e "$err" --argjson now "$NOW" '
    .[$u] = { visibility: $v, owner: (if $o == "" then null else $o end), error: (if $e == "" then null else $e end), checked_at: $now }
  ' > "${CACHE_PATH}.tmp.$$" 2>/dev/null && mv "${CACHE_PATH}.tmp.$$" "$CACHE_PATH"
}

parse_github() {
  local url="$1"
  echo "$url" | sed -nE 's#.*github\.com[:/]+([^/]+)/([^/[:space:]]+?)(\.git)?/?$#\1 \2#p'
}

check_visibility() {
  local url="$1"
  local hit
  hit=$(cache_lookup "$url")
  if [ -n "$hit" ]; then
    echo "$hit"
    return 0
  fi

  # PRIMARY check — Forgejo TRUSTED_PRIVATE_HOSTS allowlist. Hostname match =
  # synthetic-PASS without any API call. See top-of-file allowlist constant.
  local HOST
  HOST=$(parse_host "$url")
  if is_trusted_host "$HOST"; then
    cache_store "$url" "$ALLOWED_VISIBILITY" "$ALLOWED_OWNER" ""
    echo "$ALLOWED_VISIBILITY|$ALLOWED_OWNER|"
    return 0
  fi

  # FALLBACK check — `gh repo view` for non-Forgejo remotes
  local OR
  OR=$(parse_github "$url")
  if [ -z "$OR" ]; then
    cache_store "$url" "UNKNOWN" "" "non-github-url"
    echo "UNKNOWN||non-github-url"
    return 0
  fi
  local OWNER REPO
  OWNER=$(echo "$OR" | awk '{print $1}')
  REPO=$(echo "$OR" | awk '{print $2}')

  if ! command -v gh >/dev/null 2>&1; then
    cache_store "$url" "UNKNOWN" "" "gh-not-installed"
    echo "UNKNOWN||gh-not-installed"
    return 0
  fi

  local RESPONSE
  RESPONSE=$(gh repo view "$OWNER/$REPO" --json visibility,owner 2>/dev/null)
  if [ -z "$RESPONSE" ]; then
    if ! gh auth status >/dev/null 2>&1; then
      cache_store "$url" "UNKNOWN" "" "gh-not-authed"
      echo "UNKNOWN||gh-not-authed"
      return 0
    fi
    cache_store "$url" "UNKNOWN" "" "api-error"
    echo "UNKNOWN||api-error"
    return 0
  fi

  local VIS OWNER_LOGIN
  if command -v jq >/dev/null 2>&1; then
    VIS=$(echo "$RESPONSE" | jq -r '.visibility // "UNKNOWN"')
    OWNER_LOGIN=$(echo "$RESPONSE" | jq -r '.owner.login // ""')
  else
    VIS=$(echo "$RESPONSE" | grep -o '"visibility"[[:space:]]*:[[:space:]]*"[^"]*"' | sed 's/.*"\([^"]*\)"$/\1/')
    OWNER_LOGIN=$(echo "$RESPONSE" | grep -o '"login"[[:space:]]*:[[:space:]]*"[^"]*"' | sed 's/.*"\([^"]*\)"$/\1/')
    [ -z "$VIS" ] && VIS="UNKNOWN"
  fi

  cache_store "$url" "$VIS" "$OWNER_LOGIN" ""
  echo "$VIS|$OWNER_LOGIN|"
  return 0
}

OFFENDING=()
for i in "${!REMOTE_NAMES[@]}"; do
  NAME="${REMOTE_NAMES[$i]}"
  URL="${REMOTE_URLS[$i]}"
  RESULT=$(check_visibility "$URL")
  IFS='|' read -r VIS OWNER ERR <<<"$RESULT"
  if [ "$VIS" = "$ALLOWED_VISIBILITY" ] && [ "$OWNER" = "$ALLOWED_OWNER" ]; then
    continue
  fi
  REASON=""
  case "$ERR" in
    gh-not-installed) REASON="gh CLI is not installed — install from https://cli.github.com/" ;;
    gh-not-authed)    REASON="gh is not authenticated — run \`gh auth login\`" ;;
    non-github-url)   REASON="remote URL is not on github.com — gh cannot verify visibility" ;;
    api-error)        REASON="gh API call failed (network error, repo inaccessible, or rate-limit)" ;;
    "")
      if [ "$VIS" != "$ALLOWED_VISIBILITY" ]; then
        REASON="visibility=$VIS (must be $ALLOWED_VISIBILITY)"
      elif [ "$OWNER" != "$ALLOWED_OWNER" ]; then
        REASON="owner=${OWNER:-unknown} (must be $ALLOWED_OWNER)"
      else
        REASON="unknown failure"
      fi ;;
    *) REASON="$ERR" ;;
  esac
  OFFENDING+=("$NAME|$URL|$VIS|$OWNER|$REASON")
done

[ "${#OFFENDING[@]}" -eq 0 ] && exit 0

{
  echo "[CLAUDE-IP-GUARD] BLOCKED — \`.claude/\` cannot land on a public/non-${ALLOWED_OWNER} repo."
  echo ""
  echo "Command: $CMD"
  echo ""
  echo "Offending remote(s):"
  for entry in "${OFFENDING[@]}"; do
    IFS='|' read -r NAME URL VIS OWNER REASON <<<"$entry"
    echo "  - $NAME → $URL"
    if [ -n "$OWNER" ]; then
      echo "    visibility: $VIS, owner: $OWNER"
    else
      echo "    visibility: $VIS"
    fi
    echo "    reason: $REASON"
  done
  echo ""
  echo "Options to proceed:"
  echo "  1. Remove the offending remote: git remote remove <remote-name>"
  echo "  2. Move .claude/ work to a different feature branch in a different repo"
  echo "  3. If this remote is genuinely meant to receive .claude/ AND is private + ${ALLOWED_OWNER}:"
  echo "     run \`gh repo view <owner>/<repo> --json visibility,owner\` to verify"
  echo "     then re-run the original command — visibility cached for 60s"
  echo ""
  echo "See .claude/CONSTRAINTS.md §LAW — .CLAUDE WORKFLOW IP BOUNDARY for full text."
} >&2

exit 2
