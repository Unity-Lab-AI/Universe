# Builder Wiki — Schema & Rules

This is the **builder wiki** for the Universe program: the map of how it is built. The public pages for players and builders are `README.md` and `docs/`; this one is for us.

> The wiki is the map, the code is the territory. When they disagree, trust the code and fix the page.

## Page frontmatter (required on every page)

```yaml
---
title: <page title>
status: draft | partial | verified | stale
sources: [<paths or docs this page is based on>]
last-verified: <git commit hash of this repo, or "none (plan only)">
---
```

| Status | Meaning |
|---|---|
| `draft` | Describes the plan. No code exists yet, or the code isn't checked against this page |
| `partial` | Built in part, and what is built is checked against the code at `last-verified` |
| `verified` | Checked line by line against the code at `last-verified` |
| `stale` | Code changed since `last-verified`, or the plan was retired. Don't trust the page until it's re-checked |

## Writing rules

- **Rule 1, no dependencies.** Pages never describe using outside code, libraries, tools, compilers or data. The only outside things are Windows itself and the graphics card driver.
- **Rule 3, no making things up.** Every design fact traces to `docs/DESIGN.md` (or DNA-AND's, by entry number). Anything new and undecided goes to the creator as a multiple-choice question first.
- Short sections, tables. No text walls. No emoji in this project's pages.
- Quote the creator's words verbatim when a page rests on them.
- Universe is "the program", never "the game". Life is DNA-AND's.

## Upkeep

After any significant change:
1. Update every page the change touches (content + `last-verified`).
2. Keep `wiki/index.md` in sync.
3. Append an entry to `wiki/log.md`.
4. If a page can't be updated properly, set `status: stale`. Never leave it silently wrong.

## Work laws

These pages are the spec for the code (`docs/WORK-LAWS.md`, LAW 1 to 4). The code lives in the DNA-AND tree and is synced here; a page's `last-verified` is this repo's commit after the sync.

## Public vs internal (LAW 15)

This builder wiki is internal, so work notes belong here. The public pages (`README.md`, `CREDITS.md`, `docs/GUIDE.md`, `docs/HOW-TO-BUILD.md`, `docs/COMPARED.md`, `docs/LINUX.md`, `docs/in-game/`) never carry work notes: no law or task numbers, no creator quotes, no "decided" notes, no links to internal files.
