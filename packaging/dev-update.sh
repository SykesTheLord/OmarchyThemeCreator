#!/usr/bin/env bash
# Rebuild Omarchy Theme Creator from this repo and overwrite the currently-installed binary.
# For fast local iteration: `bash packaging/dev-update.sh`.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(dirname "$SCRIPT_DIR")"
BIN_NAME="omarchy-theme-creator"
FRESH="$ROOT_DIR/dist/$BIN_NAME"

# 1. Build (delegate to build.sh — single source of truth for the publish flags).
echo ":: Building fresh binary"
bash "$SCRIPT_DIR/build.sh" >&2

# 2. Locate the installed binary (PATH first, then the Makefile's /usr/local default).
TARGET="$(command -v "$BIN_NAME" || true)"
if [[ -z "$TARGET" ]]; then
  if [[ -x "/usr/local/bin/$BIN_NAME" ]]; then
    TARGET="/usr/local/bin/$BIN_NAME"
  else
    echo ":: $BIN_NAME is not installed yet." >&2
    echo "   Install it once first:  sudo make -C '$ROOT_DIR/packaging' install" >&2
    exit 1
  fi
fi
echo ":: Installed binary: $TARGET"

# 3. Overwrite it — use sudo only when the target (or its dir) isn't user-writable.
SUDO=""
if [[ ! -w "$TARGET" || ! -w "$(dirname "$TARGET")" ]]; then
  SUDO="sudo"
  echo ":: Target not writable; using sudo"
fi
$SUDO install -Dm755 "$FRESH" "$TARGET"

# 4. Refresh the .desktop entry + icon only if this prefix already owns them, so a dev
#    refresh never scatters files into an unexpected prefix.
PREFIX="$(dirname "$(dirname "$TARGET")")"   # e.g. /usr/local/bin -> /usr/local
DESKTOP="$PREFIX/share/applications/$BIN_NAME.desktop"
if [[ -f "$DESKTOP" ]]; then
  $SUDO install -Dm644 "$SCRIPT_DIR/$BIN_NAME.desktop" "$DESKTOP"
  if [[ -f "$SCRIPT_DIR/icon.png" ]]; then
    $SUDO install -Dm644 "$SCRIPT_DIR/icon.png" "$PREFIX/share/pixmaps/$BIN_NAME.png"
  fi
  echo ":: Refreshed desktop entry + icon under $PREFIX"
fi

# 5. Report the swap so it's verifiable.
echo ":: Updated $TARGET"
ls -lh --time-style=+%Y-%m-%dT%H:%M "$TARGET"
if pgrep -x "$BIN_NAME" >/dev/null 2>&1; then
  echo ":: Note: $BIN_NAME is running — restart it to pick up the new build."
fi
