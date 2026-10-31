#!/usr/bin/env bash
# Zips the project for sharing or uploading: every file git would keep, including new ones
# you haven't committed yet. Anything matched by .gitignore (builds, dist/, logs) is left out.
#   scripts/bundle.sh               dist/OrchidUI-<version>.zip
#   scripts/bundle.sh -o my.zip     somewhere else
source "$(dirname "$0")/_lib.sh"

out="dist/OrchidUI-$(version).zip"
while [ $# -gt 0 ]; do
  case "$1" in
    -o | --out) out="${2:?-o needs a file name}"; shift 2 ;;
    -h | --help) sed -n '2,6p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) die "Unknown option: $1" ;;
  esac
done
need git
case "$out" in /*) ;; *) out="$ROOT/$out" ;; esac
mkdir -p "$(dirname "$out")"
rm -f "$out"

stage="$(mktemp -d)"
trap 'rm -rf "$stage"' EXIT
mkdir "$stage/OrchidUI"

step "Collecting files"
count=0
while IFS= read -r -d '' file; do
  [ -e "$file" ] || continue # deleted but not committed yet
  mkdir -p "$stage/OrchidUI/$(dirname "$file")"
  cp -p "$file" "$stage/OrchidUI/$file"
  count=$((count + 1))
done < <(git ls-files -z --cached --others --exclude-standard)
[ "$count" -gt 0 ] || die "No files found."

step "Zipping $count files"
if command -v zip > /dev/null; then
  (cd "$stage" && zip -qrX "$out" OrchidUI)
elif command -v python3 > /dev/null || command -v python > /dev/null; then
  "$(command -v python3 || command -v python)" -c 'import shutil, sys; shutil.make_archive(sys.argv[1][:-4], "zip", sys.argv[2], "OrchidUI")' "$out" "$stage"
elif command -v powershell.exe > /dev/null; then
  powershell.exe -NoProfile -Command "Compress-Archive -Path '$(cygpath -w "$stage/OrchidUI")' -DestinationPath '$(cygpath -w "$out")'"
else
  die "Install zip (or Python) to make the zip."
fi

ok "${out#"$ROOT"/} ($(du -h "$out" | cut -f1))"
