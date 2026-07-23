#!/usr/bin/env pwsh
[CmdletBinding()]
param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $repo "Act.slnx"

dotnet restore $solution
dotnet build $solution -c $Configuration --no-restore
dotnet test $solution -c $Configuration --no-build
