#!/bin/bash
# Unity AI Lab — Claude Code Status Line
#
# Renders a centered multi-line strip at the bottom of the Claude Code CLI:
#   Line 1: [Label] | up Xh · think Ym | Model | git-branch[*+]
#   Line 2: CTX [bar] N% | <reset-countdown> [bar] N% | <reset-countdown> [bar] N%
#   Line 3: cpu N% ×cores | ram U/T G | <drive-label> N G free
#   Line 4: <gpu-name> U/T gb util% (or red "No GPU Detected")
#
# Label rules (auto-adapt to /rename):
#   - If `session_name` is present in the statusline JSON (set via Claude
#     Code's /rename slash command), display that as the label.
#   - Otherwise fall back to `basename(cwd)` so unnamed sessions keep the
#     classic project-folder identity.
#
# Label color rules (auto-vary per session):
#   - When a rename has been applied, the bracket color is picked by
#     hashing `session_id` against a 12-color ANSI palette. Stable for
#     the lifetime of one session, distinct across concurrent sessions.
#   - When no rename is set, color stays brand magenta (35) — the
#     original look. Anthropic's /color slash command does NOT persist
#     anywhere readable to the statusline (verified against live JSON +
#     ~/.claude.json + ~/.claude/sessions/*.json), so we can't sync to
#     it directly; the hash gives an auto-distinct alternative.
#
# How it works (per docs/STATUSLINE.md):
#   1. Claude Code spawns this script as a subprocess each time the
#      status line refreshes.
#   2. Pipes a JSON blob to stdin: { context_window: { used_percentage },
#      model: { display_name }, session_name, session_id, rate_limits,
#      cost, ... } — full schema captured + documented in §10 of the doc.
#   3. We parse, format, center, and print the result to stdout. Claude
#      Code renders it as the status line text. ANSI color escapes honored.
#
# Side effect (per spec section 3): writes the current context % to
# ~/.claude/context_pct.txt so hooks / watchdogs / external tooling can
# `cat` that file to know how full the context is without querying
# Claude Code directly. This is the bridge for auto-compaction warnings
# or memory-backlog routines.
#
# Wired in: .claude/settings.json → statusLine.command
# Project-agnostic: auto-detects fallback label from cwd basename, so
# the same script drops into any Unity AI Lab project without per-
# project configuration.

input=$(cat)

# Project name = basename of cwd. Claude Code's cwd is the project root
# (where .claude/ lives), so basename gives "Dream", "Website2.0", etc.
# Fallback through pwd -W (Git Bash on Windows reports W-style path).
PROJECT_NAME=$(basename "$(pwd -W 2>/dev/null || pwd)")

# Pick python launcher: prefer python3 (Linux/macOS), fall back to py -3 (Windows).
# `py` is the Windows Python launcher and doesn't exist on Linux/macOS, so the
# previous hardcoded `py -3` silently broke the statusline for any team member
# not on Windows. python3 is the POSIX-standard name; py -3 is the Windows path.
PYBIN=$(command -v python3 >/dev/null 2>&1 && echo python3 || echo "py -3")

