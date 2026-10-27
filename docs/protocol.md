# Session protocol (version 1)

Each open window has one private connection between `OrchidUI.dll` (the shim, inside Streamer.bot) and `orchid-host` (the host). Messages are length-prefixed JSON of at most 1 MiB. The shim owns all the state and runs every callback; the host draws the window and reports what the user did. The code for all of this is in [`src/OrchidUI.Protocol`](../src/OrchidUI.Protocol), which both programs compile in.

## 1. Transport

| Streamer.bot runs on | Transport | Who can connect |
|---|---|---|
| Windows | Named pipe `orchidui-<session id>` | Only the current user's SID (pipe ACL on .NET Framework, `PipeOptions.CurrentUserOnly` on .NET) |
| Wine (Linux, macOS) | TCP on `127.0.0.1`, port picked by the OS | Anyone on the machine can reach the port, so the 256-bit token is what keeps them out |

- The shim accepts one client, then stops listening. A peer that isn't on loopback is dropped.
- Why no pipes under Wine: a Windows pipe inside Wine can't reach a native .NET named pipe (a Unix domain socket), and Wine's AF_UNIX support is Staging-only.
- `ORCHIDUI_TRANSPORT=pipe|tcp` overrides the choice. The tests run both on every OS.

## 2. Session file

Before starting the host, the shim writes `session-<id>.json`:

```json
{"protocol":1,"id":"5f2c0a9b1d7e","transport":"tcp","pipe":null,"port":45985,
 "token":"<64 hex characters>","shimPid":1234,"expiresAt":1793000000}
```

`expiresAt` is Unix milliseconds, 30 s after writing.

The folder it goes in:

| Where | Folder |
|---|---|
| Windows | `%LOCALAPPDATA%\OrchidUI\sessions` |
| Wine, in order | 1. `$XDG_RUNTIME_DIR/orchidui` |
| | 2. `/run/user/<uid>/orchidui` if it exists (uid read from `/proc/self/status`; for Wine builds that hide `XDG_RUNTIME_DIR`) |
| | 3. `$TMPDIR/orchidui` when `TMPDIR` is per-user (macOS) |
| | 4. `~/.cache/orchidui/sessions`, with a warning, when `HOME` is visible |
| | 5. The prefix's `AppData\Local\OrchidUI\sessions` |
| Anywhere | `ORCHIDUI_SESSION_DIR`, if set |

Wine paths are written through `Z:`.

