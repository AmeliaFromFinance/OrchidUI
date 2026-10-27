#!/usr/bin/env bash
# Builds the Linux test kit (see docs/linux.md) into dist/OrchidTest: orchid-sandbox (net481)
# with the real OrchidUI.dll, the examples, and the native orchid-host in OrchidUI/, laid out like
# a Streamer.bot install. With a folder, also copies it there as OrchidTest (replacing an old one).
#   build/testkit.sh                          just build dist/OrchidTest
#   build/testkit.sh "$WINEPREFIX/drive_c"    build, then copy to $WINEPREFIX/drive_c/OrchidTest
#   RID=linux-arm64 build/testkit.sh          for an ARM64 machine
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
rid="${RID:-linux-x64}"
kit="$root/dist/OrchidTest"
export AVALONIA_TELEMETRY_OPTOUT=1 DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
dest=""
if [ $# -gt 0 ]; then
  dest="${1%/}"
  [ -d "$dest" ] || { echo "not a folder: $dest (create the prefix first)" >&2; exit 66; }
  dest="$(cd "$dest" && pwd)"
fi

cd "$root"
dotnet build tools/orchid-sandbox -c Release -f net481 -v quiet -nologo
dotnet publish src/OrchidUI.Host -c Release -r "$rid" --self-contained -p:DebugType=none -o "dist/$rid" -v quiet -nologo

rm -rf "$kit" && mkdir -p "$kit/OrchidUI"
cp -r tools/orchid-sandbox/bin/Release/net481/. "$kit/"
cp -r examples "$kit/examples"
cp "dist/$rid/orchid-host" "$kit/OrchidUI/"
chmod +x "$kit/OrchidUI/orchid-host"
cp tests/fixtures/specs/settings.json "$kit/spec-settings.json"
echo "Built $kit"

if [ -n "$dest" ]; then
  rm -rf "$dest/OrchidTest"
  cp -r "$kit" "$dest/OrchidTest"
  echo "Copied to $dest/OrchidTest"
fi
