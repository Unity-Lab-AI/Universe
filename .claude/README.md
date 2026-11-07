# `{{PROJECT_NAME}}`

> **TEMPLATE** — Replace this entire file with your project's actual README, or use the skeleton below as a starting point. The `.claude/` folder gives every project the same workflow baseline (persona system, hook harness, 3-tier task cascade, etc.); your project README is for YOUR project's purpose, not the .claude/ template's internals.
>
> For the comprehensive `.claude/` workflow internals (what every agent does, every skill, every hook, every LAW, the full layout, install/update flow, etc.) see **`.claude/.claudereadme.md`** — that's the maintainer-facing reference, intentionally separated from this project-facing README so it doesn't conflict with whatever you write here.

---

## What is `{{PROJECT_NAME}}`?

`{{ONE_LINE_PROJECT_DESCRIPTION}}`

{{Longer paragraph explaining what your project does, who it's for, and the value it delivers. Replace this block.}}

---

## Quick start

```bash
# Clone (or however your project is acquired)
git clone {{REPO_URL}}
cd {{PROJECT_DIR}}

# {{INSTALL_STEPS}}

# {{RUN_STEPS}}
```

---

## Usage

{{Document the main user-facing flows. Replace this section.}}

---

## Development

{{Document how contributors run the project locally, run tests (if any), and submit changes. Replace this section.}}

If you're using the bundled `.claude/` workflow in this repo:

```bash
# Linux / macOS / Git Bash
./.claude/start.sh

# Windows
.\.claude\start.bat
```

That launches Claude Code with the persona + workflow chain. See `.claude/.claudereadme.md` for the full breakdown of what the workflow does and how to customize it for your team.

---

## Project layout

```
{{PROJECT_NAME}}/
├── README.md              ← (this file — your project README)
├── .claude/               ← workflow template — see .claude/.claudereadme.md
├── docs/                  ← {{describe your docs convention}}
├── {{SRC_DIR}}/           ← {{describe your source layout}}
└── {{OTHER_DIRS}}
```

---

## Documentation

- **Project docs:** {{POINT_TO_YOUR_DOCS_FOLDER_OR_SITE}}
- **`.claude/` workflow:** see `.claude/.claudereadme.md` for the comprehensive reference

---

## License

{{LICENSE_NAME}} — see `LICENSE` file for full text.

---

## Contributing

{{CONTRIBUTING_GUIDELINES — link to CONTRIBUTING.md if applicable, or inline the basics}}

---

*This README is the project template that ships with `.claude/`. Replace the `{{PLACEHOLDERS}}` with your project's actual info, or delete this entire file and write your own. The `.claude/` workflow internals are documented separately at `.claude/.claudereadme.md`.*
