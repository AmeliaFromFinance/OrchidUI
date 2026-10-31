#!/usr/bin/env bash
# Makes a draft release public.
#   scripts/publish.sh            the version in Directory.Build.props
#   scripts/publish.sh 1.1.0      a specific one
#   scripts/publish.sh --verify   also download the files and check them against SHA256SUMS
source "$(dirname "$0")/_lib.sh"

ver="" verify=0
while [ $# -gt 0 ]; do
  case "$1" in
    --verify) verify=1; shift ;;
    -h | --help) sed -n '2,5p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    -*) die "Unknown option: $1" ;;
    *) ver="${1#v}"; shift ;;
  esac
done
ver="${ver:-$(version)}"
tag="v$ver"
need gh

info="$(gh release view "$tag" --json isDraft,url,assets --jq '[.isDraft, .url, ([.assets[].name] | join(" "))] | @tsv' 2> /dev/null)" \
  || die "There's no release for $tag yet. Start one with scripts/release.sh $ver"
IFS=$'\t' read -r draft url assets <<< "$info"
[ "$draft" = true ] || { ok "$tag is already public: $url"; exit 0; }

step "Files in the draft"
missing=0
for file in OrchidUI.dll orchid-host-win-x64.zip orchid-host-linux-x64.zip orchid-host-linux-arm64.zip orchid-host-osx-arm64.zip SHA256SUMS; do
  if [[ " $assets " == *" $file "* ]]; then ok "$file"; else warn "$file is missing"; missing=1; fi
done
[ "$missing" = 0 ] || die "The draft is incomplete. Check the release build on GitHub."

if [ "$verify" = 1 ]; then
  step "Checking the files against SHA256SUMS"
  tmp="$(mktemp -d)"
  trap 'rm -rf "$tmp"' EXIT
  gh release download "$tag" --dir "$tmp"
  if command -v sha256sum > /dev/null; then (cd "$tmp" && sha256sum -c --quiet SHA256SUMS); else (cd "$tmp" && shasum -a 256 -c --quiet SHA256SUMS); fi
  ok "Checksums match."
fi

confirm "Publish $tag? Have you gone through docs/release-checklist.md?" || die "Stopped. It's still a draft."
gh release edit "$tag" --draft=false --latest > /dev/null
ok "Published: $url"
