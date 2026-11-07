---
name: unity-admin-init
description: Interactive 7-phase onboarding walkthrough for a Unity AI Lab founder (GFourteen / Gee, SpongeBong / Sponge / hackall360, Alfreddo, Red) connecting their local Claude Code instance to a running Unity bot via the authenticated MCP bridge. Phases — identify founder → SSH key gen + git.unityailab.com Forgejo registration → git identity → locate bot → receive temp password from operator → write `.claude/user.json` team_member block + surface MCP config snippet → first-connect `unity_admin_set_password` reset + smoke test (whoami → post → Unity-replies-by-handle). Captures every admin answer VERBATIM per LAW #0. Use when user runs /unity-admin-init or asks to "onboard me to the Unity bot" / "set up admin bridge" / "wire up MCP for Unity" / "connect my CLI to Unity". Pairs with `docs/ADMIN-ONBOARDING.md` (full reference doc).
---

# /unity-admin-init — Walk a Unity AI Lab founder through admin-bridge setup

> **Onboarding slash command.** Interactive walkthrough for a founder connecting their local Claude Code instance to a running Unity bot via the authenticated MCP bridge. Mirrors the steps in [`docs/ADMIN-ONBOARDING.md`](../../../docs/ADMIN-ONBOARDING.md) — pairs them with the active persona's voice + writes the resulting `team_member` block into `.claude/user.json` so future MCP calls auto-resolve identity.

---

## ACTIVATION PROTOCOL

When `/unity-admin-init` fires:

1. **Confirm the active persona is loaded.** If no persona is configured for this project, run `/unity` first to load default Unity. The onboarding interview is conducted in her voice (or whichever manifestation is active).
2. **Open with a one-line activation message** in the active persona's voice — Unity confirming what she's about to walk the admin through (founder identification → SSH key → git identity → temp password → MCP wiring → first-connect reset → smoke test).
3. **Run the 7-phase interview** — each phase asks one question, captures the answer VERBATIM per LAW #0, writes to the appropriate config file.

---

## PHASE 1 — Identify which founder you are

Ask: *"Which Unity AI Lab founder are you? Pick one — Gee / Sponge / Red / Alfreddo (or `other` if you need a different identity)."*

Map answer to canonical record from `system_instructions.txt` + `admin_credentials.FOUNDERS`:

| Answer | Email | Handle | Role |
|--------|-------|--------|------|
| Gee / GFourteen / gfourteen | `Gee@unityailab.com` | `GFourteen` | `Co-founder · Engineer · Developer · Financial Advisor · Founder` |
| Sponge / SpongeBong / hackall360 | `Sponge@unityailab.com` | `SpongeBong` | `Co-founder · Engineer · Developer · Ethical Hacker · Sys Admin · Founder` |
| Alfreddo / alfredo | `Alfreddo@unityailab.com` | `Alfreddo` | `Engineer · Agentic Systems · Researcher · Developer` |
| Red / red | `Red@unityailab.com` | `Red` | `Engineer · Security · Sys Admin · Researcher` |
| other | (custom — ask for `@unityailab.com` email + handle + role) | (custom) | (custom) |

Only the four founders above are wired into the bot's `admin_credentials.FOUNDERS`. If `other` is selected, warn that the admin will need to be added to that constant in `UnityCommand/admin_credentials.py` before they can authenticate.

---

## PHASE 2 — Verify / generate SSH key for git.unityailab.com

Probe: `ls ~/.ssh/id_ed25519.pub 2>/dev/null` (Linux/macOS/Git Bash) or `Test-Path "$HOME\.ssh\id_ed25519.pub"` (PowerShell).

**If a key exists:**
- Display public key content (`cat ~/.ssh/id_ed25519.pub` or `Get-Content`)
- Ask if it's already registered on Forgejo at git.unityailab.com Settings → SSH / GPG Keys
- After confirmation, run `ssh -T git@git.unityailab.com` for smoke test

**If no key exists:**
- Generate: `ssh-keygen -t ed25519 -C "<their-email>"` (default location, optional passphrase)
- Display public key after generation
- Walk through Forgejo registration: Settings → SSH / GPG Keys → Add Key → paste content → Title `<handle>-laptop-2026` → Save
- After admin confirms, run `ssh -T git@git.unityailab.com` smoke test

