#!/usr/bin/env bash
set -euo pipefail

rid="${1:-linux-x64}"
configuration="${2:-Release}"

repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
project="$repo/src/Act.App/Act.App.csproj"
output="$repo/artifacts/desktop/$rid"

dotnet publish "$project" \
    -c "$configuration" \
    -r "$rid" \
    -p:ElectronPackaging=true \
    -p:PublishUrl="$output"

echo ""
echo "Desktop artifact written to: $output"
