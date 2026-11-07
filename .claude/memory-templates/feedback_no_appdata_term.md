---
name: Cross-platform terminology — no "appdata" reference (Windows-specific term)
description: The Claude Code project memory folder lives at ~/.claude/projects/<encoded>/memory/ on EVERY platform. ~ resolves to $HOME on Linux/macOS and %USERPROFILE% on Windows — NOT %APPDATA% (that's Roaming app data, a different folder). Refer to it as "the Claude Code project memory folder" or "the user-profile memory folder" or just "the memory folder", never "appdata folder" or "appdata project memory" — those are Windows-specific terms that mislead Linux/macOS team members AND are technically wrong even on Windows (the path doesn't go through %APPDATA%). Code identifiers: memoryDir / MEMORY_DIR / memory_dir / memory_synced_count / encodeProjectPathForMemory.
type: feedback
---

**Rule:** Never use "appdata" to describe Claude Code's project memory folder. The path lives at `~/.claude/projects/<encoded>/memory/` on every platform. Use OS-neutral terminology in docs, comments, variable names, and JSON keys.

**Why:** Sponge's verbatim ask (2026-05-08): *"I keep seeing 'appdata' refferenced, while this is awesome for windows, it isnt a thing on linux based operating systems, and we have people using this .claude with windows AND with linux, currently I am on linux. So we need to clear up the discrepency to only use windows things for windows and only linux things for linux."*

Two failure modes the term creates:

1. **Linux/macOS team members** see "appdata" and have no idea what it means — there's no equivalent folder concept on those platforms. The closest analogues are `~/.config/` (XDG Base Directory) or `~/.local/share/` — but Claude Code uses neither; it puts the memory folder under `~/.claude/` directly.
2. **Even on Windows**, the term is technically WRONG. `%APPDATA%` resolves to `C:\Users\<user>\AppData\Roaming\` (the Roaming app-data folder). Claude Code's project memory lives at `%USERPROFILE%\.claude\projects\...` — that's the user-profile root, NOT the AppData/Roaming subfolder. So calling it "appdata" misleads Windows team members too.

The actual path resolution:
- Linux: `$HOME/.claude/projects/<encoded>/memory/` (typically `/home/<user>/.claude/...`)
- macOS: `$HOME/.claude/projects/<encoded>/memory/` (typically `/Users/<user>/.claude/...`)
- Windows: `%USERPROFILE%/.claude/projects/<encoded>/memory/` (typically `C:\Users\<user>\.claude\...`)

The `~` shorthand resolves correctly to all three. The path is OS-neutral by design.

**Required terminology in docs / comments / strings:**

- ✅ "Claude Code project memory folder" (formal first reference)
- ✅ "the memory folder" (subsequent references in same passage)
- ✅ "the user-profile memory folder" (when emphasizing it's under the user's home)
- ✅ "`~/.claude/projects/<encoded>/memory/`" (when the literal path matters)

**Forbidden in docs / comments / strings:**

- ❌ "appdata"
- ❌ "appdata folder"
- ❌ "appdata project memory folder"
- ❌ "your appdata"
- ❌ "the appdata MEMORY.md"
- ❌ "%APPDATA%" (unless literally referring to the Roaming app-data folder, which we don't use)

**Required code identifiers:**

When naming variables, function parameters, JSON keys, environment variable names, etc. that refer to the project memory folder:

- ✅ `memoryDir` / `MEMORY_DIR` (variable / constant)
- ✅ `memory_dir` (JSON key)
- ✅ `memory_synced_count` (JSON key)
- ✅ `encodeProjectPathForMemory()` (function name)
- ✅ `claudeMemoryDir` (when disambiguation is needed)
- ✅ `projectMemoryFolder` (descriptive variant)

**Forbidden code identifiers:**

- ❌ `appdataDir` / `APPDATA_DIR`
- ❌ `appdata_dir` / `appdata_synced_count`
- ❌ `encodePathForAppdata()`

**How to apply:**

- When writing documentation that references the memory folder, default to "Claude Code project memory folder" on first reference, "memory folder" on subsequent.
- When writing hook source, use `memoryDir` / `MEMORY_DIR` / `memory_dir` etc. in code and "Claude Code project memory folder" or "user-profile memory folder" in comments.
- When writing the bash launcher (`start.sh`) or batch launcher (`start.bat`), comments use "Claude Code project memory folder" or "user-profile memory folder" — same on both, since the launcher computes the same path on both platforms.
- When discussing the launcher's encoding scheme (replacing `:`, `/`, `\`, `.`, ` `, `(`, `)` with `-`), call it "Claude Code's project-path encoding" or "the memory folder encoding scheme" — never "the appdata encoding."
- If you find a stale "appdata" reference in any file, fix it inline rather than leaving the inconsistency.

**Discovery command** (for finding stale references):

```bash
grep -rn -i 'appdata' .claude/ docs/ \
  --include="*.md" --include="*.cjs" --include="*.sh" \
  --include="*.json" --include="*.html" --include="*.bat"
```

The result, after fix-cleanup, should be empty (or only contain explicit comparison/explanation notes about what `%APPDATA%` actually is and why we don't use it).

**Companion rule:** `feedback_case_insensitivity.md` — same spirit (cross-platform discipline), different surface (file casing instead of terminology).

**Cross-references:**

- LAW companion: `feedback_case_insensitivity.md`
- Discovery grep documented in this memory's body above
- Hooks now using OS-neutral identifiers: `.claude/hooks/post-tool-memory-sync.cjs`, `.claude/hooks/post-tool-memory-sync.sh`, `.claude/hooks/session-start-env-dump.cjs`
