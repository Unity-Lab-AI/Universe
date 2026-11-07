---
name: Bundled atree fast scanner — scan engine ladder
description: `.claude/bin/atree[.exe]` is the canonical fast filesystem scanner the team ships with the template. Scanner agent uses it as the primary engine; fallback ladder is atree → tree → find → Glob. Map mode is the headline feature; A* pathfinder is the surgical bonus.
type: feedback
---

The Unity AI Lab `.claude/` template ships a native filesystem scanner binary at `.claude/bin/atree` (Linux x86_64) and `.claude/bin/atree.exe` (Windows x86_64). It is the team's canonical scan engine for `agents/scanner.md` Task 1. macOS is not bundled — the team does not run macOS.

**Why:** atree is ~2.6× faster than `tree` on real-size repos, ships with a bundled JSON Schema (Draft 7) for stable parser contracts, has built-in A* pathfinding for surgical file location, and produces consistent JSON across every team machine. Without it, scanner falls back to whatever combination of `tree`/`find`/Glob each machine happens to have — drift across the team.

**How to apply:**

- **Detection first.** Check `[ -x .claude/bin/atree ]` (Linux/native bash) or `[ -x .claude/bin/atree.exe ]` (Windows Git-Bash). If neither, drop to fallback tier.
- **Map mode is the headline use case.** `.claude/bin/atree --root . --tree --no-limit --include-files --json --no-color 2>/dev/null` for a full directory dump. The `--tree --no-limit -f` flag combo means "skip per-file stat for max scan speed, no caps, include files as leaves" — the user explicitly framed this as "less about pathfinding and more so just a fast tree."
- **A\* mode is the surgical bonus.** `.claude/bin/atree -r . -s <start> -g <goal> -f --no-color` returns the optimal navigation path between two known nodes in JSON when combined with `--json`. Use this when you have specific start + goal nodes; do NOT use it for general directory mapping.
- **Status to stderr, data to stdout.** Pipe `2>/dev/null` to suppress info banners when consuming JSON. The binary is fully pipeable.
- **Fallback ladder is canonical.** If atree fails (missing, non-executable, non-zero exit, malformed output, unknown schema_version): `tree -a -J` → `find . [excludes]` → Claude `Glob` tool. Always record which engine ran in `scan_results.engine_used`. Never silently succeed with an empty scan.
- **Schema is bundled in the binary.** `--print-schema` outputs the full JSON Schema Draft-07 — pin `schema_version: 1` in consumers to detect breaking format changes. No external schema file to ship.
- **Don't reinvent the scan.** Scanner's Task 1 should always go through the fast scan engine ladder before any ad-hoc Glob/Bash filesystem walking. The ladder exists so per-team-member tool drift never becomes the bottleneck.

Cross-references:
- Full inventory + fallback detection pattern + how to add new tools: `.claude/WORKFLOW.md §BUNDLED TOOLS`
- Scanner integration spec: `.claude/agents/scanner.md §Task 1: File System Scan` (FAST SCAN ENGINE LADDER table)
- Quick reference: `.claude/CLAUDE.md §BUNDLED BINARY TOOLS`
