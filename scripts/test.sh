#!/usr/bin/env bash
# Builds everything and runs the same checks as CI.
#   scripts/test.sh          everything
#   scripts/test.sh --quick  the DLL tests only (fastest)
source "$(dirname "$0")/_lib.sh"

quick=0
case "${1:-}" in
  "") ;;
  --quick) quick=1 ;;
  -h | --help) sed -n '2,4p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
  *) die "Unknown option: $1" ;;
esac
need dotnet

step "Building"
dotnet build OrchidUI.slnx -c Release -v quiet -nologo

step "DLL, storage and protocol tests"
dotnet test --project tests/OrchidUI.Tests -c Release --no-build

if [ "$quick" = 0 ]; then
  step "Window tests (headless)"
  dotnet test --project tests/OrchidUI.Host.Tests -c Release --no-build

  step "The examples compile unchanged"
  for example in settings runtime minimal customised; do
    dotnet tools/orchid-sandbox/bin/Release/net10.0/orchid-sandbox.dll "$example" --compile --examples examples
  done

  step "Golden files are current"
  git diff --quiet -- tests/fixtures tests/OrchidUI.Host.Tests/snapshots \
    || die "Tests changed files in tests/fixtures or snapshots. Check them with git diff."
fi

ok "All good"
