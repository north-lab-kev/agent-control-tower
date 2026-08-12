#!/usr/bin/env pwsh
[CmdletBinding()]
param(
    [string]$Rid = "win-x64",
    [string]$Configuration = "Release",
    [string]$Version = ""
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repo "src/Act.App/Act.App.csproj"
$output = Join-Path $repo "artifacts/desktop/$Rid"

$versionArgs = @()

if ($Version) {
    if ($Version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$') {
        throw "Version '$Version' is not <major>.<minor>.<patch>[-prerelease] (ex: 1.2.3)."
    }

    $versionArgs = @("-p:Version=$Version")
}

dotnet publish $project `
    -c $Configuration `
    -r $Rid `
    -p:ElectronPackaging=true `
    -p:PublishSingleFile=false `
    -p:SelfContained=true `
    -p:PublishUrl=$output `
    @versionArgs

Write-Host ""
Write-Host "Desktop artifact written to: $output"
