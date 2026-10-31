#!/usr/bin/env bash
# Takes back a release that hasn't been published, so you can fix something and release again.
# Deletes the draft and the tag (here and on GitHub). The version commit stays.
#   scripts/unrelease.sh 1.1.0
source "$(dirname "$0")/_lib.sh"

case "${1:-}" in -h | --help) sed -n '2,4p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;; esac
ver="${1#v}"
[ -n "$ver" ] || die "Which version? For example: scripts/unrelease.sh 1.1.0"
tag="v$ver"
need git gh

draft="$(gh release view "$tag" --json isDraft --jq .isDraft 2> /dev/null || echo none)"
[ "$draft" != false ] || die "$tag is already public. Release a new version instead."

confirm "Delete the $tag draft and tag?" || die "Stopped."
[ "$draft" = true ] && gh release delete "$tag" --yes && ok "Deleted the draft."
if [ -n "$(git ls-remote --tags "$REMOTE" "refs/tags/$tag")" ]; then
  git push --quiet "$REMOTE" ":refs/tags/$tag" && ok "Deleted $tag on GitHub."
fi
git tag -d "$tag" > /dev/null 2>&1 && ok "Deleted $tag here."
echo "Fix what you need, commit it, then run scripts/release.sh $ver again."
