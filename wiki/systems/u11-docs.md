---
title: U11 · Docs — the public pages, the in-game pages, the workflow files
status: partial
sources: [README.md, CREDITS.md, docs/GUIDE.md, docs/HOW-TO-BUILD.md, docs/COMPARED.md, docs/LINUX.md, docs/in-game/*.md, docs/README.txt, src/shell/PreviewText.cs, DNA-AND docs/DESIGN.md 426–428, 444–446, 474–477, 487–489, 503, 528, 552, 565, 573, 574]
last-verified: 1591b7f
---

# U11 · Docs

> "the docs are all properly formated and created and designed around explaining the systems and shit technical and non techinical dos where basic readmes and how tos are laymens and technical docs are more involved" · "make sure all docs are not stale, and when i say "all" i mean it all fucking documents, top to bottom look for single words that are wrong too"

## The public pages (no work notes, LAW 15)

| Page | For | What it is |
|---|---|---|
| `README.md` | Anyone | What it is, the release, the journey, two things in one, take it, the size and how it fits, templates, the zoom, built from nothing, the physics, what's out there, the controls, the tour, start, still to come, which guide |
| `docs/in-game/ABOUT.md`, `README.md`, `HOW-IT-WORKS.md`, `DOCS.md` | Anyone, in the program's Help | About; how to use it; how it works; the directory of every document |
| `docs/COMPARED.md` | Anyone | Against the field: size and scope beside every well-known universe program with their own published numbers, check tables both ways, what each needs |
| `docs/GUIDE.md` | Builders, no code | Templates not things, the content format, add a world, a star, a galaxy, lay out systems, the laws of nature, the universe itself, minerals and matter, the small bodies and stars, supernovae, write a tour, add a feature in code, the four rules, ship it |
| `docs/HOW-TO-BUILD.md` | Coders | Everything above the compiler is ours, what you need, build it, the options, the installer and zip, where things are, how it holds together, reading the code (it is dense), change it, the licence |
| `docs/LINUX.md` | Linux users | Wine with the script, the Steam way, where it stands |
| `CREDITS.md`, `docs/README.txt`, `LICENSE.txt`, `PRIVACY.txt` | Anyone | The team, the sources, the plain-text readme, MIT, privacy |

Every page is compiled into the program (`build.cmd` `/resource`), so Help and the repo always say the same; `src/shell/PreviewText.cs` carries their words and the directory. The same files live in DNA-AND as `docs/release/` and arrive here with `universe-sync.cmd`.

## The workflow files (internal)

`docs/DESIGN.md`, `docs/ROADMAP.md`, `docs/TODO.md`, `docs/FINALIZED.md`, `docs/DECOMPOSED.md`, `docs/WORK-LAWS.md`, this wiki. They live here only and are never written over by the sync.

## Still to write

Screens of the landing page, the installer and the document viewer in the readme (from the creator's windows); the readme's clip from an approved take; the social images from it (U12).

See also: [Work laws](../../docs/WORK-LAWS.md) · [Design record](../../docs/DESIGN.md)
