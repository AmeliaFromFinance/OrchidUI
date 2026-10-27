# Linux

Streamer.bot runs on Linux under Wine, either directly or through Bottles, Lutris or Proton. Streamer.bot calls this experimental, so set it up with their [Linux guide](https://docs.streamer.bot/get-started/installation/linux) first. `OrchidUI.dll` runs inside Streamer.bot like on Windows, but the window is drawn by the Linux build of `orchid-host`, so it uses your desktop's fonts, scaling and screen reader.

## Install

```bash
cd "$WINEPREFIX/drive_c/Streamer.bot"        # wherever Streamer.bot.exe is
cp ~/Downloads/OrchidUI.dll .
mkdir -p OrchidUI && unzip ~/Downloads/orchid-host-linux-x64.zip -d OrchidUI
chmod +x OrchidUI/orchid-host
```

On an ARM machine (`uname -m` says `aarch64`), use `orchid-host-linux-arm64.zip`. Using the wrong one gives errors like `qemu-aarch64-static: Could not open '/lib/ld-linux-aarch64.so.1'`; `file OrchidUI/orchid-host` tells you which one you have.

When you open a window, Streamer.bot's log should say `Settings window opened (Wine on Linux, 127.0.0.1:<port>)`, or `(Wine on Linux (Proton), ...)` under Proton.

The host is self-contained, so you don't need .NET installed for it, and OrchidUI doesn't need `winetricks dotnet48` or `corefonts` (though Streamer.bot itself may). It uses the usual X11 libraries that every desktop has (`libX11`, `libICE`, `libSM`, `libfontconfig`), and runs through XWayland on Wayland.

When a window is opened, the DLL notices it's under Wine, listens on `127.0.0.1` with a one-time token, writes a small session file and starts the Linux host with Wine's `start /unix`. The host reads the file, connects and draws the window. [protocol.md](protocol.md) has the details.

## If the window never appears

Look at the host log first: `~/.local/state/orchidui/orchid-host.log` (or under `$XDG_STATE_HOME`, or [inside a Flatpak's folder](#where-things-are)).

- A new line ending in `refusing it` means the session folder isn't private. See [Session folder](#session-folder).
- Any other error means the host started and gave up, and the line says why.
- Nothing new means the host never started. This is usually Wine's `start /unix` bug, below.

Wine 9.5 up to 10.0-rc2 has a broken `start /unix` ([bug 56471](https://list.winehq.org/hyperkitty/list/wine-bugs@list.winehq.org/thread/NRM2H4ZMUJTO6BCB62RWMYALWXHDXELY/)). After 8 seconds, Streamer.bot's log says the host hasn't connected. The fix is to run the host yourself in watch mode and leave it running:

```bash
"$WINEPREFIX/drive_c/Streamer.bot/OrchidUI/orchid-host" --watch
```

It watches the session folder and opens a window for each new session. To stop the DLL trying `start /unix` at all, set `ORCHIDUI_NO_LAUNCH=1` for Streamer.bot.

If Streamer.bot says `isn't a build this OrchidUI.dll trusts`, the DLL and host are from different builds. Copy both from the same release, with Streamer.bot closed.

### Session folder

The session file holds a one-time token, so it has to be in a folder only you can open. The DLL can't set Unix permissions from inside Wine, so it looks for a folder that's already private, in this order:

1. `$XDG_RUNTIME_DIR/orchidui`, which is normally `/run/user/<uid>` with mode `0700`.
2. `/run/user/<uid>/orchidui`, for when Wine hides `XDG_RUNTIME_DIR` (GE-Proton in Bottles does). The DLL finds your uid through `Z:`.
3. `$TMPDIR/orchidui`, but only when `TMPDIR` is per-user, as on macOS.
4. `~/.cache/orchidui/sessions`, if Wine can see your home folder. This logs a warning, because you have to make it private yourself.
5. The prefix's `AppData\Local\OrchidUI\sessions`, as a last resort. This also has to be made private.

The host refuses a session file in a folder other users could read, and logs `refusing it`. To fix that, `chmod 700` the folder named in the log, or run `orchid-host --watch` once (it makes the folder it watches `0700`), or point both sides at a folder of your own:

```bash
mkdir -p ~/orchid-sessions && chmod 700 ~/orchid-sessions
# For Streamer.bot: ORCHIDUI_SESSION_DIR=/home/<you>/orchid-sessions
# For the host:     orchid-host --watch --dir ~/orchid-sessions
```

## Bottles, Lutris, Steam and others

| Launcher | Where the files go | Where environment variables go |
|---|---|---|
| Bottles | The bottle's `drive_c/.../Streamer.bot` (Browse files) | Settings > Environment Variables |
| Lutris | The prefix's `drive_c/.../Streamer.bot` | Configure > System options > Environment variables |
| Steam (Proton) | `~/.local/share/Steam/steamapps/compatdata/<id>/pfx/drive_c/.../Streamer.bot` | Launch options, e.g. `ORCHIDUI_SCALE=1.25 %command%` |

Bottles from Flathub works with no extra settings. Everything, including the host, runs inside its sandbox. It's been tested with GE-Proton 11-6.

Steam and umu run Proton inside the Steam Linux Runtime container, and so does the host. If the window doesn't open there, use watch mode with `ORCHIDUI_SESSION_DIR` set to a folder in your home, which the container can see (see [Session folder](#session-folder)), and run `orchid-host --watch --dir` on your desktop. With Steam, `protontricks <id> dotnet48` sets up the prefix for Streamer.bot. On one machine a non-Steam shortcut only started with `PROTON_LOG=1 %command%` in its launch options.

In a Flatpak launcher (Lutris from Flathub, for example), check the host can run inside the sandbox with `flatpak run --command=bash <app id>` and then `OrchidUI/orchid-host --version`. If it says `Failure processing application bundle`, it can't unpack itself into `~/.net`: set `DOTNET_BUNDLE_EXTRACT_BASE_DIR` to a folder inside `~/.var/app/<app id>/`.

CrossOver, Heroic's Wine builds and PlayOnLinux are plain Wine with their own prefix folder and settings, so the steps above apply.

## Scaling, fonts and screen readers

Scaling follows your desktop (per monitor on X11, from `Xft.dpi` or your GNOME or KDE settings), not `winecfg`. `ORCHIDUI_SCALE=1.25` adds extra zoom, and `Ctrl` with `+`, `-` or `0` zooms a single window.

Fonts come from fontconfig. If none of the theme's fonts are installed, the bundled Inter is used.

Every control is exposed over AT-SPI2 with its label as the name and its description as help text, so Orca reads a tab from top to bottom.

## Where things are

| | |
|---|---|
| Host log | `~/.local/state/orchidui/orchid-host.log` (or `$XDG_STATE_HOME/orchidui/`) |
| Host log in a Flatpak launcher | `~/.var/app/<app id>/.local/state/orchidui/orchid-host.log` |
| Session files | See [Session folder](#session-folder). They're deleted as soon as the window connects |
| Settings | Streamer.bot's global variables |

## Testing a build without Streamer.bot

`build/testkit.sh` builds a test kit in `dist/OrchidTest`: the DLL, the host and `orchid-sandbox.exe`, which runs the example windows with a fake Streamer.bot. Pass a folder to copy it there too, e.g. `build/testkit.sh "$WINEPREFIX/drive_c"`, then run `wine orchid-sandbox.exe settings` (or `minimal`) from inside it. Add `--store C:\OrchidTest\store.json` to keep what you save between runs. Each run writes `orchid-sandbox.log` next to it, which helps where a launcher hides the output. The kit's DLL is a development build, so it logs `Development build of OrchidUI` and starts any host.

CI builds and tests the host under Xvfb on Ubuntu 24.04 for every push and pull request, and every night runs the DLL under Wine 9.0 and checks the host connects both through `start /unix` and through `--watch`.
