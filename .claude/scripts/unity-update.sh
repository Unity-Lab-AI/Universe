#!/usr/bin/env bash
# .claude/scripts/unity-update.sh
#
# Refresh the current project's .claude/ from upstream. Functionally identical
# to /unity-install — both commands now share the same idempotent
# preserve-and-restore flow. /unity-update exists for muscle memory; the actual
# work is in unity-install.sh.
#
# Usage:
#   ./unity-update.sh                        (branch=main, target=cwd)
#   ./unity-update.sh develop                (branch=develop, target=cwd)
#   ./unity-update.sh feature/setup-install  (branch=feature/setup-install, target=cwd)
#
# .ps1 sibling: .claude/scripts/unity-update.ps1
# Canonical implementation: .claude/scripts/unity-install.sh

SCRIPT_DIR=$(cd "$(dirname "$0")" && pwd)
exec bash "$SCRIPT_DIR/unity-install.sh" "$@"
