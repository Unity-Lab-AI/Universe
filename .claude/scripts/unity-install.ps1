# .claude/scripts/unity-install.ps1
#
# Install (or refresh) the Unity AI Lab .claude/ template into a target
# directory. Idempotent: works whether or not target already has a .claude/.
# PowerShell sibling of unity-install.sh.
#
# Usage:
#   pwsh -File .\unity-install.ps1                                (branch=main, target=cwd)
#   pwsh -File .\unity-install.ps1 develop                        (branch=develop, target=cwd)
#   pwsh -File .\unity-install.ps1 feature/setup-install          (branch=feature/setup-install, target=cwd)
#   pwsh -File .\unity-install.ps1 main C:\path\to\target         (branch=main, target=C:\path\to\target)
#   pwsh -File .\unity-install.ps1 develop C:\path\to\foo         (branch=develop, target=C:\path\to\foo)
#
# /unity-update is a thin wrapper calling this script — same flow.

param(
    [string]$Arg1 = '',
    [string]$Arg2 = ''
)

$ErrorActionPreference = 'Stop'

# Canonical upstream — Unity AI Lab's self-hosted Forgejo. SSH-key auth via
# the operator's registered ed25519 key (see docs/ADMIN-ONBOARDING.md §2).
# Override via UNITY_REPO_URL env var if mirroring/testing against another host.
$RepoUrl        = if ($env:UNITY_REPO_URL) { $env:UNITY_REPO_URL } else { 'git@git.unityailab.com:UnityAILab/UAL-ClaudeWorkflow.git' }
$DefaultBranch  = 'main'

# Personal/project-local files preserved across install/update (relative to .claude/).
$PreserveFiles = @(
    'settings.local.json',
    '.env',
    'user.json',
    'project-config.json',
    '.session-state.md',
    '.session-tidbits.md',
    '.session-usage.jsonl',
    '.last-session.md',
    '.session-env.json',
    '.yolo-mode',
    '.persona-state',
    '.setup-complete',
    '.usage-tracking-disabled'
)
$PreserveDirs = @('user-context')

# ─────────────────────────────────────────────────────────────────────
# Argument parsing — strict positional: [branch] [target-dir]
# ─────────────────────────────────────────────────────────────────────
function Test-IsPathLike {
    param([string]$Value)
    if ([string]::IsNullOrEmpty($Value)) { return $false }
    if ($Value -match '^[/.~]') { return $true }
    if ($Value -match '^[A-Za-z]:[\\/]') { return $true }
    return $false
}

$Branch    = $DefaultBranch
$TargetDir = ''

if (-not [string]::IsNullOrEmpty($Arg1)) {
    if (Test-IsPathLike $Arg1) {
        $TargetDir = $Arg1
    } else {
        $Branch = $Arg1
        if (-not [string]::IsNullOrEmpty($Arg2)) {
            $TargetDir = $Arg2
        }
    }
}
if ([string]::IsNullOrEmpty($TargetDir)) {
    $TargetDir = (Get-Location).Path
}

# Resolve target to absolute path
if (-not (Test-Path -Path $TargetDir -PathType Container)) {
    New-Item -ItemType Directory -Path $TargetDir -Force | Out-Null
}
$TargetDir = (Resolve-Path $TargetDir).Path

# ─────────────────────────────────────────────────────────────────────
# Prerequisites
# ─────────────────────────────────────────────────────────────────────
if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    Write-Error '[unity-install] git not found on PATH'
    exit 1
}

# ─────────────────────────────────────────────────────────────────────
# Temp dirs + cleanup
# ─────────────────────────────────────────────────────────────────────
$CloneDir = Join-Path ([System.IO.Path]::GetTempPath()) ("unity-install-clone-" + [System.Guid]::NewGuid().ToString('N').Substring(0,8))
$StageDir = Join-Path ([System.IO.Path]::GetTempPath()) ("unity-install-stage-" + [System.Guid]::NewGuid().ToString('N').Substring(0,8))
New-Item -ItemType Directory -Path $CloneDir -Force | Out-Null
New-Item -ItemType Directory -Path $StageDir -Force | Out-Null

