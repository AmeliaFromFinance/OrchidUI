#!/usr/bin/env bash
# Sends your commits to GitHub. If someone else pushed in the meantime, it checks their changes
# don't clash with yours, puts yours on top, runs the tests and then pushes.
#   scripts/push.sh             quick tests, then push
#   scripts/push.sh --full      all the tests first
#   scripts/push.sh --no-test   skip the tests
source "$(dirname "$0")/_lib.sh"

tests=quick
case "${1:-}" in
  "") ;;
  --full) tests=full ;;
  --no-test) tests=none ;;
  -h | --help) sed -n '2,6p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
  *) die "Unknown option: $1" ;;
esac
need git
require_branch
clean_tree || warn "Uncommitted changes won't be pushed. Use scripts/commit.sh -p to include them."

step "Checking GitHub"
fetch
read -r ahead behind < <(ahead_behind)
if [ "$ahead" = 0 ] && [ "$behind" = 0 ]; then ok "Already up to date."; exit 0; fi

if [ "$behind" -gt 0 ]; then
  step "$behind new commit(s) on GitHub, checking for conflicts"
  git_at_least 2 38 || die "Checking for conflicts needs git 2.38 or newer (you have $(git --version | cut -d' ' -f3))."
  if result="$(git merge-tree --write-tree --name-only --no-messages HEAD "$REMOTE/$BRANCH")"; then
    ok "No conflicts."
  else
    echo "$result" | tail -n +2 | sed 's/^/  /' >&2
    die "Those files were changed both here and on GitHub. Run git pull --rebase, fix them, then scripts/push.sh again."
  fi
  require_clean
  if [ "$ahead" = 0 ]; then
    git merge --quiet --ff-only "$REMOTE/$BRANCH"
    ok "Updated to match GitHub. Nothing of yours to push."
    exit 0
  fi
  git rebase --quiet "$REMOTE/$BRANCH" || { git rebase --abort; die "Your commits don't apply cleanly on top. Run git pull --rebase and sort it out by hand."; }
  ok "Your commits now sit on top of GitHub's."
fi

case "$tests" in
  quick) "$ROOT/scripts/test.sh" --quick ;;
  full) "$ROOT/scripts/test.sh" ;;
esac

step "Pushing"
git log --oneline "$REMOTE/$BRANCH..HEAD" | sed 's/^/  /'
git push --quiet "$REMOTE" "HEAD:$BRANCH"
ok "Pushed to $BRANCH."
