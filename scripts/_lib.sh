# Shared by the scripts in this folder. Not meant to be run on its own.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"
export DOTNET_NOLOGO=1 DOTNET_CLI_TELEMETRY_OPTOUT=1 AVALONIA_TELEMETRY_OPTOUT=1 TESTINGPLATFORM_TELEMETRY_OPTOUT=1

REMOTE="${REMOTE:-origin}"
BRANCH="${BRANCH:-main}"

if [ -t 1 ]; then
  _b=$'\e[1m' _r=$'\e[0m' _red=$'\e[31m' _grn=$'\e[32m' _yel=$'\e[33m' _pnk=$'\e[35m'
else
  _b='' _r='' _red='' _grn='' _yel='' _pnk=''
fi

step() { printf '%s==>%s %s%s%s\n' "$_pnk" "$_r" "$_b" "$*" "$_r"; }
ok()   { printf '%s✓%s %s\n' "$_grn" "$_r" "$*"; }
warn() { printf '%s!%s %s\n' "$_yel" "$_r" "$*" >&2; }
die()  { printf '%s✗ %s%s\n' "$_red" "$*" "$_r" >&2; exit 1; }

need() {
  local cmd
  for cmd in "$@"; do command -v "$cmd" > /dev/null || die "$cmd isn't installed."; done
}

# Asks a yes/no question. YES=1 answers yes for you (for CI or when you're sure).
confirm() {
  [ "${YES:-0}" = 1 ] && return 0
  [ -t 0 ] || die "$1 (run with YES=1 to answer yes)"
  local reply
  read -r -p "$1 [y/N] " reply
  [[ "$reply" =~ ^[Yy] ]]
}

version() { sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' Directory.Build.props | head -n 1; }

is_semver() { [[ "$1" =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?$ ]]; }

branch() { git symbolic-ref --quiet --short HEAD || echo "(detached)"; }

clean_tree() { [ -z "$(git status --porcelain)" ]; }

require_clean() { clean_tree || die "You have uncommitted changes. Commit them (scripts/commit.sh) or stash them first."; }

require_branch() { [ "$(branch)" = "$BRANCH" ] || die "You're on $(branch). Switch to $BRANCH first."; }

identity() {
  local name email
  name="$(git config user.name || true)"
  email="$(git config user.email || true)"
  [ -n "$name" ] && [ -n "$email" ] || die "Git doesn't know who you are. Run: git config user.name \"Your Name\" && git config user.email you@example.com"
  echo "$name <$email>"
}

git_at_least() {
  local v major minor
  v="$(git --version | sed 's/[^0-9.]*\([0-9][0-9.]*\).*/\1/')"
  major="${v%%.*}" minor="${v#*.}" minor="${minor%%.*}"
  [ "$major" -gt "$1" ] || { [ "$major" = "$1" ] && [ "$minor" -ge "$2" ]; }
}

fetch() { git fetch --quiet --prune --tags "$REMOTE"; }

# Prints "ahead behind" compared with the remote branch.
ahead_behind() { git rev-list --left-right --count "HEAD...$REMOTE/$BRANCH" | awk '{print $1, $2}'; }
