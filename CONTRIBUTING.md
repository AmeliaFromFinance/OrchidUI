# Contributing

Thanks for taking a look. Bug reports, fixes and new features are all welcome.

When reporting a bug, please include the relevant lines from Streamer.bot's log, your `orchid-host.log` (the [README](README.md#troubleshooting) says where it is), your OS, and your Wine or Proton version if you use one.

## Setup

You need the .NET 10 SDK. It builds everything on any OS, including the .NET Framework DLL. Set `AVALONIA_TELEMETRY_OPTOUT=1` so Avalonia's build doesn't send telemetry. The tests run headless; you only need an X server (Xvfb is enough) on Linux to run the host by hand.

```sh
dotnet build OrchidUI.slnx
dotnet test --project tests/OrchidUI.Tests        # the DLL, storage, protocol and live sessions
dotnet test --project tests/OrchidUI.Host.Tests   # the window, headless
```

## Things to keep in mind

- `src/OrchidUI` and `src/OrchidUI.Protocol` target .NET Framework 4.8.1 with C# 7.3, because that's what Streamer.bot runs. The build enforces it. They also build for net10.0, but only so the tests run everywhere.
- Don't change or remove public API, since people's actions would stop compiling. Adding to it is fine. The four files in `examples/` must keep compiling unchanged.
- Don't change the saved format, or settings people already have will break. If you touch `Field.Normalize` or the JSON writer, the settings fixtures will tell you.
- The host doesn't keep state. Anything that decides something (saving, validation, whether the window may close) belongs in the DLL; the host draws and reports.
- A new message type needs an entry in `MessageSchema`, a test, and a line in [docs/protocol.md](docs/protocol.md).
- Use made-up names in examples and tests (PixelPancake, QuietFern, MossyLantern and so on).

## Golden files and snapshots

Some tests compare against saved output. If you meant to change it, regenerate it and read the diff before committing:

```sh
ORCHIDUI_UPDATE_GOLDEN=1 dotnet test --project tests/OrchidUI.Tests        # tests/fixtures/
ORCHIDUI_UPDATE_GOLDEN=1 dotnet test --project tests/OrchidUI.Host.Tests   # theme screenshots
```

Never update them just to make a failing test pass. A mismatch writes a `*.received` file next to the expected one (git ignores these). Screenshots are compared on Linux with a small per-pixel tolerance. Golden files mustn't contain anything specific to your machine, like your home folder.

## Pull requests

Keep each pull request to one topic and include tests. A bug fix should come with a test that fails without it. CI has to pass on Windows, Linux and macOS. The nightly Wine job can be flaky, so mention it if your change touches the Wine path.

## Scripts

`scripts/` has shortcuts for the usual chores. They run in bash: Linux, macOS, or Git Bash on Windows. Add `--help` to any of them for the options.

| Script | What it does |
|---|---|
| `scripts/test.sh` | Builds and runs the same checks as CI. `--quick` runs only the DLL tests |
| `scripts/commit.sh "message"` | Commits all your changes. `-t` runs the quick tests first, `-p` pushes after |
| `scripts/push.sh` | Checks GitHub's newer commits don't clash with yours, puts yours on top, tests, pushes |
| `scripts/status.sh` | Branch, changes, what's waiting to push or pull, latest builds and drafts |
| `scripts/bundle.sh` | Zips the project into `dist/`, leaving out everything in `.gitignore` |
| `scripts/release.sh 1.2.0` | Sets the version, dates the CHANGELOG, tests, and pushes the tag. `--watch` waits for the build |
| `scripts/publish.sh` | Makes the draft release public. `--verify` checks the files against `SHA256SUMS` first |
| `scripts/unrelease.sh 1.2.0` | Deletes an unpublished draft and its tag, so you can fix something and release again |

`release.sh`, `publish.sh` and `unrelease.sh` ask before doing anything; `YES=1` skips the question. The last three need the [GitHub CLI](https://cli.github.com) (`gh auth login` once).

## Releasing

1. Add what changed under a `## Unreleased` heading in the CHANGELOG.
2. Run `scripts/release.sh X.Y.Z`. It bumps `<Version>` in `Directory.Build.props`, dates the CHANGELOG section, and pushes the `vX.Y.Z` tag. The release workflow builds and pins everything, ad-hoc signs the macOS host, and makes a draft release.
3. Go through [docs/release-checklist.md](docs/release-checklist.md) on the draft release.
4. Run `scripts/publish.sh X.Y.Z`.
