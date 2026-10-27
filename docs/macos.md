# macOS

Streamer.bot runs on macOS through Wine, usually via Whisky, CrossOver or Wine itself. Streamer.bot's [macOS guide](https://docs.streamer.bot/get-started/installation/macos) uses Whisky, which is now in maintenance mode; CrossOver and plain Wine work the same way for OrchidUI. `OrchidUI.dll` runs inside Streamer.bot, and the window is drawn by the macOS build of `orchid-host`, a native Apple Silicon program.

## Install

```bash
cd "/path/to/Streamer.bot"                   # the folder with Streamer.bot.exe
cp ~/Downloads/OrchidUI.dll .
mkdir -p OrchidUI && unzip ~/Downloads/orchid-host-osx-arm64.zip -d OrchidUI
xattr -d com.apple.quarantine OrchidUI/orchid-host
chmod +x OrchidUI/orchid-host
```

The `xattr` line matters. The host is ad-hoc signed but not notarised, and macOS quarantines files downloaded in a browser and quietly refuses to start them from Wine.

To check the host runs, `OrchidUI/orchid-host --version` should print something like `orchid-host 1.0.0 (protocol 1)`.

## If the window never appears

Read the host log at `~/Library/Logs/OrchidUI/orchid-host.log`. If it's empty or missing, the host never started, so run the `xattr` line again.

If that doesn't help, your Wine build's `start /unix` may not work. Run the host yourself in a Terminal and leave it open, as described in [linux.md](linux.md#if-the-window-never-appears):

```bash
"/path/to/Streamer.bot/OrchidUI/orchid-host" --watch
```

Session files go in your per-user `$TMPDIR`, which only you can open. If your Wine wrapper hides `TMPDIR`, make a private folder and point both sides at it:

```bash
mkdir -p ~/Library/Caches/OrchidUI && chmod 700 ~/Library/Caches/OrchidUI
# For Streamer.bot: ORCHIDUI_SESSION_DIR=/Users/<you>/Library/Caches/OrchidUI
# For the host:     orchid-host --watch --dir ~/Library/Caches/OrchidUI
```

## Differences from Windows

- Shortcuts use `Cmd` instead of `Ctrl`.
- The title bar is always the native one. The `Chrome` and `Backdrop` options only apply on Windows.
- Retina scaling is automatic. `ORCHIDUI_SCALE` adds zoom on top.
- VoiceOver reads every control, with its label as the name and its description as help.
- Only Apple Silicon (`osx-arm64`) is built for now.
- Don't re-sign the host. That changes its SHA-256, and a release DLL will refuse it.
