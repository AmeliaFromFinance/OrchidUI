# Customisation

Everything here is set on `ui` before `ShowUI()`, or loaded from a [JSON file](#json-files). The quick start covers what most windows need; the rest is reference.

## Quick start

```csharp
var ui = new Orchid(CPH, "MY EXTENSION", "1.0.0")
    .SetTheme("Sakura")                    // Orchid, Amethyst, Sakura, Synthwave, Graphite or Daylight
    .SetAccent("#FF4FA3")
    .SetFont("Inter", 13)
    .SetWindowSize(900, 640)
    .SetNavigation(NavigationStyle.Tabs)   // tabs along the top instead of a sidebar
    .SetDensity(Density.Compact)
    .SetAutoSave(true);                    // saves as you go, with a Done button instead of Save

ui.ConfigureTheme(t => t.CornerRadius = 10);
ui.ConfigureWindow(w => w.Resizable = false);
ui.ConfigureLayout(l => l.ShowSearch = false);
ui.ConfigureStrings(s => s.Save = "Apply");
```

`ConfigureTheme`, `ConfigureWindow`, `ConfigureLayout` and `ConfigureStrings` give you every property in the tables below. `LoadThemeFile("themes/my-theme.json")` loads them from a file, and a few [environment variables](#environment-variables) override them for every window.

## Theme: colours

```csharp
ui.ConfigureTheme(t => { t.Accent = "#FF7AC8"; });
```

Colours can be `#RGB`, `#RRGGBB`, `#AARRGGBB` (alpha first), or `transparent`.

You normally set the four base colours below. Every other colour is worked out from them unless you set it yourself, and a light background gets light versions of everything.

### Base

| Property | Default (Orchid) | Used for |
|---|---|---|
| `Background` | `#16111D` | Window background |
| `Accent` | `#F28AC9` | Switches, sliders, primary buttons, focus line, selection pill |
| `Accent2` | `#A57CDB` | Banner gradient, chips, text selection, app badge |
| `Text` | `#F7F2FA` | Main text |
| `Tint` | `0.12` | 0 to 1. How much `Accent2` tints the greys of cards and inputs. `0` is neutral grey |

<details>
<summary>Every other colour</summary>

#### Surfaces

| Property | Derived from | Used for |
|---|---|---|
| `Surface` | Background, slightly lighter, translucent | Content area behind the cards |
| `Pane` | transparent | Sidebar / tab strip background |
| `TitleBar` | transparent | Custom title bar (and native title bar colour on Windows 11) |
| `Card` | light overlay | Setting cards |
| `CardHover` | light overlay | Card under the mouse |
| `CardBorder` | light overlay | Card outlines |
| `CardExpanded` | light overlay | Body of expanded list/table cards |
| `Popup` | Background, lighter, opaque | Dropdowns, menus, tooltips, dialogs, toasts |
| `PopupBorder` | Background, lighter | Their outlines |
| `Divider` | light overlay | Separators |
| `Overlay` | black, translucent | Dimming behind dialogs |
| `WindowBorder` | PopupBorder | Window outline (Windows 10 / Windows 11 frame colour) |

#### Inputs

| Property | Used for |
|---|---|
| `Input` | Text boxes, dropdowns, buttons |
| `InputHover` | The same, under the mouse |
| `InputFocus` | Text box while typing |
| `InputBorder` | Their outlines |
| `InputUnderline` | Bottom line of text boxes, slider track, switch outline |
| `ToggleOff` | Switch fill when off |
| `SubtleHover` | Hover on flat buttons, nav items, list items |
| `Scrollbar` | Scrollbar thumb |

#### Text

| Property | Used for |
|---|---|
| `TextSecondary` | Descriptions |
| `TextMuted` | Placeholders, hints, version text |
| `TextDisabled` | Disabled text |
| `Link` | URLs in descriptions |
| `Selection` | Selected text highlight |
| `Focus` | Keyboard focus ring |
| `Code` / `CodeBackground` | `` `code` `` and `%variables%` in descriptions |

#### Accent states

| Property | Used for |
|---|---|
| `AccentHover` / `AccentPressed` | Primary button states |
| `OnAccent` | Text/icons on accent (picked automatically for contrast) |
| `Chip` / `ChipBorder` | Multi-select chips |

#### Status

| Property | Used for |
|---|---|
| `Success`, `Warning`, `Danger`, `Info` | Icons, error text, invalid-card outline |
| `SuccessBackground`, `WarningBackground`, `DangerBackground`, `InfoBackground` | Info boxes |

#### Title bar and banner

| Property | Used for |
|---|---|
| `CloseHover` | Close button hover (custom title bar) |
| `HeroStart` / `HeroEnd` | Banner gradient (`AddHeaderText`) |
| `HeroText` | Banner text |

</details>

## Theme: fonts, shape, effects

| Property | Default | Notes |
|---|---|---|
| `FontFamily` | `OrchidTheme.DefaultFonts` | Comma-separated. First installed one wins. |
| `HeadingFontFamily` | `OrchidTheme.DefaultHeadingFonts` | Page titles, section titles, dialogs, banner |
| `MonoFontFamily` | `OrchidTheme.DefaultMonoFonts` | Code, colour hex values |
| `FontFile` | none | `.ttf`/`.otf` file or folder. Absolute, or relative to the Streamer.bot folder. Used ahead of `FontFamily`. |
| `FontSize` | `14` | Base size for almost everything |
| `SmallFontSize` | `12` | Descriptions, title bar, tooltips |
| `PageTitleSize` | `28` | Big tab title |
| `SectionTitleSize` | `14` | `AddTitle` |
| `HeroTitleSize` | `30` | Banner title |
| `HeadingWeight` | `SemiBold` | `Normal`, `Medium`, `SemiBold`, `Bold` and so on |
| `CrispText` | `true` | Pixel-snapped text. Set `false` for smoother text on HiDPI. |
| `CornerRadius` | `6` | Cards, inputs, buttons |
| `LargeCornerRadius` | `10` | Dialogs, popups, banner, content area |
| `BorderThickness` | `1` | Control borders |
| `Shadows` | `true` | Drop shadows |
| `BackdropOpacity` | `0.72` | How much `Background` covers Mica/Acrylic (Windows 11) |

```csharp
ui.SetFont("Inter", 13);                     // family and size, falling back to the defaults
ui.SetFontFile("fonts/Inter-Regular.ttf");
```

### Presets

`OrchidTheme.Orchid()`, `Amethyst()`, `Sakura()`, `Synthwave()`, `Graphite()`, `Daylight()`.

```csharp
ui.SetTheme("Sakura");
ui.SetTheme(OrchidTheme.Graphite());
var mine = OrchidTheme.Sakura(); mine.Accent = "#FF4FA3"; ui.SetTheme(mine);
```

## Window

```csharp
ui.ConfigureWindow(w => { w.Resizable = false; });
```

| Property | Default | Notes |
|---|---|---|
| `Title` | `"{NAME} — Settings"` | Window/taskbar title |
| `IconPath` | none | `.png`/`.ico` path or URL. Without one, a badge is drawn in your accent colours |
| `Width` / `Height` | `1180` / `800` | Start size |
| `MinWidth` / `MinHeight` | `0` (automatic) | 720x420 with a sidebar, 460x420 without |
| `MaxWidth` / `MaxHeight` | no limit | |
| `Left` / `Top` | centred | Start position |
| `Resizable` | `true` | `false` gives a fixed size with no maximise |
| `CanMinimize` / `CanMaximize` | `true` | `false` removes the button, and for maximise also blocks double-click and snapping |
| `StartMaximized` | `false` | |
| `Topmost` | `false` | Always on top |
| `ShowInTaskbar` | `true` | |
| `RememberBounds` | `true` | Reopen at the last size, position, zoom and tab |
| `ConfirmClose` | `true` | Ask before closing with unsaved changes |
| `Rounded` | `true` | Windows 11 rounded window corners |
| `Scale` | `1.0` | Zoom the whole UI |
| `AllowZoom` | `true` | Zooming with `Ctrl` and `+`, `-` or `0` |
| `Chrome` | `Auto` | `Custom` (OrchidUI title bar), `Native` (OS title bar). Windows only; Linux and macOS always use the native title bar. |
| `Backdrop` | `Auto` | `Mica`, `MicaAlt`, `Acrylic`, `Solid`. Windows only (Mica needs Windows 11); Linux and macOS windows are solid. |

Shortcuts: `SetWindowSize(w, h)`, `SetResizable(bool)`, `SetMinSize(w, h)`, `SetMaxSize(w, h)`.

## Layout

```csharp
ui.ConfigureLayout(l => { l.Navigation = NavigationStyle.Tabs; });
```

| Property | Default | Notes |
|---|---|---|
| `Navigation` | `Auto` | `Sidebar`, `Tabs` (along the top) or `None`. `Auto` uses a sidebar for two or more tabs |
| `Pane` | `Auto` | Sidebar: `Expanded` or `Compact` (icons only). `Auto` collapses below `CompactPaneBelow` |
| `PaneWidth` | `280` | |
| `CompactPaneBelow` | `1000` | Window width where Auto collapses the sidebar |
| `ContentMaxWidth` | `1000` | Cards stop growing past this. `0` for no limit |
| `ContentPadding` | `36` | Page padding on the left and right |
| `Density` | `Normal` | `Compact`, `Normal` or `Comfortable`: card padding, spacing and control height |
| `InputWidth` | `280` | Text boxes, copy boxes, image inputs |
| `DropdownWidth` | `240` | |
| `SliderWidth` | `220` | |
| `StartTab` | first tab | |
| `RememberTab` | `true` | Reopen on the last tab |
| `ShowPageTitle` | `true` | Big title at the top of each tab |
| `ShowSearch` | `true` | |
| `ShowImportExport` | `true` | |
| `ShowResetAll` | `true` | |
| `ShowAbout` | `true` | |
| `ShowStatus` | `true` | "Unsaved changes" text in the footer |
| `ShowDiscard` / `ShowSave` / `ShowSaveAndClose` | `true` | Footer buttons |
| `ShowDirtyMarkers` | `true` | Dot next to changed settings |
| `ShowTitleBarName` | `true` | Name/version in the custom title bar |
| `Animations` | `true` | Also follows the Windows "show animations" setting |
| `AutoSave` | `false` | Saves shortly after every change, with a Done button instead of Save and Discard |
| `AutoSaveDelayMs` | `600` | |
| `ToastSeconds` | `3.2` | |

For a single field, use `.WithWidth(320)` or `.Stacked()`. In a narrow window, controls move under their label by themselves.

## Strings

Every built-in piece of text is in `ui.Strings`, with `{0}` and `{1}` as placeholders.

```csharp
ui.ConfigureStrings(s =>
{
    s.Save = "Speichern";
    s.SaveAndClose = "Speichern & schließen";
    s.Discard = "Verwerfen";
    s.UnsavedChanges = "Ungespeicherte Änderungen";
    s.WindowTitle = "{0}: Einstellungen";
});
```

The full list is the `UiStrings` class in [`src/OrchidUI/Options.cs`](../src/OrchidUI/Options.cs). Some of them:

| Area | Examples |
|---|---|
| Footer | `Save`, `SaveAndClose`, `Discard`, `Done`, `UnsavedChanges`, `AllSaved`, `SavedAt`, `NeedsAttention` |
| Sidebar and menus | `Search`, `Import`, `Export`, `ResetAll`, `About`, `More` |
| Dialogs | `SaveChangesTitle`, `SaveChangesBody`, `DontSave`, `Cancel`, `Ok`, `Yes`, `No`, `ErrorTitle` |
| Controls | `On`, `Off`, `Browse`, `AddItem`, `AddRow`, `NoEntries`, `PickColor`, `Copy` |
| Units | `Days`, `Hours`, `Minutes`, `Seconds`, `DayUnit` (`d`), `HourUnit` (`h`) |
| Validation | `Required`, `FixBeforeSaving` |

## JSON files

```csharp
ui.LoadThemeFile("themes/my-theme.json");   // path relative to the Streamer.bot folder
ui.LoadThemeJson("{ \"theme\": { \"Accent\": \"#FF7AC8\" } }");
```

A file can hold just theme properties:

```json
{ "Accent": "#FF7AC8", "Accent2": "#A57CDB", "FontSize": 13 }
```

or sections, all optional:

```json
{
  "preset": "Sakura",
  "theme":   { "Accent": "#FF7AC8", "CornerRadius": 8 },
  "window":  { "Width": 900, "Resizable": false, "Chrome": "Native" },
  "layout":  { "Navigation": "Tabs", "Density": "Compact", "AutoSave": true },
  "strings": { "Save": "Apply" }
}
```

Keys aren't case-sensitive, and enum values are written as text (`"Tabs"`). The `preset` is applied first and the rest on top of it. Keys starting with `$` are ignored, so `"$comment"` works for notes. Unknown keys are listed in Streamer.bot's log and skipped.

The [`themes`](../themes) folder has ready-made files, and [`example-everything.json`](../themes/example-everything.json) uses every key.

## Environment variables

These are read when a window opens and override what the action sets.

| Variable | Values |
|---|---|
| `ORCHIDUI_BACKDROP` | `solid`, `mica`, `micaalt`, `acrylic` or `auto` |
| `ORCHIDUI_SCALE` | `0.5` to `3`, extra zoom on top of the system's scaling |
| `ORCHIDUI_ANIMATIONS` | `0` or `1` |
| `ORCHIDUI_THEME` | A preset name, or the path to a theme `.json` |

## Icons

Anywhere an icon is accepted (`Tab(name, icon)`, `SetTabIcon`, `.WithIcon`) you can pass:

| Value | Shows |
|---|---|
| `Icons.Heart` (or `"heart"`) | Built-in vector icon |
| `"🎮"`, `"A"` | That text, centred |
| `"M12,2 L22,22 H2 Z"` | A filled 24x24 SVG path, like Material Design Icons |
| `"stroke:M4,12 H20"` | An outlined 24x24 path with round caps, like Lucide or Tabler |
| A name from `Icons.Register` | Your own icon |
| A Segoe Fluent or MDL2 glyph (e.g. `""`) | The matching built-in icon |

```csharp
Icons.Register("twitch", "M2,2 H22 V22 H2 Z", stroke: false);
Icons.Register("logo", "M4,12 H20", stroke: true, grid: 16, strokeWidth: 1.5);
foreach (var name in Icons.Names) CPH.LogInfo(name);
```
