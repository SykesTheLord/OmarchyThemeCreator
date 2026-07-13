#!/usr/bin/env bash
# Build a single self-contained linux-x64 binary for Omarchy Theme Creator.
# Output: dist/omarchy-theme-creator
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(dirname "$SCRIPT_DIR")"
PROJECT="$ROOT_DIR/src/OmarchyThemeCreator/OmarchyThemeCreator.csproj"
OUT_DIR="${1:-$ROOT_DIR/dist}"
RID="${RID:-linux-x64}"

echo ":: Publishing $PROJECT ($RID) -> $OUT_DIR"

dotnet publish "$PROJECT" \
  -c Release \
  -r "$RID" \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:DebugType=none \
  -p:DebugSymbols=false \
  -o "$OUT_DIR"

echo ":: Built $OUT_DIR/omarchy-theme-creator"