# Git branch + state-aware coloring (line 1, after model). Computed in bash
# because git shell calls are quick (~5-20ms each) and avoid round-tripping
# through Python. GIT_PART stays empty in non-git directories so the segment
# self-omits cleanly.
#
# Branch-type colors (when clean + pushed):
#   main / master  -> blue   #3b82f6  rgb(59,130,246)   Tailwind blue-500
#   develop        -> purple #a855f7  rgb(168,85,247)   Tailwind purple-500
#   feature/* etc. -> green  #22c55e  rgb(34,197,94)    matches gradient <=35%
#
# State markers (override branch-type color when applicable):
#   dirty (uncommitted changes, staged or unstaged) -> red + '*'
#       red #dc2626 rgb(220,38,38) — matches gradient 80%+
#   ahead (clean tree but local commits not on remote, OR no upstream) -> orange + '+'
#       orange #f97316 rgb(249,115,22) — matches gradient 56-65%
#   pushed (clean tree + in sync with remote) -> branch-type color, no marker
GIT_PART=""
GIT_BRANCH=$(git rev-parse --abbrev-ref HEAD 2>/dev/null)
if [ -n "$GIT_BRANCH" ]; then
    if git status --porcelain 2>/dev/null | grep -q .; then
        GIT_STATE="dirty"
    else
        AHEAD=$(git rev-list --count @{u}..HEAD 2>/dev/null)
        if [ -z "$AHEAD" ] || [ "$AHEAD" -gt 0 ]; then
            GIT_STATE="ahead"
        else
            GIT_STATE="pushed"
        fi
    fi

    case "$GIT_BRANCH" in
        main|master) GIT_TYPE_COLOR="\033[38;2;59;130;246m" ;;
        develop)     GIT_TYPE_COLOR="\033[38;2;168;85;247m" ;;
        *)           GIT_TYPE_COLOR="\033[38;2;34;197;94m" ;;
    esac

    case "$GIT_STATE" in
        dirty) GIT_PART=" \033[92m|\033[0m \033[38;2;220;38;38m${GIT_BRANCH}*\033[0m" ;;
        ahead) GIT_PART=" \033[92m|\033[0m \033[38;2;249;115;22m${GIT_BRANCH}+\033[0m" ;;
        pushed) GIT_PART=" \033[92m|\033[0m ${GIT_TYPE_COLOR}${GIT_BRANCH}\033[0m" ;;
    esac
fi

# Terminal width for dynamic centering. tput cols < /dev/tty reads the
# controlling TTY's TIOCGWINSZ even when stdin is the piped JSON. Falls
# back to $COLUMNS env (set by some parent shells) and finally 80 — at
# which point centering on an 80-column virtual width still produces a
# reasonable layout for most terminal sizes; lines wider than 80 just
# self-degrade to left-align via the max(0, ...) clamp in python.
TERM_COLS=$(tput cols 2>/dev/null < /dev/tty || echo "${COLUMNS:-80}")

eval "$(echo "$input" | PYTHONIOENCODING=utf-8 \
    PROJECT_NAME_RESOLVED="$PROJECT_NAME" \
    GIT_PART_RAW="$GIT_PART" \
    TERM_COLS="$TERM_COLS" \
    $PYBIN -c "
