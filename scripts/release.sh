#!/usr/bin/env bash
# Starts a release. Sets the version, dates the CHANGELOG's "Unreleased" section, runs the tests,
# then pushes a vX.Y.Z tag. GitHub builds everything from the tag and makes a draft release.
#   scripts/release.sh 1.1.0             release 1.1.0
#   scripts/release.sh 1.1.0 --watch     and wait for the build to finish
#   scripts/release.sh 1.1.0 --no-test   skip the tests (CI still runs them)
source "$(dirname "$0")/_lib.sh"

ver="" watch=0 run_tests=1
while [ $# -gt 0 ]; do
  case "$1" in
    --watch) watch=1; shift ;;
    --no-test) run_tests=0; shift ;;
    -h | --help) sed -n '2,7p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    -*) die "Unknown option: $1" ;;
    *) ver="${1#v}"; shift ;;
  esac
done
[ -n "$ver" ] || die "Which version? For example: scripts/release.sh 1.1.0 (now $(version))"
is_semver "$ver" || die "$ver isn't a version like 1.2.3."
tag="v$ver"
need git
[ "$watch" = 0 ] || need gh
identity > /dev/null
require_branch
require_clean

step "Checking GitHub"
fetch
read -r _ behind < <(ahead_behind)
[ "$behind" = 0 ] || die "GitHub has $behind commit(s) you don't. Run scripts/push.sh first."
git rev-parse -q --verify "refs/tags/$tag" > /dev/null && die "$tag already exists."
[ -z "$(git ls-remote --tags "$REMOTE" "refs/tags/$tag")" ] || die "$tag already exists on GitHub."

step "Version and changelog"
if [ "$(version)" != "$ver" ]; then
  sed -i.bak "s:<Version>.*</Version>:<Version>$ver</Version>:" Directory.Build.props && rm -f Directory.Build.props.bak
  ok "Directory.Build.props: $ver"
fi
if grep -qE "^## \[?$ver\]?( |$)" CHANGELOG.md; then
  ok "CHANGELOG already has $ver"
elif grep -q '^## Unreleased' CHANGELOG.md; then
  awk -v h="## $ver ($(date +%Y-%m-%d))" '!done && /^## Unreleased/ {print h; done=1; next} {print}' CHANGELOG.md > CHANGELOG.md.new && mv CHANGELOG.md.new CHANGELOG.md
  ok "CHANGELOG: Unreleased is now $ver"
else
  git checkout --quiet -- Directory.Build.props
  die "Add a \"## Unreleased\" section to CHANGELOG.md with what changed, commit it, then try again."
fi
notes="$(awk -v v="$ver" '$0 ~ "^## \\[?"v"\\]?" {f=1; next} /^## / && f {exit} f' CHANGELOG.md)"
[ -n "$(echo "$notes" | tr -d '[:space:]')" ] || { git checkout --quiet -- Directory.Build.props CHANGELOG.md; die "The CHANGELOG section for $ver is empty."; }
echo
echo "$notes" | sed -e '/./,$!d' -e 's/^/  /'
echo

[ "$run_tests" = 1 ] && "$ROOT/scripts/test.sh"

confirm "Release $tag? This pushes the tag and starts the build." || { git checkout --quiet -- Directory.Build.props CHANGELOG.md; die "Stopped. Nothing was pushed."; }

clean_tree || git commit --quiet -am "Release $ver"
git tag -a "$tag" -m "OrchidUI $ver"
step "Pushing $BRANCH and $tag"
git push --quiet "$REMOTE" "HEAD:$BRANCH"
git push --quiet "$REMOTE" "$tag"
ok "GitHub is building $tag. It'll appear as a draft release."

if [ "$watch" = 1 ]; then
  step "Waiting for the build"
  run=""
  for _ in $(seq 1 20); do
    run="$(gh run list --workflow release.yml --branch "$tag" --limit 1 --json databaseId --jq '.[0].databaseId' 2> /dev/null || true)"
    [ -n "$run" ] && break
    sleep 3
  done
  [ -n "$run" ] || die "Couldn't find the build. Check the Actions tab on GitHub."
  gh run watch "$run" --exit-status || die "The build failed. Fix it, then scripts/unrelease.sh $ver and release again."
  ok "Draft ready: $(gh release view "$tag" --json url --jq .url)"
fi
echo "Next: go through docs/release-checklist.md with the draft's files, then run scripts/publish.sh $ver"
