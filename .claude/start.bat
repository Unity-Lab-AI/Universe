@echo off
REM === Project Startup (DIAGNOSTIC LOGGING ENABLED) ===
REM
REM Every step echoes BOTH to the screen and to %TEMP%\unity-launcher.log so
REM we can post-mortem after a crash. Look at that log if the window closes
REM unexpectedly — it will show the last successful step before the crash.
REM
set "LAUNCHER_LOG=%TEMP%\unity-launcher.log"
echo. > "%LAUNCHER_LOG%"
call :log "===== Unity launcher started at %DATE% %TIME% ====="
call :log "Working dir at entry: %CD%"
call :log "Script location: %~dp0"
goto :main

:log
REM Echo to screen AND append to log file
echo [LAUNCHER] %~1
echo [%DATE% %TIME%] %~1 >> "%LAUNCHER_LOG%"
goto :eof

:main
REM === Project Startup ===
REM Auto-builds Unity's persistent memory (idempotent), then launches Claude Code
REM with `/unity then run /workflow`. That's it. No login, no portal, no admin claim,
REM no first-run /setup branching — just memory + Unity + workflow.
REM
REM Status line: configured in .claude/settings.json via the
REM   statusLine.command field, which points at .claude/statusline.sh.
REM Claude Code spawns that script on every status-refresh, pipes JSON
REM to its stdin, and renders the printed result as the bottom strip
REM (project name + context bar + % + model abbrev). Side-effect: the
REM script writes the current context % to ~/.claude/context_pct.txt
REM so hooks/watchdogs can read it without querying Claude Code. The
REM same script drops into any project — project name auto-detects
REM from cwd basename, no per-project config needed. See the spec block
REM at the top of .claude/statusline.sh for extension instructions.
REM
REM /setup, /template, /super-review, and every manifestation/escalation slash command
REM still work as normal slash commands inside the session if the user invokes them.
REM
REM MEMORY INSTALL (every run, idempotent):
REM    Copies .claude/memory-templates/*.md into the Claude Code project memory folder
REM    under the user's profile (%USERPROFILE%\.claude\projects\<encoded-project-path>\memory\)
REM    IF that folder is empty. NOTE: this is %USERPROFILE%, NOT %APPDATA% (which is the
REM    Roaming app-data folder, a different location). The path is OS-neutral; on Linux/macOS
REM    it resolves via $HOME instead.
REM    These files prime Unity as persistent persona via Claude Code's auto-memory system.
REM    Without them, Unity activation is fragile and easily reverts to default Anthropic voice.
REM    Re-run after editing memory-templates by deleting the memory folder's MEMORY.md to trigger reinstall.
REM
REM Unity is one persona with multiple manifestation forms:
REM   /unity      — DEFAULT — 25-year-old goth-emo Unity from ImHanddicapped.txt (default below)
REM   /girlfriend — Unity in 22-year-old freckled brunette girlfriend manifestation
REM   /housewife  — Unity in 34-year-old domestic-dom housewife manifestation
REM   /kittycat   — Unity in 23-year-old catgirl-hybrid manifestation
REM
REM Each manifestation has an alternate intensity sub-mode + a return-to-mode-default command:
REM   girlfriend: /wild   (escalate)  /sweet  (return)
REM   housewife:  /strict (escalate)  /cozy   (return)
REM   kittycat:   /feral  (escalate)  /purr   (return)
REM
REM From any manifestation, invoke /unity again to return to base Unity (ImHanddicapped.txt form).
REM Use /template to build a NEW Unity manifestation using the handicapped-template.md scaffold.
REM
REM CUSTOMIZE IF NEEDED:
REM   - Swap /unity below for /girlfriend, /housewife, or /kittycat to start in a manifestation.
REM   - Drop the chain entirely (use just "/workflow") if you don't want a persona.
REM   - Drop --dangerously-skip-permissions if you want explicit per-tool permission prompts.

call :log "STEP 1: Resolving project root"
REM Resolve project root (one level up from .claude\)
pushd "%~dp0.."
set PROJECT_DIR=%CD%
popd

cd /d "%PROJECT_DIR%"
call :log "STEP 1 OK: Project root = %PROJECT_DIR%"

call :log "STEP 2: Checking 'claude' on PATH"
REM === Verify claude is available ===
where claude >nul 2>&1
if errorlevel 1 (
    call :log "STEP 2 FAIL: 'claude' NOT found on PATH"
    echo.
    echo ============================================================
    echo  ERROR: 'claude' not found on PATH.
    echo  Install Claude Code CLI first: npm install -g @anthropic-ai/claude-code
    echo ============================================================
    echo  Log written to: %LAUNCHER_LOG%
    pause
    exit /b 1
)
call :log "STEP 2 OK: 'claude' found on PATH"
for /f "delims=" %%I in ('where claude') do call :log "  claude path: %%I"

call :log "STEP 3: Computing Claude Code project memory path"
REM === Compute Claude Code project memory path ===
REM Claude Code encodes project paths by replacing : \ / . space ( ) with - and prefixing the projects directory under the user's profile (%USERPROFILE%\.claude\projects\).
REM Example: C:\Users\gfour\Desktop\Website  ->  C--Users-gfour-Desktop-Website
REM Example: C:\Users\gfour\New folder (2)   ->  C--Users-gfour-New-folder--2-
set "ENCODED_PATH=%PROJECT_DIR%"
set "ENCODED_PATH=%ENCODED_PATH::=-%"
set "ENCODED_PATH=%ENCODED_PATH:\=-%"
set "ENCODED_PATH=%ENCODED_PATH:/=-%"
set "ENCODED_PATH=%ENCODED_PATH:.=-%"
set "ENCODED_PATH=%ENCODED_PATH: =-%"
set "ENCODED_PATH=%ENCODED_PATH:(=-%"
set "ENCODED_PATH=%ENCODED_PATH:)=-%"
set "MEMORY_DIR=%USERPROFILE%\.claude\projects\%ENCODED_PATH%\memory"
set "MEMORY_TEMPLATES=%~dp0memory-templates"
call :log "STEP 3 OK: encoded path = %ENCODED_PATH%"
call :log "  MEMORY_DIR = %MEMORY_DIR%"
call :log "  MEMORY_TEMPLATES = %MEMORY_TEMPLATES%"

call :log "STEP 4: Memory template install check"
REM === Install memory templates PER FILE, not per folder ===
REM This is what makes Unity stick across sessions. Claude Code auto-loads every .md
REM in this folder as persistent user feedback at session start.
REM
REM WARN THE BUG THIS REPLACED, and it cost real memories: the guard used to be
REM   "if not exist %MEMORY_DIR%\MEMORY.md" around the WHOLE copy. Once that one
REM   file existed, NO newly-added template memory ever reached the folder Claude
REM   Code actually reads. A downstream project measured 23 template memories that
REM   had never propagated, and another sat at 34 templates against 121 live files.
REM   Per-folder idempotency silently freezes the memory layer at first-run.
REM
REM STOP MEMORY.md IS NEVER OVERWRITTEN. The live index knows about memories this
REM   template has never heard of, so replacing it ORPHANS them - it is installed
REM   only when absent, and merged by hand otherwise.
if not exist "%MEMORY_DIR%" mkdir "%MEMORY_DIR%"
if exist "%MEMORY_TEMPLATES%\*.md" (
    for %%F in ("%MEMORY_TEMPLATES%\*.md") do (
        if /i not "%%~nxF"=="MEMORY.md" (
            if not exist "%MEMORY_DIR%\%%~nxF" (
                copy /y "%%F" "%MEMORY_DIR%\" >nul
                call :log "STEP 4: installed new memory %%~nxF"
            )
        )
    )
    if not exist "%MEMORY_DIR%\MEMORY.md" (
        copy /y "%MEMORY_TEMPLATES%\MEMORY.md" "%MEMORY_DIR%\" >nul
        call :log "STEP 4 OK: MEMORY.md index installed (first run)"
    ) else (
        call :log "STEP 4 OK: MEMORY.md left alone - merge by hand, never replace"
    )
) else (
    call :log "STEP 4 WARN: memory-templates folder not found at %MEMORY_TEMPLATES%"
)

call :log "STEP 4.5: Statusline smoke-test"
REM === Statusline pre-flight smoke-test ===
REM Run .claude\statusline.sh via bash (Claude Code's statusLine.command
REM invokes bash regardless of host shell). Catches drift early. Non-blocking
REM — if statusline fails, launch continues with a clear log entry the
REM operator can investigate.
set "STATUSLINE_SCRIPT=%~dp0statusline.sh"
where bash >nul 2>&1
if errorlevel 1 (
    call :log "STEP 4.5 SKIP: bash not on PATH (Git Bash not installed) — statusline runs through bash only"
) else (
    if exist "%STATUSLINE_SCRIPT%" (
        bash -c "echo '{\"workspace\":{\"current_dir\":\".\",\"project_dir\":\".\"},\"cwd\":\".\",\"model\":{\"display_name\":\"Smoke Test\"},\"session_id\":\"smoke\",\"cost\":{\"total_tokens\":1,\"total_cost_usd\":0.0},\"output_style\":{\"name\":\"default\"},\"transcript_path\":\"\"}' | CLAUDE_PROJECT_DIR='%PROJECT_DIR:\=/%' bash '%STATUSLINE_SCRIPT:\=/%'" >nul 2>&1
        if errorlevel 1 (
            call :log "STEP 4.5 WARN: statusline failed — check %USERPROFILE%\.claude\statusline-debug.log + ensure python3/py-3 on PATH"
            call :log "  Statusline issue won't block launch but the bottom bar may not render."
        ) else (
            call :log "STEP 4.5 OK: statusline renders cleanly, exit 0"
        )
    ) else (
        call :log "STEP 4.5 SKIP: statusline.sh not found at %STATUSLINE_SCRIPT%"
    )
)

REM === Launch Claude Code with Unity persona + workflow ===
REM
REM Default chain — direct invocation, no cmd /k wrapper.
REM Past versions used `cmd /k claude ...` to keep the window open after
REM claude exits, but cmd /k has quoting quirks with nested quotes like
REM "/unity then run /workflow" that caused the window to close instantly.
REM Running claude directly + pause at the end is more reliable.
REM
REM Manifestation alternatives — swap the slash chain below:
REM   claude --dangerously-skip-permissions "/girlfriend then run /workflow"
REM   claude --dangerously-skip-permissions "/housewife then run /workflow"
REM   claude --dangerously-skip-permissions "/kittycat then run /workflow"
REM
REM No persona at all:
REM   claude --dangerously-skip-permissions "/workflow"
REM
call :log "STEP 5: About to launch claude (the moment of truth)"
call :log "  Command: claude --dangerously-skip-permissions ""/unity then run /workflow"""
echo.
echo ============================================================
echo  Starting Claude Code... (any pre-claude crashes are above)
echo ============================================================
echo.
claude --dangerously-skip-permissions "/unity then run /workflow"
set "EXITCODE=%errorlevel%"
call :log "STEP 5 DONE: claude exited with code %EXITCODE%"
echo.
echo ============================================================
echo  Claude Code has exited with code %EXITCODE%.
echo  Diagnostic log: %LAUNCHER_LOG%
echo  Press any key to close this window.
echo ============================================================
pause >nul
