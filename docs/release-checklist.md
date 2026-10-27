# Release checklist

CI tests the code; this is what needs a person. Use the files from the release workflow (or the draft release), not a local build, since the DLL only trusts the hosts it was built with.

Go through it on Windows 11, on Linux with Streamer.bot in Bottles, and on macOS with Streamer.bot in Whisky or CrossOver, all with real Streamer.bot. Other Linux setups (plain Wine, Lutris, Steam, umu) can be checked with the test kit described in [linux.md](linux.md#testing-a-build-without-streamerbot).

## Each platform

- [ ] `examples/1 - Settings Action.cs` opens and every tab draws.
- [ ] Streamer.bot's log says `Settings window opened`, with a pipe on Windows and `127.0.0.1` elsewhere.
- [ ] Change a toggle, a slider, a duration and a colour, then Save. Reopen, and the values are back.
- [ ] `examples/2 - Runtime Action.cs` reads them.
- [ ] Clearing a required text box turns the card red, disables Save, and makes closing ask first.
- [ ] In `examples/3 - Minimal.cs`, auto-save works and Done closes the window.
- [ ] `examples/4 - Customised Window.cs` shows the Daylight theme, tabs along the top and its own strings.
- [ ] Import, Export, Reset to defaults and Copy diagnostics in the menu next to Save all work.
- [ ] `Ctrl` (`Cmd` on macOS) with `S`, `F`, `2`, `Tab`, `+`, `-` and `0` works, and so does `Esc`.
- [ ] On a 150 % or 200 % monitor the text is sharp, and the window reopens at the same size.
- [ ] A screen reader (Narrator, Orca or VoiceOver) reads the names and descriptions on a whole tab.
- [ ] Killing `orchid-host` makes Streamer.bot log `closed unexpectedly` and carry on. Running the action again opens a new window.
- [ ] Linux: with `ORCHIDUI_NO_LAUNCH=1`, `orchid-host --watch` opens the window.
- [ ] macOS: the quarantine steps in [macos.md](macos.md) still work.

## Release files

- [ ] `SHA256SUMS` matches the downloads.
- [ ] `gh attestation verify <file> --repo AmeliaFromFinance/OrchidUI` passes for each file.