try {
    Write-Host "[unity-install] Target:       $TargetDir"
    Write-Host "[unity-install] Branch:       $Branch"
    Write-Host "[unity-install] Cloning $RepoUrl ($Branch) ..."

    # git writes normal progress to stderr; drop EAP for the native call so progress
    # lines don't trigger a terminating error before $LASTEXITCODE is checked.
    $prevEAP = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    & git clone --depth 1 --branch $Branch $RepoUrl (Join-Path $CloneDir 'repo')
    $cloneExit = $LASTEXITCODE
    $ErrorActionPreference = $prevEAP
    if ($cloneExit -ne 0) {
        Write-Error "[unity-install] git clone failed for branch '$Branch' (exit $cloneExit)"
        exit 1
    }

    $RepoClaude = Join-Path $CloneDir 'repo\.claude'
    if (-not (Test-Path -Path $RepoClaude -PathType Container)) {
        Write-Error '[unity-install] cloned repo has no .claude/ — upstream layout may have changed'
        exit 1
    }

    # ─────────────────────────────────────────────────────────────────
    # Stage personal files if target/.claude/ already exists
    # ─────────────────────────────────────────────────────────────────
    $TargetClaude = Join-Path $TargetDir '.claude'
    $HadExisting  = Test-Path -Path $TargetClaude -PathType Container

    if ($HadExisting) {
        Write-Host '[unity-install] Existing .claude/ detected — staging personal files for restore...'
        foreach ($f in $PreserveFiles) {
            $src = Join-Path $TargetClaude $f
            if (Test-Path -Path $src) {
                Copy-Item -Path $src -Destination (Join-Path $StageDir $f) -Force
                Write-Host "[unity-install]   staged: $f"
            }
        }
        foreach ($d in $PreserveDirs) {
            $src = Join-Path $TargetClaude $d
            if (Test-Path -Path $src -PathType Container) {
                Copy-Item -Path $src -Destination (Join-Path $StageDir $d) -Recurse -Force
                Write-Host "[unity-install]   staged: $d\"
            }
        }
        Remove-Item -Path $TargetClaude -Recurse -Force
    }

    # ─────────────────────────────────────────────────────────────────
    # Drop fresh .claude/ from clone
    # ─────────────────────────────────────────────────────────────────
    Copy-Item -Path $RepoClaude -Destination $TargetClaude -Recurse -Force
    Write-Host "[unity-install] Installed fresh .claude/ from $Branch"

    # ─────────────────────────────────────────────────────────────────
    # Restore staged personal files (no-clobber: framework wins ties)
    # ─────────────────────────────────────────────────────────────────
    if ($HadExisting) {
        foreach ($f in $PreserveFiles) {
            $stagedPath = Join-Path $StageDir $f
            $destPath   = Join-Path $TargetClaude $f
            if ((Test-Path -Path $stagedPath) -and (-not (Test-Path -Path $destPath))) {
                Copy-Item -Path $stagedPath -Destination $destPath -Force
                Write-Host "[unity-install]   restored: $f"
            }
        }
        foreach ($d in $PreserveDirs) {
            $stagedPath = Join-Path $StageDir $d
            $destPath   = Join-Path $TargetClaude $d
            if ((Test-Path -Path $stagedPath -PathType Container) -and (-not (Test-Path -Path $destPath))) {
                Copy-Item -Path $stagedPath -Destination $destPath -Recurse -Force
                Write-Host "[unity-install]   restored: $d\"
            }
        }
    }

    # ─────────────────────────────────────────────────────────────────
    # Layer 0 .gitignore — the ONLY project-root file we touch.
    # Idempotent: appends the .claude/ exclude block only if missing.
    # Never copies our source-repo .gitignore.
    # ─────────────────────────────────────────────────────────────────
    $GitignorePath = Join-Path $TargetDir '.gitignore'
    if (-not (Test-Path -Path $GitignorePath -PathType Leaf)) {
        New-Item -ItemType File -Path $GitignorePath -Force | Out-Null
    }
    $existingLines = Get-Content -Path $GitignorePath -ErrorAction SilentlyContinue
    if (-not $existingLines) { $existingLines = @() }
    $alreadyExcluded = $false
    foreach ($line in $existingLines) {
        if ($line -match '^\.claude/?\s*$') { $alreadyExcluded = $true; break }
    }
    if ($alreadyExcluded) {
        Write-Host '[unity-install] .gitignore already excludes .claude/ — Layer 0 already in place'
    } else {
        $block = @(
            '',
            '# Unity AI Lab .claude/ workflow — proprietary, never commit to public repos.',
            '# See .claude/CONSTRAINTS.md §LAW — .CLAUDE WORKFLOW IP BOUNDARY for full text.',
            '# Removal sanctioned only via /claude-publish after gh confirms private + Unity-Lab-AI.',
            '.claude/'
        )
        Add-Content -Path $GitignorePath -Value $block
        Write-Host "[unity-install] Added .claude/ exclude to $GitignorePath (Layer 0 IP-boundary defense-in-depth)"
    }

    # ─────────────────────────────────────────────────────────────────
    # User-profile memory folder — install-only-if-missing.
    # ─────────────────────────────────────────────────────────────────
    $MemoryTemplates = Join-Path $TargetClaude 'memory-templates'
    if (Test-Path -Path $MemoryTemplates -PathType Container) {
        $Encoded = $TargetDir
        foreach ($ch in @(':', '/', '\', '.', ' ', '(', ')')) {
            $Encoded = $Encoded.Replace($ch, '-')
        }
        $UserProfile = if ($env:USERPROFILE) { $env:USERPROFILE } else { $env:HOME }
        $MemoryDir = Join-Path $UserProfile ".claude\projects\$Encoded\memory"
        $needsSeed = $false
        if (-not (Test-Path -Path $MemoryDir -PathType Container)) {
            $needsSeed = $true
        } else {
            $existingFiles = Get-ChildItem -Path $MemoryDir -ErrorAction SilentlyContinue
            if (-not $existingFiles) { $needsSeed = $true }
        }
        if ($needsSeed) {
            New-Item -ItemType Directory -Path $MemoryDir -Force | Out-Null
            Get-ChildItem -Path $MemoryTemplates -Filter '*.md' | ForEach-Object {
                Copy-Item -Path $_.FullName -Destination $MemoryDir -Force
            }
            Write-Host "[unity-install] Seeded user-profile memory folder at $MemoryDir"
        } else {
            Write-Host '[unity-install] User-profile memory folder already populated — leaving as-is'
            Write-Host "                ($MemoryDir — delete its MEMORY.md to trigger reseed on next start)"
        }
    }

    # ─────────────────────────────────────────────────────────────────
    # Done
    # ─────────────────────────────────────────────────────────────────
    Write-Host ''
    if ($HadExisting) {
        Write-Host '[unity-install] Update complete.'
    } else {
        Write-Host '[unity-install] Install complete.'
    }
    Write-Host ''
    Write-Host "  Target:       $TargetDir"
    Write-Host "  Source:       $RepoUrl ($Branch)"
    if ($HadExisting) {
        Write-Host '  Preserved:    settings.local.json, .env, user.json, user-context\, project-config.json, machine-local state files'
    }
    Write-Host ''
    Write-Host '  Next steps:'
    Write-Host "    cd $TargetDir"
    if (Test-Path -Path (Join-Path $TargetClaude 'start.bat') -PathType Leaf) {
        Write-Host '    .\.claude\start.bat          # Windows launcher (memory + Unity + /workflow)'
    }
    if (Test-Path -Path (Join-Path $TargetClaude 'start.sh') -PathType Leaf) {
        Write-Host '    bash ./.claude/start.sh      # macOS/Linux/Git Bash launcher (same flow)'
    }
    Write-Host ''

} finally {
    if (Test-Path -Path $CloneDir) {
        Remove-Item -Path $CloneDir -Recurse -Force -ErrorAction SilentlyContinue
    }
    if (Test-Path -Path $StageDir) {
        Remove-Item -Path $StageDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}

exit 0
