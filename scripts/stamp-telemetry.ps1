#!/usr/bin/env pwsh
# Stamps the PostHog project token into src/Act.App/appsettings.json before packaging.
# Called once per packaging job in release.yml; the reasoning lives here so the two
# jobs cannot drift apart.
#
# The key is not in the repo and never will be: `appsettings.json` ships an
# empty one, so a fork or a build from source gets `NullTelemetrySink` and
# points at nobody's project. Only a release carries a real value, stamped
# into the checkout here and thrown away with the runner.
#
# It is written base64-encoded so the installer does not carry it in clear.
# Be clear about what that is worth: it is obfuscation, not protection. A
# PostHog *project* key is write-only by design — PostHog publishes it in the
# script tag of every site that uses them — so there is nothing here to
# defend, and base64 would not defend it. **Nothing that is genuinely secret
# may be added to this script on the strength of it being encoded.**
[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($env:POSTHOG_PROJECT_TOKEN)) {
    throw "The POSTHOG_PROJECT_TOKEN environment variable is empty or not set; refusing to package an installer that would send nothing."
}

$encoded = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($env:POSTHOG_PROJECT_TOKEN))

# Actions masks the secret itself, but not a transform of it. Registering
# the encoded form keeps a later `set -x` or a stack trace from printing
# what the raw value is masked to hide.
Write-Host "::add-mask::$encoded"

$repo = Split-Path -Parent $PSScriptRoot
$path = Join-Path $repo 'src/Act.App/appsettings.json'
$settings = Get-Content $path -Raw | ConvertFrom-Json

$settings.Telemetry.ProjectToken = $encoded
$settings | ConvertTo-Json -Depth 10 | Set-Content $path -Encoding utf8

# Read back and decode rather than trust the write: a renamed section would
# leave the key unset, and the installer would look perfectly healthy.
$written = (Get-Content $path -Raw | ConvertFrom-Json).Telemetry
$roundTripped = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($written.ProjectToken))

if ($roundTripped -ne $env:POSTHOG_PROJECT_TOKEN) {
    throw "The telemetry token in $path does not decode back to the secret."
}
if (-not $written.Enabled) {
    throw "Telemetry is disabled in $path; the installer would ship with it off."
}

Write-Host "Telemetry token stamped (base64, $($encoded.Length) chars); host $($written.Host)."
