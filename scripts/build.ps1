#!/usr/bin/env pwsh
[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [switch]$Coverage,
    [switch]$E2E,
    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $repo "Act.slnx"
$runSettings = Join-Path $repo ".runsettings"

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

# `-NoBuild` exists for the browser job in CI, which has to build *before* it can install Chromium —
# the installer is emitted into the test project's output by the Playwright package, so it does not
# exist until the solution is built. Without this the job would build the solution twice.
if (-not $NoBuild) {
    Invoke-Step "restore" { dotnet restore $solution }
    Invoke-Step "build" { dotnet build $solution -c $Configuration --no-restore }
}

# The browser suite is excluded by default and `-E2E` runs it *instead*. It needs Chromium installed
# (`playwright.ps1 install chromium` from the e2e project's output), starts a real Kestrel host per test
# class, and takes about a minute rather than the fast suite's few seconds. Leaving it in the default run
# is how a fast suite stops being fast and a local build starts depending on a browser.
$filter = if ($E2E) { "Category=E2E" } else { "Category!=E2E" }

# Off by default: collecting coverage roughly doubles how long the suite takes, and the common reason
# to run this script is to find out whether the build is green. `.runsettings` says what the number
# counts and, more to the point, what it does not.
if ($Coverage) {
    $results = Join-Path $repo "TestResults"

    if (Test-Path $results) {
        Remove-Item -Recurse -Force $results
    }

    Invoke-Step "test" {
        dotnet test $solution -c $Configuration --no-build `
            --filter $filter --settings $runSettings --collect:"XPlat Code Coverage" --results-directory $results
    }
}
else {
    Invoke-Step "test" { dotnet test $solution -c $Configuration --no-build --filter $filter }
}