import sys, json, os, hashlib, re, shutil, platform, subprocess, time
sys.stdout.reconfigure(encoding='utf-8')
ANSI_RE = re.compile(r'\x1b\[[0-9;]*m')
try:
    d = json.load(sys.stdin)

    # 6-tier color gradient applied to every gauge (ctx, 5h, 7d, cpu, ram,
    # drive %used, vram, gpu util). Truecolor escapes — passthrough only,
    # works on every modern terminal that supports 24-bit color (Windows
    # Terminal, Git Bash mintty, Console Host since Win10 v1809, etc.).
    #   <=35%  green        (#22c55e — Tailwind green-500)
    #   36-45  green-yellow (#a3e635 — Tailwind lime-400)
    #   46-55  yellow       (#eab308 — Tailwind yellow-500)
    #   56-65  orange       (#f97316 — Tailwind orange-500)
    #   66-79  red-orange   (#ea580c — Tailwind orange-600 / red-orange)
    #   80+    red          (#dc2626 — Tailwind red-600)
    #   None / N/A          dim grey \033[90m
    def _grad_color(p):
        if p is None: return '\033[90m'
        if p <= 35: return '\033[38;2;34;197;94m'
        if p <= 45: return '\033[38;2;163;230;53m'
        if p <= 55: return '\033[38;2;234;179;8m'
        if p <= 65: return '\033[38;2;249;115;22m'
        if p < 80:  return '\033[38;2;234;88;12m'
        return '\033[38;2;220;38;38m'

    # Label = renamed session_name if present, else cwd basename.
    # session_id drives a stable per-session color hash when renamed.
    session_name = (d.get('session_name') or '').strip()
    session_id = (d.get('session_id') or '').strip()
    project_resolved = os.environ.get('PROJECT_NAME_RESOLVED') or '?'
    label_text = session_name if session_name else project_resolved

    # 12-color ANSI palette: 6 normal + 6 bright. Skips black/white to
    # avoid invisibility on either light or dark terminals. Magenta (35)
    # stays the un-renamed default to preserve the original brand look.
    PALETTE = ['91','92','93','94','95','96','31','32','33','34','35','36']
    if session_name and session_id:
        h = hashlib.md5(session_id.encode('utf-8')).digest()[0]
        name_color = PALETTE[h % len(PALETTE)]
    else:
        name_color = '35'
    label = f'\033[{name_color}m[{label_text}]\033[0m'

    cw = d.get('context_window', {})
    pct = cw.get('used_percentage', 0)

    # Model abbreviation + family-specific color.
    #   Opus    -> O#.#  in gold (truecolor #FFD700 — closest to gold,
    #                    no named ANSI equivalent; passthrough only)
    #   Sonnet  -> S#.#  in bright red (91)
    #   Haiku   -> H#.#  in bright blue (94)
    #   MiniMax -> M#.#  in bright magenta (95) — purple-ish
    # Detection checks both display_name AND id so providers that surface
    # only one or the other (e.g., minimax via ANTHROPIC_BASE_URL override
    # naming the model 'MiniMax-M2.7') still classify correctly.
    model_block = d.get('model', {})
    model_disp = model_block.get('display_name', '?')
    model_id = model_block.get('id', '')
    model_search = f'{model_disp} {model_id}'

    minimax_m = re.search(r'(?i)MiniMax-?M?(\d+(?:\.\d+)?)', model_search)
    if minimax_m:
        model = f'M{minimax_m.group(1)}'
        model_color = '95'
    elif 'Opus' in model_disp:
        ver = model_disp.split('Opus')[1].strip().split()[0]
        model = f'O{ver}'
        model_color = '38;2;255;215;0'
    elif 'Sonnet' in model_disp:
        ver = model_disp.split('Sonnet')[1].strip().split()[0]
        model = f'S{ver}'
        model_color = '91'
    elif 'Haiku' in model_disp:
        ver = model_disp.split('Haiku')[1].strip().split()[0]
        model = f'H{ver}'
        model_color = '94'
    else:
        model = model_disp
        model_color = '0'

    model_str = f'\033[{model_color}m{model}\033[0m'

    # Defensive clamp — Claude Code has emitted negative or >100 values.
    try:
        pct = float(pct)
    except:
        pct = 0
    if pct < 0: pct = 0
    if pct > 100: pct = 100

    # Side-effect: write context % to ~/.claude/context_pct.txt as a
    # plain integer string. Hooks read this without round-tripping
    # through Claude Code. mkdir -p so it works on first run.
    home = os.environ.get('USERPROFILE') or os.path.expanduser('~')
    ctx_file = os.path.join(home, '.claude', 'context_pct.txt')
    try:
        os.makedirs(os.path.dirname(ctx_file), exist_ok=True)
        with open(ctx_file, 'w') as f:
            f.write(str(round(pct)))
    except:
        pass

    # Unicode block-fill bar — 16 cells wide × 8 sub-levels per cell = 128
    # total sub-cells, ~0.78% granularity. Cell glyphs walk from empty to
    # full: ' ' '▏' '▎' '▍' '▌' '▋' '▊' '▉' '█'. Bar color from the shared
    # 6-tier _grad_color gradient.
    def _make_bar(p, cells=16):
        LEVELS = [' ', '▏', '▎', '▍', '▌', '▋', '▊', '▉']
        sub_total = cells * 8
        filled_sub = max(0, min(sub_total, int(round(p / 100 * sub_total))))
        full = filled_sub // 8
        rem = filled_sub % 8
        if full >= cells:
            return '█' * cells
        return '█' * full + LEVELS[rem] + ' ' * (cells - full - 1)

    # CTX prefix added before the bar. Cyan label matches the rate-limit /
    # uptime / think / sys-stat cyan-label convention so every gauge segment
    # shares the same color hierarchy (cyan label + gradient-colored value).
    ctx_str = f'\033[36mCTX\033[0m {_grad_color(pct)}[{_make_bar(pct)}] {pct:.0f}%\033[0m'

    # Rate-limit gauges. Labels show TIME REMAINING UNTIL RESET, not the bare
    # window length — sourced from rate_limits.{five_hour,seven_day}.resets_at
    # which Claude Code emits as a Unix epoch (seconds, integer). Verified
    # empirically against live JSON capture (CC 2.1.138, see STATUSLINE.md §10).
    # Falls back to the static '5h'/'7d' label if resets_at is missing or in
    # the past. Bar still tracks used_percentage with the canonical gradient.
    # Self-omits when used_percentage is missing (free plans).
    # NOTE: no backticks anywhere in this Python block — they trigger
    # bash command substitution inside the python3 -c '...' heredoc.
    def _pct_bar(p):
        try: p = float(p)
        except: p = 0
        if p < 0: p = 0
        if p > 100: p = 100
        return f'{_grad_color(p)}[{_make_bar(p)}] {p:.0f}%\033[0m'

    def _reset_label(default_label, reset_ts, now_ts):
        if reset_ts is None: return default_label
        try:
            delta = float(reset_ts) - now_ts
        except (TypeError, ValueError):
            return default_label
        if delta <= 0: return default_label
        s = int(delta)
        if s < 60: return f'{s}s'
        mm = s // 60
        if mm < 60: return f'{mm}m'
        hh, mm = divmod(mm, 60)
        if hh < 24: return f'{hh}h{mm:02d}m'
        dd, hh = divmod(hh, 24)
        return f'{dd}d{hh:02d}h'

    now_ts = time.time()
    rate_limits = d.get('rate_limits', {})
    five_h_block = rate_limits.get('five_hour', {})
    seven_d_block = rate_limits.get('seven_day', {})
    five_h_pct = five_h_block.get('used_percentage')
    seven_d_pct = seven_d_block.get('used_percentage')
    five_h_label_str = _reset_label('5h', five_h_block.get('resets_at'), now_ts)
    seven_d_label_str = _reset_label('7d', seven_d_block.get('resets_at'), now_ts)

    rate5h_part = f' \033[92m|\033[0m \033[36m{five_h_label_str}\033[0m {_pct_bar(five_h_pct)}' if five_h_pct is not None else ''
    rate7d_part = f' \033[92m|\033[0m \033[36m{seven_d_label_str}\033[0m {_pct_bar(seven_d_pct)}' if seven_d_pct is not None else ''

    # Session uptime + cumulative thinking/API time from cost block.
    # cost.total_duration_ms = wall-clock since CLI session start (CC docs)
    # cost.total_api_duration_ms = cumulative time spent in API calls (Claude
    #   processing time = the closest signal to 'time Claude has been thinking')
    cost = d.get('cost', {})
    def _fmt_ms(ms):
        try: ms_int = int(ms)
        except: return ''
        if ms_int <= 0: return ''
        s = ms_int // 1000
        if s < 60: return f'{s}s'
        mm, ss = divmod(s, 60)
        if mm < 60: return f'{mm}m{ss:02d}s'
        hh, mm = divmod(mm, 60)
        if hh < 24: return f'{hh}h{mm:02d}m'
        dd, hh = divmod(hh, 24)
        return f'{dd}d{hh:02d}h'
    uptime_fmt = _fmt_ms(cost.get('total_duration_ms', 0))
    think_fmt = _fmt_ms(cost.get('total_api_duration_ms', 0))
    # Cyan label + bright-white value (97). Bright white over plain white
    # so the timer values pop on dark terminals. Leading separator baked in
    # so empty values don't leave dangling ' | ·' fragments.
    uptime_part = f' \033[92m|\033[0m \033[36mup\033[0m \033[97m{uptime_fmt}\033[0m' if uptime_fmt else ''
    think_part = f' \033[92m·\033[0m \033[36mthink\033[0m \033[97m{think_fmt}\033[0m' if think_fmt else ''

    # Line 3: cpu | ram | drive free space.
    # Linux: /proc + shutil stdlib (sub-millisecond reads).
    # Windows: ctypes -> kernel32 (GlobalMemoryStatusEx for RAM, GetSystemTimes
    #   + sidecar cache for CPU delta). Also sub-ms; no subprocess / wmic
    #   shell-out so the render stays inside the sub-100ms budget.
    # Disk works cross-platform via shutil.disk_usage on every path.
    cpu_pct = None
    sys_name = platform.system()
    if sys_name == 'Linux':
        try:
            with open('/proc/loadavg') as f_la:
                load_1m = float(f_la.read().split()[0])
            ncpu_calc = os.cpu_count() or 1
            cpu_pct = min(100.0, (load_1m / ncpu_calc) * 100.0)
        except Exception:
            pass
    elif sys_name == 'Windows':
        # GetSystemTimes returns (idle, kernel, user) FILETIMEs. lpKernelTime
        # INCLUDES idle time, so total = kernel + user, busy = total - idle.
        # Single sample is meaningless -> sidecar cache holds the previous
        # render's tuple; we compute the delta. First render after launch
        # shows '—' (no prior sample); every render after that is accurate.
        try:
            import ctypes
            from ctypes import wintypes
            idle_t = wintypes.FILETIME()
            kern_t = wintypes.FILETIME()
            user_t = wintypes.FILETIME()
            ctypes.windll.kernel32.GetSystemTimes(
                ctypes.byref(idle_t),
                ctypes.byref(kern_t),
                ctypes.byref(user_t),
            )
            def _ft_int(ft):
                return (ft.dwHighDateTime << 32) | ft.dwLowDateTime
            cur_idle = _ft_int(idle_t)
            cur_kern = _ft_int(kern_t)
            cur_user = _ft_int(user_t)
            cpu_cache_file = os.path.join(home, '.claude', 'cpu-cache.json')
            prev = None
            try:
                if os.path.exists(cpu_cache_file):
                    with open(cpu_cache_file) as f_cc:
                        prev = json.load(f_cc)
            except Exception:
                pass
            if prev:
                d_idle = cur_idle - prev.get('idle', 0)
                d_total = (cur_kern + cur_user) - (prev.get('kern', 0) + prev.get('user', 0))
                if d_total > 0:
                    busy = d_total - d_idle
                    cpu_pct = max(0.0, min(100.0, (busy / d_total) * 100.0))
            try:
                os.makedirs(os.path.dirname(cpu_cache_file), exist_ok=True)
                with open(cpu_cache_file, 'w') as f_cc:
                    json.dump({'idle': cur_idle, 'kern': cur_kern, 'user': cur_user}, f_cc)
            except Exception:
                pass
        except Exception:
            pass

    ram_used_gb = ram_total_gb = ram_pct = None
    if sys_name == 'Linux':
        try:
            with open('/proc/meminfo') as f_mi:
                mi = f_mi.read()
            total_kb = int(re.search(r'MemTotal:\s+(\d+)', mi).group(1))
            avail_kb = int(re.search(r'MemAvailable:\s+(\d+)', mi).group(1))
            ram_total_gb = (total_kb * 1024) / (1024**3)
            ram_used_gb = ((total_kb - avail_kb) * 1024) / (1024**3)
            ram_pct = (ram_used_gb / ram_total_gb) * 100 if ram_total_gb > 0 else 0
        except Exception:
            pass
    elif sys_name == 'Windows':
        # GlobalMemoryStatusEx via ctypes — instant (~microseconds), no
        # subprocess. ullAvailPhys is the same 'Available' figure Task
        # Manager shows (free + standby cache); used = total - avail.
        try:
            import ctypes
            class MEMORYSTATUSEX(ctypes.Structure):
                _fields_ = [
                    ('dwLength', ctypes.c_ulong),
                    ('dwMemoryLoad', ctypes.c_ulong),
                    ('ullTotalPhys', ctypes.c_ulonglong),
                    ('ullAvailPhys', ctypes.c_ulonglong),
                    ('ullTotalPageFile', ctypes.c_ulonglong),
                    ('ullAvailPageFile', ctypes.c_ulonglong),
                    ('ullTotalVirtual', ctypes.c_ulonglong),
                    ('ullAvailVirtual', ctypes.c_ulonglong),
                    ('sullAvailExtendedVirtual', ctypes.c_ulonglong),
                ]
            ms = MEMORYSTATUSEX()
            ms.dwLength = ctypes.sizeof(MEMORYSTATUSEX)
            ctypes.windll.kernel32.GlobalMemoryStatusEx(ctypes.byref(ms))
            ram_total_gb = ms.ullTotalPhys / (1024**3)
            ram_used_gb = (ms.ullTotalPhys - ms.ullAvailPhys) / (1024**3)
            ram_pct = (ram_used_gb / ram_total_gb) * 100 if ram_total_gb > 0 else 0
        except Exception:
            pass

    disk_free_gb = disk_total_gb_val = disk_pct = None
    try:
        du = shutil.disk_usage('.')
        disk_free_gb = du.free / (1024**3)
        disk_total_gb_val = du.total / (1024**3)
        disk_pct = (du.used / du.total) * 100 if du.total > 0 else 0
    except Exception:
        pass

    # Drive label resolution.
    #   Windows -> 'C:/' / 'F:/' from cwd drive letter via os.path.splitdrive
    #   POSIX   -> findmnt LABEL of the partition backing cwd; fall back to
    #              the mount point ('/', '/home', '/mnt/...') when no
    #              partition label is set.
    # Makes 'WHAT drive' immediately legible without parsing the project
    # path. Two findmnt calls are ~5-10ms each; acceptable per-render cost,
    # no caching needed.
    def _get_drive_label():
        if platform.system() == 'Windows':
            dd = os.path.splitdrive(os.getcwd())[0]
            return f'{dd}/' if dd else 'drive'
        try:
            cwd = os.getcwd()
            r = subprocess.run(
                ['findmnt', '-no', 'LABEL', '--target', cwd],
                capture_output=True, text=True, timeout=1.0
            )
            label_str = r.stdout.strip() if r.returncode == 0 else ''
            if label_str:
                return label_str
            r2 = subprocess.run(
                ['findmnt', '-no', 'TARGET', '--target', cwd],
                capture_output=True, text=True, timeout=1.0
            )
            target_str = r2.stdout.strip() if r2.returncode == 0 else ''
            return target_str or '/'
        except Exception:
            return 'drive'

    drive_label = _get_drive_label()

    # CPU cores suffix — appended after the cpu% in cyan to match the label
    # palette. × is the multiplication sign '×' (cleaner than 'x' next
    # to the digits). Self-omits when os.cpu_count() returns None (rare).
    ncpu = os.cpu_count() or 0
    cores_suffix = f' \033[36m×{ncpu}\033[0m' if ncpu else ''

    def _sys_seg(label_str, value_str, pct_val, suffix=''):
        # value_str is dim-grey '—' on N/A; otherwise gradient-colored
        # via the shared _grad_color helper. suffix is appended verbatim
        # (already-formatted, includes own ANSI), used for cpu's '×N cores'.
        if value_str is None:
            return f'\033[36m{label_str}\033[0m \033[90m—\033[0m'
        return f'\033[36m{label_str}\033[0m {_grad_color(pct_val)}{value_str}\033[0m{suffix}'

    cpu_val = f'{cpu_pct:.0f}%' if cpu_pct is not None else None
    ram_val = f'{ram_used_gb:.1f}/{ram_total_gb:.1f}G' if ram_pct is not None else None
    # Drive segment: '<free>G / <total>G free' — both gigabyte values share
    # the gradient color (which tracks % USED so high = red), and the literal
    # word 'free' is cyan-suffixed to match the rest-of-line cyan-label palette
    # (drive_label, cpu, ram). Implemented via the new free_suffix passed to
    # _sys_seg so the gradient block ends cleanly before the cyan word starts.
    disk_val = f'{disk_free_gb:.0f}G / {disk_total_gb_val:.0f}G' if disk_free_gb is not None and disk_total_gb_val is not None else None
    free_suffix = ' \033[36mfree\033[0m' if disk_val else ''

    sys_line = (
        f'{_sys_seg(\"cpu\", cpu_val, cpu_pct, cores_suffix)}'
        f' \033[92m|\033[0m '
        f'{_sys_seg(\"ram\", ram_val, ram_pct)}'
        f' \033[92m|\033[0m '
        f'{_sys_seg(drive_label, disk_val, disk_pct, free_suffix)}'
    )

    # Line 4: GPU(s) via nvidia-smi (with sidecar cache). Cached in
    # ~/.claude/gpu-cache.json with a 5-second TTL so the ~150ms nvidia-smi
    # shell-out doesn't block every render. On cache miss the script blocks
    # once to refresh. When nvidia-smi is missing OR returns no GPUs, line 4
    # renders as red 'No GPU Detected'.
    home_dir = os.environ.get('USERPROFILE') or os.path.expanduser('~')
    gpu_cache_file = os.path.join(home_dir, '.claude', 'gpu-cache.json')
    gpu_data = {'gpus': []}
    cache_fresh = False
    try:
        if os.path.exists(gpu_cache_file):
            if (time.time() - os.path.getmtime(gpu_cache_file)) < 5.0:
                with open(gpu_cache_file) as f_gc:
                    gpu_data = json.load(f_gc)
                cache_fresh = True
    except Exception:
        pass

    if not cache_fresh:
        try:
            sm = subprocess.run(
                ['nvidia-smi',
                 '--query-gpu=name,memory.used,memory.total,utilization.gpu',
                 '--format=csv,noheader,nounits'],
                capture_output=True, text=True, timeout=2.0
            )
            gpus = []
            if sm.returncode == 0 and sm.stdout.strip():
                for line_g in sm.stdout.strip().split('\n'):
                    parts = [p.strip() for p in line_g.split(',')]
                    if len(parts) >= 4:
                        try:
                            gpus.append({
                                'name': parts[0],
                                'mem_used_mb': float(parts[1]),
                                'mem_total_mb': float(parts[2]),
                                'util_pct': float(parts[3]),
                            })
                        except ValueError:
                            pass
            gpu_data = {'gpus': gpus}
        except (FileNotFoundError, subprocess.TimeoutExpired, Exception):
            gpu_data = {'gpus': []}
        try:
            os.makedirs(os.path.dirname(gpu_cache_file), exist_ok=True)
            with open(gpu_cache_file, 'w') as f_gc:
                json.dump(gpu_data, f_gc)
        except Exception:
            pass

    def _short_gpu(name):
        # Match common NVIDIA naming: RTX/GTX/GT prefix + digits + optional Ti/SUPER.
        # Falls through to first 12 chars on no match.
        mm = re.search(r'(?:RTX|GTX|GT|Tesla|Quadro)\s*([A-Za-z0-9]+)\s*(SUPER|Ti)?', name, re.IGNORECASE)
        if mm:
            base = mm.group(1)
            sfx = (mm.group(2) or '').lower()
            return base + ('s' if sfx == 'super' else 'ti' if sfx == 'ti' else '')
        return name[:12]

    gpus_list = gpu_data.get('gpus', [])
    if not gpus_list:
        gpu_line = '\033[31mNo GPU Detected\033[0m'
    else:
        gpu_segs = []
        for g in gpus_list:
            short_n = _short_gpu(g.get('name', '?'))
            mu_mb = g.get('mem_used_mb', 0)
            mt_mb = g.get('mem_total_mb', 0)
            ut = g.get('util_pct', 0)
            vram_pct = (mu_mb / mt_mb * 100) if mt_mb > 0 else 0
            used_gb = mu_mb / 1024
            total_gb = mt_mb / 1024
            gpu_segs.append(
                f'\033[36m{short_n}\033[0m {_grad_color(vram_pct)}{used_gb:.1f}/{total_gb:.0f}gb\033[0m {_grad_color(ut)}{ut:.0f}%\033[0m'
            )
        gpu_line = ' \033[92m|\033[0m '.join(gpu_segs)

    # Line composition (moved from bash so dynamic centering can run inline
    # without spawning a second python subprocess). UPTIME's leading
    # separator is baked in so it self-omits cleanly on fresh sessions —
    # without it, the line falls back to a single pipe between label and
    # model. Line 2's RATE5H/RATE7D follow the same self-omit pattern.
    git_part_raw = os.environ.get('GIT_PART_RAW', '')
    pipe = '\033[92m|\033[0m'
    if uptime_part:
        line1 = f'{label}{uptime_part}{think_part} {pipe} {model_str}{git_part_raw}'
    else:
        line1 = f'{label} {pipe} {model_str}{git_part_raw}'
    line2 = f'{ctx_str}{rate5h_part}{rate7d_part}'
    line3 = sys_line
    line4 = gpu_line

    # Dynamic centering — terminal width from \$TERM_COLS env (computed in
    # bash via tput cols < /dev/tty). Strips ANSI for visible-length count
    # using the precompiled ANSI_RE; pads with leading spaces only (no
    # trailing padding to avoid cursor-position drift). Self-degrades to
    # left-align when terminal width can't be determined or when content
    # is wider than the terminal (negative pad clamps to 0). Each line
    # centers independently so different-width lines naturally stack.
    cols_str = os.environ.get('TERM_COLS', '80') or '80'
    try:
        cols = int(cols_str)
    except ValueError:
        cols = 80
    def _center(s):
        if not s: return s
        visible = ANSI_RE.sub('', s)
        pad = max(0, (cols - len(visible)) // 2)
        return ' ' * pad + s

    line1c = _center(line1)
    line2c = _center(line2)
    line3c = _center(line3)
    line4c = _center(line4)

    print(f'LINE1=\"{line1c}\" LINE2=\"{line2c}\" LINE3=\"{line3c}\" LINE4=\"{line4c}\"')
except Exception:
    fallback_name = os.environ.get('PROJECT_NAME_RESOLVED') or '?'
    fallback_label = f'\033[35m[{fallback_name}]\033[0m'
    print(f'LINE1=\"{fallback_label}\" LINE2=\"\" LINE3=\"\" LINE4=\"\"')
" 2>/dev/null)"

# Render — composition + centering both happen inside the python heredoc
# above, so bash just emits the four LINE vars. Lines 2/3/4 self-omit on
# empty (e.g. exception path or all-N/A sys metrics).
echo -e "$LINE1"
[ -n "$LINE2" ] && echo -e "$LINE2"
[ -n "$LINE3" ] && echo -e "$LINE3"
[ -n "$LINE4" ] && echo -e "$LINE4"
