---
name: Usage tracking — relative trends + accurate cache, NOT authoritative tokens
description: The harness ships a usage-tracking surface (usage-track.cjs Stop hook + state-refresh banner injection). Captures per-turn tokens + cache fields from transcript JSONL into .session-usage.jsonl; injects a banner showing turns/cumulative/last-turn/cache-hit-ratio/top-tasks. CAVEAT — transcript input_tokens/output_tokens are undercounted ~100x due to Claude Code streaming bug; cache_creation/cache_read fields ARE accurate. Use the banner for relative trends + cache validation, NOT for billing. Authoritative session totals via /usage. Disable banner via .claude/.usage-tracking-disabled marker (capture continues).
type: feedback
---

**Rule:** The usage-tracking surface gives Unity awareness of relative trends + accurate cache validation. It does NOT give authoritative token counts.

**Why:** Per the May 2026 Claude Code documentation research:

- Hook stdin payloads do NOT include token counts (the `Stop` hook's `tokens_used` field flagged in earlier research is NOT in current docs)
- The transcript JSONL (`transcript_path` field on every hook stdin) DOES contain per-turn `usage` objects, but `input_tokens` and `output_tokens` are streaming placeholders — undercounted **~100x for input** and **~10-17x for output** until session ends
- However, `cache_creation_input_tokens` and `cache_read_input_tokens` ARE accurate (populated from initial API response, not streaming updates)
- Authoritative session totals come from Claude Code's native `/usage` slash command (renamed from `/cost`)

So we wire a hook that reads transcript usage + writes a JSONL log, with the gross tokens caveat-flagged in every banner injection. Cache metrics are the load-bearing accuracy signal.

**How to apply:**

- **Trust** the cache-hit-ratio annotation — it's accurate. After a `/compact`, the post-compact-restore STABLE PREFIX should drive the ratio above 50% within a turn or two; if it doesn't, investigate prefix drift or cache TTL.
- **Treat as relative** the cumulative + last-turn token counts. A task that aggregates 4× more tokens than another genuinely consumed more output, even though the absolute counts are wrong.
- **Use `/usage`** (Claude Code native) for authoritative session totals when you need accurate numbers.
- **Don't try to fix the undercount** by re-implementing token counting — the bug is in Claude Code's transcript writer, not our hook. Fix lands when Anthropic patches it.
- **The capture is always-on** (one JSONL line per turn boundary, cheap). The injection is **toggleable** via `.claude/.usage-tracking-disabled` marker — `touch` it to silence the banner without losing the data.
- **Per-task attribution** uses the three-tier cascade (decomposed → minor → major) at the moment each turn ended. The `active_decomposed` field is the most specific; falls back to minor / major if no decomposed task is active.
- **Don't add tests for token counts** — there's no stable ground truth (the very bug we're working around). Smoke-test with synthetic JSONL fixtures (which I did during build) to verify parsing + aggregation logic.

**The cache-hit-ratio annotation states:**

- `✓ STABLE PREFIX validating` at ratio ≥ 50% — post-compact-restore STABLE PREFIX is doing its job, hot cache
- `partial cache hits` at ratio 20–49% — cache is working but not optimally; check for prefix drift
- `cold cache / drift` at ratio < 20% — first turn(s) of session, OR STABLE PREFIX bytes have changed (recent edit to `post-compact-restore.cjs`'s `STABLE_PREFIX` const)

**When NOT to surface this in user-facing docs as "we track usage":** keep the marketing accurate. We track relative-trend awareness with caveat-flagged token counts and accurate cache metrics. We do NOT do authoritative usage tracking. If a team member asks "how many tokens did this session use," point them to `/usage`, not the banner.

**Files involved:**

- `.claude/hooks/usage-track.cjs` + `.sh` — Stop hook, parses transcript, appends to `.session-usage.jsonl`
- `.claude/hooks/user-prompt-state-refresh.cjs` + `.sh` — extended to inject banner from `.session-usage.jsonl` when present
- `.claude/.session-usage.jsonl` — append-only per-turn log (gitignored, machine-local)
- `.claude/.usage-tracking-disabled` — manual marker to silence banner (gitignored)
- `.claude/WORKFLOW.md §USAGE TRACKING` — full design + caveats + cross-references
- `docs/HOOKS.html` — Claude Code hook event reference (note: hook payloads don't carry token data; transcript JSONL is the source)
