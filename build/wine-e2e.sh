#!/usr/bin/env bash
# Opens the minimal example from the net481 sandbox under Wine and waits for orchid-host to
# say hello. Mode "launch": the DLL starts the host with start /unix. Mode "watch": a
# separate `orchid-host --watch` picks up the session (ORCHIDUI_NO_LAUNCH=1).
#   build/wine-e2e.sh launch|watch <screenshot.png>
set -euo pipefail
mode="$1"; shot="$2"
root="$(cd "$(dirname "$0")/.." && pwd)"
export XDG_STATE_HOME="$root/.e2e-state" XDG_RUNTIME_DIR="$root/.e2e-run"
mkdir -p "$XDG_STATE_HOME" "$XDG_RUNTIME_DIR" && chmod 700 "$XDG_RUNTIME_DIR"
log="$XDG_STATE_HOME/orchidui/orchid-host.log"
rm -f "$log"
script=$(cat <<SH
set -u
if [ "$mode" = watch ]; then "$root/dist/linux-x64/orchid-host" --watch & watcher=\$!; fi
cd "$root"
wine tools/orchid-sandbox/bin/Release/net481/orchid-sandbox.exe minimal > "$root/wine-$mode.log" 2>&1 & sandbox=\$!
for i in \$(seq 1 120); do
  if grep -q "hello sent" "$log" 2>/dev/null; then break; fi
  sleep 1
done
sleep 3
import -window root "$root/$shot" || true
ok=0; grep -q "hello sent" "$log" 2>/dev/null && ok=1
kill \$sandbox 2>/dev/null || true
[ "$mode" = watch ] && kill \$watcher 2>/dev/null || true
wineserver -k 2>/dev/null || true
cat "$log" 2>/dev/null || true
[ \$ok = 1 ] || { echo "orchid-host never connected ($mode)"; exit 1; }
echo "orchid-host connected to the net481 DLL under Wine ($mode)"
SH
)
xvfb-run -a -s "-screen 0 1400x900x24" bash -c "$script"
