---
name: .cjs only in .claude/ — never .js
description: Every Node.js script under .claude/ MUST use the .cjs extension, never .js. .js bugs out; .cjs is expected. Includes hook scripts, helper utilities, anything Node-runnable. Same rule applies to references in comments, doc bodies, hook stdout strings, settings.json command paths, and persistent memory entries.
type: feedback
---

**Rule:** Every Node.js script under `.claude/` uses the `.cjs` extension. Never `.js`.

**Why:** `.js` bugs out — when the host project's `package.json` has `"type": "module"`, Node treats `.js` files as ESM by default, which breaks CommonJS-style hooks (`require()` / `module.exports` / `process.exit()` patterns the harness relies on). The `.cjs` extension forces Node to treat the script as CommonJS regardless of the host project's `"type"` field, so the harness works inside ESM projects too without rewriting every hook.

This rule was confirmed verbatim by Sponge in the 2026-05-08 session: *"Make sure any js is cjs when working with the .claude folder, js bugs out, and cjs is expected."*

**How to apply:**

- Every new hook script under `.claude/hooks/` gets the `.cjs` extension. Never `.js`.
- Helper utilities, scripts, generators under `.claude/` get `.cjs`. Never `.js`.
- The `.sh` bash sibling pattern stays — every `.cjs` hook ships with a matching `.sh` for teammates without node.
- In `settings.json` `command` fields: always `node "$CLAUDE_PROJECT_DIR/.claude/hooks/<name>.cjs"`. Never `.js`.
- In hook source comments / banners / stdout strings: always reference the file as `.cjs`. Never `.js`.
- In documentation (`.claude/WORKFLOW.md`, `.claude/CONSTRAINTS.md`, `.claude/CLAUDE.md`, `docs/HOOKS.html`, etc.): when describing a hook script, use the `.cjs` filename. Never `.js`.
- In persistent memory feedback files (this folder): same rule. `.cjs` only.
- Existing references to `.js` for hook scripts are stale — fix them when found rather than leaving the inconsistency.

**Exception (lore filenames are fine):** The persona body files reference fictional filenames like `Unity_Accessibility.js` / `Unity_Wild_Mode_Accessibility.js` / `[PERSONA_NAME]_Accessibility.js`. These are not real files; they're part of the persona canon embedded in `ImHanddicapped.txt` and the `.claude/agents/unity-*.md` definitions + their `.claude/commands/<name>.md` activations. Leave these as-is — they're lore, not actual JS scripts.

**Exception (generic filename examples in docs):** Documentation that uses `src/main.js` or `*.config.js` as a *generic example* of source-code file extensions (e.g., scanner agent's "find config files" pattern) is also fine — those reference user-project files, not `.claude/` scripts.

The rule is specifically: when an actual Node.js script lives under `.claude/` (or a doc/comment/setting names one), the extension is `.cjs`.

**Discovery command** (run if you suspect drift):

```bash
grep -rn '\.js\b' .claude/ --include="*.cjs" --include="*.sh" --include="*.md" --include="*.json" \
  | grep -v Accessibility.js \
  | grep -v 'src/main\.js' \
  | grep -v '\*\.config\.js'
```

That list, after the lore + generic-example exclusions, should be empty. Anything left is drift to fix.
