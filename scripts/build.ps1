#!/usr/bin/env pwsh
[CmdletBinding()]
param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $repo "Act.slnx"

# `$ErrorActionPreference = "Stop"` does not stop on a *native* command's exit code — only newer
# PowerShell does that, and only with `$PSNativeCommandUseErrorActionPreference`. Without an explicit
# check, a failed `dotnet build` falls straight through to `dotnet test --no-build`, which then runs
# the previous build's assemblies and reports them green. That happened; hence this.
function Invoke-Step {
    param([string]$Name, [scriptblock]$Step)

    & $Step

    if ($LASTEXITCODE -ne 0) {
        Write-Error "$Name failed with exit code $LASTEXITCODE."

        exit $LASTEXITCODE
    }
}

Invoke-Step "restore" { dotnet restore $solution }
Invoke-Step "build" { dotnet build $solution -c $Configuration --no-restore }
Invoke-Step "test" { dotnet test $solution -c $Configuration --no-build }
