# Universe Work Laws

> "No dependancies!!!! WE DO NOT USE ANOYONE ELSE CODE< SCRIPTS< PROGERMAES< BINARIES <EXECULATEBLES< TANBLES< DATA< FILES< FOLDERS< DIRECTORES< OR COMPOLIERS" / "YOU do NOT add shit that you were not told to add, but you DO build out everything required to make what i say a reality." / "YOU do not make shit up!"

These laws are binding for every piece of Universe work: every code task, every doc change, every person, every AI session. They are DNA-AND's laws (`docs/WORK-LAWS.md` there), as they apply to this program on its own.

---

## The laws

### LAW 1 · The docs are the spec
Code is written from the docs: the builder wiki (`wiki/`), the public guides (`docs/GUIDE.md`, `docs/HOW-TO-BUILD.md`) and the decisions in `docs/DESIGN.md`. If it isn't in the docs, it doesn't get built.

### LAW 2 · Every task names its spec
Every task in `docs/TODO.md` names the pages it is built from (**Spec:**). Read every spec page in full before writing code for it.

### LAW 3 · Gaps get asked, never guessed
If the code needs a detail the docs don't give: ask the creator, plainly, as multiple choice; record the answer word for word in `docs/DESIGN.md`; update every affected page; only then write the code.

### LAW 4 · Code and docs move together
Any change in behaviour updates its docs in the same batch. When a system's code matches its wiki page, set `status: verified` and `last-verified: <commit>`. Docs and code never drift apart.

### LAW 5 · No dependencies
Everything is our own C# code, compiled by the C# compiler that ships with Windows ("Use C# built into Windows"). The only outside pieces: Windows itself (its compiler, runtime, Direct3D 12 and its shader compiler, Media Foundation for the MP4, the framework's Deflate for the PNG), and the graphics card driver. No packages, no libraries, no engines, no stored art, no outside tools in the build. On Linux, Wine runs the program; it is the user's, not ours.

### LAW 6 · Build only what was asked, and all of it
> "YOU do NOT add shit that you were not told to add, but you DO build out everything required to make what i say a reality."

### LAW 7 · Never make things up
> "YOU do not make shit up! you always use ask me question and you leave the tecnial bullshit out of it and talk plain english and to the point."

### LAW 8 · Templates, not names
Every kind of galaxy, star, system, world, body, mineral and law is an entry in `content/`; every value a range; nothing tested by name in code. The four rules of the toolkit (`docs/GUIDE.md`): templates not names, ranges not single values, seeds not storage, the graphics card first.

### LAW 9 · Words matter
- A star is a star. The name of our own star is never used in this project (there is none of it in this universe): it is "our own star". Masses, radii and brightness are in solar units (solar masses, solar radii, solar luminosities), the correct terminology for the use case.
- The range is the cosmic web of galaxies and the supermassive black holes at their hearts down to the quarks, and on below; never "galaxy to atom".
- Universe is "the program", never "the game"; life, gameplay and the interface are DNA-AND's.
- The creator's words are quoted exactly in DESIGN, TODO and FINALIZED.

### LAW 10 · Git flow
Work on a `feature/*` branch in the DNA-AND tree; its cascade is `feature/* → develop → main`. This project's `main` and `develop` are moved together on every release, and pushed to both `origin` (git.unityailab.com, over HTTPS) and `github`. Both repos are public, `.claude/` included (`.claude/.claude-public-ok`).

### LAW 11 · The sync, then the release, then the push
A change to the code or the content is made in the DNA-AND tree, then `universe-sync.cmd` brings it here (the source, the content as `.unt`, the installer, the Linux script, the public pages), then `release.cmd` here builds the program, the installer and the zip, then both repos are committed and pushed and the release assets replaced on both pages. The sync never touches `.claude/`, `wiki/`, these workflow files or this project's `.gitignore`.

### LAW 12 · Regression check before every push
Every link works; no stale number (sizes, line counts, the tour's length) and no wrong word (LAW 9); no work notes in public pages (LAW 15); the public pages and the in-game copies say the same thing.

### LAW 13 · Decisions stay in the main session
> "stop u dont outsource importatn decistions to a fuucking agent thhat knows nothing of the project!"
Research, reviews and doc audits are done by the assistant itself, never by subagents or scripts.

### LAW 14 · The creator tests
> "i SAID: next time ill install and test!"
Nothing is run by the assistant, ever. The creator installs and tests, and sends shots and clips; the assistant reads them and the zoom log (`%TEMP%\universe_zoom.log`).

### LAW 15 · Public pages carry no work notes
**Public:** `README.md`, `CREDITS.md`, `docs/GUIDE.md`, `docs/HOW-TO-BUILD.md`, `docs/COMPARED.md`, `docs/LINUX.md`, `docs/README.txt`, `docs/in-game/*`. They never carry round, law or task numbers, quotes of the creator, "decided" notes, notes about Claude, or links to internal files. **Internal:** `docs/DESIGN.md`, `docs/ROADMAP.md`, `docs/TODO.md`, `docs/FINALIZED.md`, `docs/DECOMPOSED.md`, this file, `wiki/`. They keep every work note, word for word.

### LAW 16 · Research first
Before writing content or docs for any topic, research it so nothing is left out (reliable sources: science agencies, encyclopedias, review papers); write our own entries from it; note the sources at the top of the content file (`# researched: …`), never in public pages. If research can't settle a fact, leave it out.

### LAW 17 · No tests, no runs: code it right, then the creator runs it
No test suites. Every build is `build.cmd chk` (the shader check) clean, with warnings as errors. The screen is judged at the creator's native resolution, from the creator's own shots and clips.

---

## Before writing any code

| | Check |
|:-:|---|
| 1 | The task is in `docs/TODO.md` (here and in DNA-AND's), with its **Spec:** pages |
| 2 | Every spec page is read in full, and its topic is researched (LAW 16) |
| 3 | Every detail the code needs is in those pages, or LAW 3 was followed |
| 4 | Nothing outside the project is used (LAW 5) |
| 5 | You are on a `feature/*` branch in the DNA-AND tree (LAW 10) |

## Before every push

| | Check |
|:-:|---|
| 1 | The docs match the code (LAW 4), here and in DNA-AND |
| 2 | The regression check passes (LAW 12), and the public pages have no work notes (LAW 15) |
| 3 | Finished tasks are in `docs/FINALIZED.md`, word for word |
| 4 | Synced, released, pushed to both remotes, the assets replaced (LAW 11) |