How the file is handled:
- Written to a temporary name, then renamed.
- Where the shim runs on .NET (tests, development) the file is `0600` in a `0700` folder.
- The release DLL can't set Unix permissions (it runs on .NET Framework under Wine). Its files get Wine's default mode, usually `0644`. That's why the folder must sit inside one other users can't enter, and why the host checks.
- The host reads the file and deletes it at once. It refuses (and still deletes) a file that:
  - is group- or other-writable;
  - other users could read, unless a folder above it is closed to group and others
    (`XDG_RUNTIME_DIR` is `0700`, macOS's per-user `TMPDIR` too, and `--watch` makes its folder `0700`);
  - has another protocol major, a malformed token, a bad port or pipe name, or has expired.
- The shim deletes it as soon as the handshake succeeds, and refuses a late hello.

The host gets it as `orchid-host --session <path>`. Under Wine the shim runs
`start.exe /unix <host> --session <path>`. If that's broken (Wine 9.5 to 10.0-rc2),
`orchid-host --watch` polls the folder every 250 ms and starts a host per new file.

## 3. Frames

Each message is a 4-byte big-endian length, then that many bytes of UTF-8 JSON:

```json
{"v":1,"id":7,"type":"change","payload":{"field":"f12","key":"enabled","value":false}}
```

| Rule | |
|---|---|
| `v` | Must be `1` |
| `id` | Non-negative integer, increasing per sender. For logs only |
| Size | Over 1 MiB (1 048 576 bytes): the reader stops and the link closes |
| `type` | Must be one this side may receive |
| `payload` | An object that passes the schema for its type: required fields, JSON types, ids `[a-z0-9]{1,32}`, strings ≤ 64 KiB |
| Nesting | Deeper than 32 is rejected |
| Text | Written as UTF-8, unpaired surrogates replaced by U+FFFD. Invalid UTF-8 coming in is rejected |

A frame that fails any rule is never acted on, and the link closes.

## 4. Handshake

```
host                                          shim
 |--- connect -------------------------------->|  one client only
 |--- hello {token, host{version,os,windowSystem}} ->|
 |                                             |  constant-time token compare, expiry check
 |<-- open {spec} -----------------------------|  or: refused {reason}, then close
 |   (window shown)                            |
 |--- ready {} ------------------------------->|  ShowUI() returns
```

- The hello must arrive within 5 s of connecting.
- `refused` carries a short reason for the host log. Then the connection closes.

## 5. Messages

### From the host to the shim

| type | payload | meaning |
|---|---|---|
| `hello` | `token`, `host?` | First frame (above) |
| `ready` | | The window is on screen |
| `change` | `field`, `key`, `value`, `native?` | The user changed a value. A slider with a switch sends its two keys separately. `native: true` means `value` is a path from the host's file dialog: the shim translates it (under Wine, from `/home/...` to `Z:\home\...`) and sends it back |
| `click` | `field` | A button was pressed |
| `command` | `name`, `field?`, `path?`, `native?` | See [commands](#commands) |
| `tab` | `tab`, `state?` | A tab was shown (copy boxes refresh) |
| `dialogResult` | `request`, `index` | Answer to a `dialog`. `-1` = dismissed |
| `closeRequest` | `state?` | The user tried to close. `state` = `{w,h,x,y,max,zoom,tab,scale}`: size and position in DIP, `scale` of the screen it's on |
| `log` | `level`, `message` | A host problem worth showing in Streamer.bot's log |
| `ping` | | Heartbeat |

#### Commands

| `name` | Sent by |
|---|---|
| `save`, `saveAndClose`, `discard`, `done` | Footer buttons, `Ctrl+S` |
| `resetField` | Right-click Reset to default (`field` set) |
| `refresh` | The ↻ button of a refreshable dropdown or multi-select (`field` set) |
| `resetAll`, `about` | The More menu (next to Save) |
| `import`, `export` | The More menu, with the `path` the host's dialog picked (`native` as for `change`). The shim only reads a regular local file for `import`; `export` always writes a `.json` file on a local disk |

### From the shim to the host

| type | payload | meaning |
|---|---|---|
| `open` | `spec` | The whole `WindowSpec`. Embedded files (font, header image, window icon: read by the shim, 1 MB each at most) are dropped largest first, with a warning in Streamer.bot's log, if the frame would pass 1 MiB. Image previews are dropped before those |
| `patch` | `values?`, `states?`, `status?`, `fields?` | What changed: values, `FieldState`s (visible, enabled, error, display, dirty and so on), footer status, replaced field specs (e.g. new dropdown options). See [image previews](#image-previews) |
| `toast` | `message`, `kind?` | Show a toast |
| `dialog` | `request`, `title`, `message`, `buttons`, `primary?` | Modal dialog, answered by `dialogResult` |
| `dialogCancel` | `request` | The shim stopped waiting (timeout) |
| `focus` | | Bring the window to the front (`AlreadyOpened`) |
| `jump` | `field` | Show the tab with this field and scroll to it (first validation error) |
| `close` | | Close now |
| `refused` | `reason` | Handshake failed |
| `copy` | `text` | Put `text` on the clipboard (About's Copy diagnostics) |
| `ping` | | Heartbeat |

#### Image previews

- An image input's state carries `image` (bytes, a URL, or `null`) only when the preview changed.
  No `image` key = keep the one you have.
- When a frame would pass 1 MiB, previews are left out largest first. They follow in the next patches.

### What happens on a change

1. The host sends `change`.
2. The shim normalises the value to its stored form and runs `OnChange`.
3. It re-checks `ShowWhen`, `EnableWhen`, `Required`, `Validate` and `WithFormat`.
4. It sends one `patch` with only what changed.
5. If normalising changed the value (a clamp, a dropdown fallback), the patch carries it back.
   The host shows it without echoing it again.

### Closing

Closing is the shim's decision. On `closeRequest` it:
1. checks for unsaved changes and errors (asking through a `dialog` if needed);
2. saves the window state to `orchid_{NAME}.window`;
3. sends `close`.

## 6. Liveness

- Both sides send `ping` every 5 s.
- If nothing is heard for 15 s, the link is dead. The shim marks the window closed and logs
  `closed unexpectedly`. The next `ShowUI()` starts a new host.
- All link I/O runs on the shim's own threads, never the action's. The outgoing queue is bounded:
  the link closes rather than block.

## 7. Versioning

- `protocol` in the session file and `v` in every frame are the major version.
- A host refuses a session file with another major. A frame with another `v` is rejected.
- Adding a new optional payload field keeps the major. Anything else bumps it.
- Each release DLL only starts the hosts built with it anyway.
