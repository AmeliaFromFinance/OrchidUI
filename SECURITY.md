# Security

## Reporting a vulnerability

Please report vulnerabilities privately through [GitHub security advisories](https://github.com/AmeliaFromFinance/OrchidUI/security/advisories/new) rather than in a public issue. I aim to reply within a week.

## How OrchidUI is protected

OrchidUI is two programs: `OrchidUI.dll` inside Streamer.bot, and `orchid-host`, which draws the window. They talk over a local connection. The two things to protect against are another program on the same machine using that connection, and a broken or fake host harming Streamer.bot.

**The connection.** On Windows it's a named pipe that only your user account can open. Under Wine it's a TCP port on `127.0.0.1`, and the host has to present a random 256-bit token, compared in constant time. Either way the DLL accepts one client and then stops listening.

**The session file.** The token reaches the host in a small file that expires after 30 seconds and is deleted as soon as it's read. On Windows it's in your own `%LOCALAPPDATA%`. Under Wine the DLL can't set Unix permissions, so it uses a folder other users can't enter, such as `$XDG_RUNTIME_DIR`. The host refuses a file that others can write to, or read when no folder above it is private.

**Which host runs.** Release DLLs contain the SHA-256 of the hosts built with them and won't start anything else. Releases also come with `SHA256SUMS` and GitHub build provenance. Builds you make yourself don't pin unless you follow [docs/building.md](docs/building.md).

**Messages.** Each message is capped at 1 MiB and 32 levels of JSON nesting, checked against a schema for its type, and dropped along with the connection if anything is wrong. The connection runs on the DLL's own threads with timeouts and a bounded queue, so a stuck or hostile host can't block an action. If nothing is heard for 15 seconds, the window is treated as closed.

**Files.** The host never opens a file the DLL names. For header images, fonts, icons and image previews, the DLL reads the file itself, only if it's a regular local file, and stops at 1 MB (512 KB for a preview) whatever size the file claims. Previews are only made from files that look like images by both extension and content. Import reads a local file of at most 4 MB and only the keys the window has. Export only writes `.json` files on a local disk.

**Images.** The host reads an image's dimensions from its header before decoding it, and refuses anything over 16 million pixels, so a small file that would unpack into a huge one is never decoded.

**Links and themes.** Only `http` and `https` links are opened. Theme files can only set known keys; anything else is logged and ignored.

**Logs.** Password values are masked in `LogSettings()` and in the diagnostics, and the host log never contains setting values.

The tests for these are mostly in `SessionTests`, `ProtocolTests`, `LaunchTests`, `FileTests` and the two `RegressionTests` classes.

## Known limits

- Passwords are saved as plain text in Streamer.bot's global variables. They're only masked on screen and in logs.
- Development builds start any `orchid-host`. Protection against a fake host (for example one that picks the paths Import and Export use) relies on the release pin.
- A URL typed into an image input is fetched by the host, so that server sees your IP address, as it would from a browser.
- `orchid-host --watch` opens a window for any session file in its folder. The folder is made private (`0700`) and files writable by others are refused, so only your own user can add one.
- Under Wine, the TCP port is reachable by any local user for the moment before the host connects. The token is what protects it.
- Programs already running as your user are out of scope: they can read Streamer.bot's memory and global variables directly.
- The macOS host is ad-hoc signed, not notarised.