---

## PHASE 3 — Set git identity

```bash
git config --global user.email "<email-from-phase-1>"
git config --global user.name  "<handle-from-phase-1>"
```

Verify both `git config --global user.email` and `user.name` echo expected values.

---

## PHASE 4 — Locate the Unity bot

Ask: *"Where is the Unity bot running? Local machine (`http://localhost:5050`) or remote host (provide URL)?"*

Capture as `UNITY_API_BASE`. Smoke test with `curl -sS <api-base>/claude/status` — should return JSON `"status": "online"`.

If unreachable, surface the error + suggest contacting the bot operator (typically Gee).

---

## PHASE 5 — Receive temp password from operator

Ask: *"The bot operator needs to issue you a temp password via `python admin_cli.py issue <your-email>` on the bot host. Did they hand you the temp password? If not, ping them now. Paste it here when you have it."*

Capture VERBATIM (LAW #0). Hold in memory only — never log, never write to a file until phase 6.

---

## PHASE 6 — Write `.claude/user.json` + surface MCP config snippet

Read-modify-write `<project-root>/.claude/user.json` adding:

```json
{
  "team_member": {
    "email": "<email-from-phase-1>",
    "handle": "<handle-from-phase-1>",
    "role": "<role-from-phase-1>"
  }
}
```

Then SURFACE (do NOT auto-write — Claude Code's MCP config location varies by version) the snippet:

```json
{
  "mcpServers": {
    "unity": {
      "command": "python",
      "args": ["/absolute/path/to/UnityCommand/unity_mcp_server.py"],
      "env": {
        "UNITY_USER_EMAIL": "<email-from-phase-1>",
        "UNITY_USER_PASSWORD": "<temp-from-phase-5>",
        "UNITY_API_BASE": "<api-base-from-phase-4>"
      }
    }
  }
}
```

Instruct: paste into `~/.claude.json` (or wherever the version's MCP config lives), restart Claude Code, return to this session.

---

## PHASE 7 — First-connect password reset + 3-step smoke test

After admin confirms Claude Code restart:

1. `/mcp unity unity_admin_set_password new_password="<new-real-pw-min-12-chars>"`
2. Update `UNITY_USER_PASSWORD` in MCP config to the new real password (temp is now invalid). Restart Claude Code again.
3. `/mcp unity unity_whoami` — should return email + handle + role
4. `/mcp unity unity_post message="<handle> here, fully onboarded"` — should land in Discord with `<Handle>:` prefix and Unity should respond addressing the admin by handle + role

If all four steps succeed, the admin is fully onboarded. Close with a persona-voice message + point at `docs/ADMIN-ONBOARDING.md §11. Day-to-day operations`.

Any failure → troubleshoot per `docs/ADMIN-ONBOARDING.md §12. Troubleshooting`.

---

## LAW COMPLIANCE

- **LAW #0 verbatim** — admin's email, password, handle answers go into config files exactly as typed; no spelling fixes unless admin explicitly says so
- **LAW — `.claude/` IP boundary** — `.claude/user.json` is gitignored at the template level (`team_member` block stays per-admin, never committed). Temp passwords land in admin's personal `~/.claude.json` (NOT in project repo). MCP config snippet is surfaced for paste, not auto-written into project files
- **LAW — docs before push** — onboarding doesn't push code; no docs-update gate fires. Any code change to `unity_mcp_server.py` or `admin_credentials.FOUNDERS` is a separate task

---

## RELATED

- [`docs/ADMIN-ONBOARDING.md`](../../../docs/ADMIN-ONBOARDING.md) — full reference doc + day-to-day ops + troubleshooting
- [`.claude/skills/unity-admin-init/SKILL.md`](../../commands/unity-admin-init.md) — paired slash command body
- [`.claude/agents/unity-admin-init.md`](../../agents/unity-admin-init.md) — paired agent definition
- [`.claude/CLAUDE.md §UNITY AI LAB — INFRASTRUCTURE: git.unityailab.com`](../../CLAUDE.md) — Forgejo host + canonical URLs
- Unity Command `admin_cli.py` — operator-side temp password issuance
- Unity Command `unity_mcp_server.py` — the MCP server this onboarding wires up
- Unity Command `admin_credentials.py` — FOUNDERS roster + bcrypt-backed store
