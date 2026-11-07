---
name: Cross-platform case insensitivity — enforce Windows/macOS rules on Linux too
description: File/folder paths are CASE-INSENSITIVE on every platform — even on Linux where the filesystem allows case-distinct names. Two paths differing only by case (apple.md vs Apple.md) are the SAME PATH cross-platform. Never create conflicts. Never rely on case to distinguish files. Pick ONE canonical casing and stick with it. Use two-step rename ceremony for case-only changes. References in code/docs/imports must match on-disk casing exactly. Cross-platform team — enforce the lowest-common-denominator (Windows/macOS rules) even on Linux. Pre-create grep check; pre-commit duplicate-detection. Failure recovery: identify case-variants, pick canonical, git rm non-canonical, commit cleanup.
type: feedback
---

**Rule:** Treat file and folder paths as CASE-INSENSITIVE on every platform — even on Linux, where the filesystem allows case-distinct names. Pick ONE canonical casing per name and stick with it. References in code/docs/imports/settings must match on-disk casing byte-for-byte.

**Why:** Sponge's verbatim ask (2026-05-08): *"for the development workflow, commits, creation of folders and files, we need it ALL to follow a convention that is for WINDOWS. Because during development, git and github normally supports different folders like Apple, apple, aPPle, and what not, just like linux would allow, but, to keep thins cross platform we should ALWAYS assume case insensitivity, even on linux. Even though linux has case sensitivity, windows does NOT, and will treat apple, Apple, aPPle, all as the same thing. so we need to enforce case insensitivity when working on projects in linux, EVEN THOUGH LINUX WILL ALLOW IT."*

The cross-platform breakage matrix:
- Windows filesystems (NTFS/ReFS/FAT32/exFAT) default case-INSENSITIVE
- macOS default filesystems (HFS+/APFS) case-INSENSITIVE-but-PRESERVING
- Linux ext4/btrfs/xfs/zfs case-SENSITIVE
- Git defaults: Linux `core.ignorecase=false`, Windows/macOS `=true`

A Linux dev who creates two case-distinct files in the same directory ships a working tree that PARTIALLY CORRUPTS the moment a Windows or macOS teammate runs `git pull` — silent overwrites or partial-merge breakage. The team is cross-platform (Sponge on Linux; others on Windows), so this rule eliminates the bug class entirely by forcing the lowest-common-denominator on the most-permissive platform.

**Forbidden:**

- Creating two files/folders in the same parent whose names differ only by case (`apple.md` + `Apple.md`, `Components/` + `components/`)
- Single-step renaming `foo.md` → `Foo.md` (silently ignored on case-insensitive filesystems)
- Importing/referencing files via different casing than on-disk (`import './Components/foo'` when folder is `components/`)
- Letting `git status` show two pending files differing only by case
- Writing path strings in hooks/settings/launchers/docs that don't match on-disk casing exactly
- Setting `git config core.ignorecase true` on Linux without understanding the implication (silently merges case-distinct paths)

**Required:**

- **Pick canonical casing first.** Default: lowercase-with-hyphens (`my-feature.md`, `unity-girlfriend.md`, `feedback_harness_layer.md`). Top-level workflow files use established UPPERCASE (`README.md`, `CLAUDE.md`, `CONSTRAINTS.md`, `WORKFLOW.md`, `TODO.md`, `FINALIZED.md`). Folders: lowercase (`agents/`, `commands/`, `hooks/`).
- **Pre-create grep check** on Linux:
  ```bash
  ls <target-dir> | grep -i '^<target-name>$'
  # Empty = safe; non-empty = collision, rename or rethink
  ```
- **Two-step case-only rename ceremony** when intending to change a file's case:
  ```bash
  git mv apple.md apple.md.tmp
  git commit -m "rename apple.md (step 1)"
  git mv apple.md.tmp Apple.md
  git commit -m "rename to Apple.md (step 2)"
  ```
- **Match on-disk casing exactly** in references — imports, require(), file paths in docs, hook command paths, settings.json keys, slash command activation paths, etc.
- **Pre-commit duplicate detection:**
  ```bash
  # Block commits with case-collisions
  git status --porcelain | awk '{print $2}' | sort -f | uniq -i -d
  # Empty output = no case-variants pending; non-empty = block commit
  ```

**Naming convention defaults:**

- Top-of-repo workflow files: `README.md`, `CLAUDE.md`, `CONSTRAINTS.md`, `WORKFLOW.md`, `LICENSE`, `CONTRIBUTING.md`, `CHANGELOG.md` (UPPERCASE — convention)
- `docs/` workflow files: `TODO.md`, `FINALIZED.md`, `ROADMAP.md`, `DECOMPOSED.md`, `ARCHITECTURE.md`, `SKILL_TREE.md` (UPPERCASE — workflow convention)
- Source files / agents / commands / hooks / memory templates: lowercase-with-hyphens or lowercase_with_underscores (template convention is mixed historically; pick one per directory and stick with it)
- Folders: all lowercase (`agents/`, `commands/`, `hooks/`, `memory-templates/`, `bin/`)
- Persona body filenames inside persona text (`Unity_Accessibility.js` etc.): lore-strings, NOT real files, do not normalize
- Generated artifacts / machine-local state: lowercase with leading dot (`.session-state.md`, `.session-tidbits.md`, `.session-usage.jsonl`, `.last-session.md`, `.yolo-mode`)

When in doubt: **lowercase-with-hyphens**. Never SHOUTYCAS_DASHES or MixedCase_Snake_Underscores.

**How to apply:**

- Before creating any new file: grep the target directory case-insensitively for the target name. If a variant exists, either canonicalize the existing or rename the new.
- Before committing: scan `git status` for case-only collisions. Resolve before commit.
- When citing a file in docs / code comments / commit messages: copy the casing from `ls` exactly. Don't trust memory.
- When the team starts a new project from this template: configure `git config core.ignorecase false` so git surfaces case-only renames cleanly. Document this in the project's `CONTRIBUTING.md` or `.gitattributes`.
- If a teammate's PR introduces a case-collision (caught at review time): block the merge until they fix the casing AND any references that use the wrong case.

**Failure recovery (when a case-collision lands):**

1. STOP. Acknowledge the violation.
2. Inspect: `git ls-files | sort -f | uniq -i -d`
3. Pick the canonical casing (typically the older / more-referenced variant)
4. `git rm` the non-canonical variant
5. Verify code/doc/import references use the canonical casing
6. Commit the cleanup with a clear message ("resolve case-collision: keep apple.md, drop Apple.md")
7. Update `docs/FINALIZED.md` with a recovery note so the audit trail captures the cross-platform breakage

**Cross-references:**

- Full LAW body: `.claude/CONSTRAINTS.md §CROSS-PLATFORM CASE INSENSITIVITY`
- LAW one-liner: `.claude/CLAUDE.md` LAW INDEX
- Companion: `feedback_no_appdata_term.md` (cross-platform terminology hygiene — same spirit)
