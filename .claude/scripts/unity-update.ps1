# .claude/scripts/unity-update.ps1
#
# Refresh the current project's .claude/ from upstream. Functionally identical
# to /unity-install — both commands now share the same idempotent
# preserve-and-restore flow. /unity-update exists for muscle memory; the actual
# work is in unity-install.ps1.
#
# Usage:
#   pwsh -File .\unity-update.ps1                                (branch=main, target=cwd)
#   pwsh -File .\unity-update.ps1 develop                        (branch=develop, target=cwd)
#   pwsh -File .\unity-update.ps1 feature/setup-install          (branch=feature/setup-install, target=cwd)
#
# Canonical implementation: .claude/scripts/unity-install.ps1

param(
    [string]$Arg1 = '',
    [string]$Arg2 = ''
)

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$InstallScript = Join-Path $ScriptDir 'unity-install.ps1'

if (-not (Test-Path -Path $InstallScript -PathType Leaf)) {
    Write-Error "[unity-update] canonical install script not found at $InstallScript"
    exit 1
}

& $InstallScript $Arg1 $Arg2
exit $LASTEXITCODE
