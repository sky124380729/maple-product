#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DOTNET_BIN="${DOTNET_BIN:-dotnet}"

cd "$ROOT_DIR"
"$DOTNET_BIN" test MapleProduct.sln --configuration Release
npm --prefix client test -- --run
npm --prefix client run build
git diff --check

if rg -n "SendInput|PostMessage|Virtual HID|RawKeyboard" src client; then
  echo "Forbidden input API reference found" >&2
  exit 1
fi

KEYBD_MATCHES="$(rg -l "keybd_event" src client | sort || true)"
if [[ "$KEYBD_MATCHES" != "src/Maple.InputBroker/KeybdEventInputAdapter.cs" ]]; then
  echo "keybd_event must only appear in the broker adapter" >&2
  printf '%s\n' "$KEYBD_MATCHES" >&2
  exit 1
fi
