# Changelog

All notable changes to OrchidUI are recorded here. Versions follow [Semantic Versioning](https://semver.org/).

## 1.0.0 (unreleased)

First release.

- `OrchidUI.dll` for Streamer.bot C# actions, with text, choice, number, duration, colour, list, table, file, folder and image fields, buttons, conditions, validation and auto-save.
- `orchid-host`, which draws the window natively on Windows (x64), Linux (x64 and arm64) and macOS (Apple Silicon), including when Streamer.bot runs under Wine.
- Six themes, theme files and full window customisation.
- Import, export and reset to defaults from the window, plus dialogs and toasts from your code.
- `orchid-host --watch` for Wine versions with a broken `start /unix`.
- `orchid-sandbox` and `build/testkit.sh` for testing without Streamer.bot.
