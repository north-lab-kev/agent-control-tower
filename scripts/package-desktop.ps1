#!/usr/bin/env pwsh
[CmdletBinding()]
param(
    [string]$Rid = "win-x64",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repo "src/Act.App/Act.App.csproj"
$output = Join-Path $repo "artifacts/desktop/$Rid"

dotnet publish $project `
    -c $Configuration `
    -r $Rid `
    -p:ElectronPackaging=true `
    -p:PublishSingleFile=false `
    -p:SelfContained=true `
    -p:PublishUrl=$output

Write-Host ""
Write-Host "Desktop artifact written to: $output"
