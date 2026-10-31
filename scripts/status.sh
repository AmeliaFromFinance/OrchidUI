#!/usr/bin/env bash
# A quick look at where things stand: branch, changes, GitHub, version, builds and drafts.
source "$(dirname "$0")/_lib.sh"
need git

fetch 2> /dev/null || warn "Couldn't reach GitHub; this may be out of date."
read -r ahead behind < <(ahead_behind 2> /dev/null || echo "? ?")
changed="$(git status --porcelain | wc -l | tr -d ' ')"
last_tag="$(git describe --tags --abbrev=0 2> /dev/null || echo none)"

printf '%-12s %s\n' Branch "$(branch)" Version "$(version)" "Last tag" "$last_tag" \
  Uncommitted "$changed file(s)" "To push" "$ahead commit(s)" "To pull" "$behind commit(s)"

if command -v gh > /dev/null && gh auth status > /dev/null 2>&1; then
  echo
  step "Latest builds"
  gh run list --limit 3 | sed 's/^/  /'
  drafts="$(gh release list --json tagName,isDraft --jq '.[] | select(.isDraft) | .tagName')"
  [ -z "$drafts" ] || { echo; step "Drafts waiting to be published"; echo "$drafts" | sed 's/^/  /'; }
fi
