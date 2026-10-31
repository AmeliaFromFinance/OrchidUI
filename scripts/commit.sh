#!/usr/bin/env bash
# Commits everything you've changed (files in .gitignore stay out).
#   scripts/commit.sh "Fix the slider label"          commit
#   scripts/commit.sh -t "Fix the slider label"       run the quick tests first
#   scripts/commit.sh -p "Fix the slider label"       then push (see push.sh)
source "$(dirname "$0")/_lib.sh"

run_tests=0 push=0
while [ $# -gt 0 ]; do
  case "$1" in
    -t | --test) run_tests=1; shift ;;
    -p | --push) push=1; shift ;;
    -h | --help) sed -n '2,5p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    -*) die "Unknown option: $1" ;;
    *) break ;;
  esac
done
message="${1:-}"
[ -n "$message" ] || die "Give a commit message: scripts/commit.sh \"What changed\""
need git
who="$(identity)"
clean_tree && die "Nothing to commit."

[ "$run_tests" = 1 ] && "$ROOT/scripts/test.sh" --quick

step "Changes"
git add --all
git status --short
echo

conflicted=""
while IFS= read -r -d '' file; do
  grep -IqE '^(<<<<<<<|>>>>>>>)( |$)' "$file" && conflicted="$conflicted $file"
done < <(git diff --cached --name-only -z --diff-filter=d)
[ -z "$conflicted" ] || { git reset --quiet; die "These still have merge conflict markers:$conflicted"; }

git commit --quiet -m "$message"
ok "Committed $(git rev-parse --short HEAD) as $who"

[ "$push" = 1 ] && exec "$ROOT/scripts/push.sh"
exit 0
