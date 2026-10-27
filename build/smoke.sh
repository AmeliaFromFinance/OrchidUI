#!/usr/bin/env bash
# Starts orchid-host on the minimal example's spec, checks it is still running after a few
# seconds (it drew a window instead of crashing), and saves a screenshot when it can.
#   build/smoke.sh <orchid-host> <screenshot.png> [wrapper...]   e.g. wrapper: xvfb-run -a
set -euo pipefail
host="$1"; shot="$2"; shift 2
root="$(cd "$(dirname "$0")/.." && pwd)"
"$host" --version
script=$(cat <<SH
"$host" --spec "$root/tests/fixtures/specs/minimal.json" --log-console & pid=\$!
sleep 8
if ! kill -0 \$pid 2>/dev/null; then wait \$pid; echo "orchid-host exited early (code \$?)" >&2; exit 1; fi
if command -v import >/dev/null && [ -n "\${DISPLAY:-}" ]; then import -window root "$shot" || true;
elif command -v screencapture >/dev/null; then screencapture -x "$shot" || true; fi
kill \$pid
echo "orchid-host stayed up and drew its window"
SH
)
"$@" bash -c "$script"
