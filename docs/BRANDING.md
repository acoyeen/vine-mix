# VineMix visual identity

Updated 28 October 2026 for the monochrome refresh planned for Beta 6. The
native workspace uses the split VM mark, IBM Plex typography and forest-green
actions. The logo follows the website's single-colour treatment while controls
retain their operational colours. The [original implementation plan and
evidence](BRANDING_IMPLEMENTATION_PLAN.md) records the earlier two-colour identity.

## Naming

For independent forks, see [Crediting VineMix](../ATTRIBUTION.md). A short text
acknowledgement and original-project link are the requested attribution; keeping
the logo or naming a fork VineMix is not required. The visual conventions below
describe the official product.

Use **VineMix**, without a space, in product copy, accessibility text, release
titles and documentation. Use `vine-mix` for the repository, Codex project and
checkout folder. Language-specific names retain their normal syntax, including
`VineMix.Desktop` and `VINEMIX_*`. The website remains `vinemix.app`.
[Compatibility identifiers](REBRAND_COMPATIBILITY.md) and historical evidence
retain their original spelling where it is required for accuracy.

## Mark and assets

The symbol consists of a separate V above an M. Their long diagonals share a
45-degree angle; a visible gap separates the pieces. Small corner radii soften
the shape. The application caption pairs the symbol with the approved outlined
**VineMix** wordmark in one foreground colour, with full-width square i dots
aligned to the capital letters and shorter i stems. Both symbol pieces and all
letters share the live text brush, including Windows high-contrast foreground.
The website reference is the monochrome header/hero in
`website-redesign/src/index.html` and its white `src/favicon.svg`; the geometry
and existing caption proportions are preserved.
Use **VineMix** consistently in Windows titles, accessible identity and tray commands.

The caption centres the 106 × 20 DIP wide artwork across the full visible header,
with equal space above and below. Its transparent 122 × 20 DIP button leaves the
surrounding caption available for dragging and the native resize border clear.
The button supports keyboard focus and opens
[vinemix.app](https://vinemix.app), with the accessible name **Open VineMix
website**. The lettering comes from the packaged vector, so it needs no font
fallback and retains the approved custom letter shapes.

The website mark stays transparent on hover and press, without a tooltip,
underline or mouse highlight. Its accessible name remains available. A separate
outline appears for keyboard navigation and follows the live theme.

An 8 × 8 DIP connection dot sits at the far left, vertically centred in the same
header, with the current compact release version beside it. Green means the interface is connected to the audio engine and ready to
accept edits; it dims during disconnection or restart. Its hover text and
accessible name expose the current connection status. Warnings occupy the left
half; saved status, Settings and window actions occupy the right without shifting
the central mark.

- [Scalable transparent mark](../desktop/VineMix.Desktop/Assets/Branding/mark.svg)
- [Scalable transparent wide logo](../desktop/VineMix.Desktop/Assets/Branding/wordmark.svg)
- [Application icon](../desktop/VineMix.Desktop/Assets/Branding/app.ico)
- [Tray icon](../desktop/VineMix.Desktop/Assets/Branding/tray.ico)
- [256px icon preview](../desktop/VineMix.Desktop/Assets/Branding/app.png)

Icons contain 16, 20, 24, 32, 40, 48, 64, 128 and 256px frames. Their backgrounds
are transparent; both pieces use the in-app logo's soft white `#E8EBF2` in the
taskbar, executable/window icon and tray. This follows the maintainer's
28 October refinement of the initial grey icons. No background plate is added;
visibility on a light desktop background depends on the background image.
The mark uses 80% of the canvas height, up from 72%, making it about 11% larger
while retaining clear transparent margins at every supplied size.
Shortcuts, Setup and Installed apps inherit the packaged application icon.
The portable SVG artwork is white. Recreate the standalone mark and icons with
`scripts/generate-brand-assets.ps1`; keep its paths synchronized with the native
vector in `Controls/BrandMark.cs`. The generator also normalizes the existing
`wordmark.svg` to one ink without changing its custom lettering; it is drawn by
`Controls/BrandWordmark.cs`.

## Typography and colour

Use bundled IBM Plex Sans Regular, Medium and SemiBold for labels, node titles,
menus, search, help and tooltips. Use IBM Plex Mono Regular for exact numeric
entries, detector readouts and menu shortcuts. Keep native symbol fonts for
window/expander glyphs and normal Unicode fallback for user-provided names.
Fonts are process-local; no system installation is required. Source, hashes and
OFL terms are in [font provenance](IBM_PLEX_FONTS.md).

Menu checkmarks use drawn strokes in the shared menu template instead of a font
character, keeping their shape consistent across Settings and other menus.

| Role | Normal palette | Meaning |
| --- | --- | --- |
| In-app logo | `ConsoleTextBrush` (`#E8EBF2`) | One foreground for the entire identity |
| Windows icon | `#E8EBF2` | Soft white matching the normal in-app logo |
| Portable SVG logo | `#FFFFFF` | Website-aligned monochrome artwork |
| Primary action / active Mono | `#2D6A4F` | Deliberate interaction |
| Primary action text | `#F5F8F6` | Readable light label |
| Primary action hover | `#347B5B` | Hover feedback |
| Window / panels | `#0C0D0F` / `#15181E` | Quiet working surfaces |
| Text / secondary text | `#E8EBF2` / `#A5ADBA` | Information hierarchy |
| Audio cable / socket | `#78BE98` | Audible audio route |
| Sidechain cable / diamond socket | `#B8A0D8` | Ducking detector only |
| Control cable / square socket | `#A0A8B3` | Control connection |

Retain distinct muted category headers, explicit Mute/Bypass labels and
green/amber/red meter zones. Do not reuse action green for warnings or replace
the sidechain role with audio green. High contrast replaces operational colours
with Windows system colours through dynamic resources.

Each menu root owns one help popup for its rows and nested menus. Moving between
rows updates the explanation; entering or leaving a submenu clears the previous
owner's help. Closing the menu also clears the popup and pending help. The shared
handoff policy covers Settings, Add and context menus, while ordinary controls
use the same application-wide ownership rules.

## Branding inventory

| Surface | Implementation point |
| --- | --- |
| Caption wide logo, website button and Windows title/icon | `MainWindow.xaml`, `MainWindow.cs`, `Controls/BrandWordmark.cs`, `Controls/BrandMark.cs` |
| Executable/taskbar icon and product metadata | `VineMix.Desktop.csproj`, `Assets/Branding/` |
| Setup, Installed apps and default shortcuts | `installer/VineMix.iss`, application icon embedded in `VineMix.exe` |
| Tray icon, menu font, tooltip and commands | `App.xaml.cs`, `TrayBranding.cs` |
| Interface, detached menus and tooltips | `App.xaml` shared font/control styles |
| Live palette and Windows high contrast | `Controls/ConsoleTheme.cs`, `Theme.xaml` |
| Primary Add action and empty workspace | `GraphWorkspace.cs` |
| Node labels, active Mono and detector readouts | `GraphNodeView.cs` |
| Numerical entries | `NodeNumericEntry` style, `Controls/RotaryValueControl.cs` |
| EQ and Voice Color drawn text | Shared inherited font in their drawing controls |
| Search, Help, activity and context menus | Shared styles on the existing native views |
| Settings help across rows and nested menus | `MenuHoverHelp.cs`, attached by `MainWindow.cs` |
| User-facing project copy and screenshots | README, user/node guides, issue template, `docs/images/` |
| Credits and redistributable notices | `THIRD_PARTY_NOTICES.md`, `licenses/`, `scripts/build.ps1` |

Beta 1 uses VineMix for executable, assembly, namespace, startup and process
identity. Personal data is copied from the legacy AudioMixer location to
`%LOCALAPPDATA%\VineMix`, preserving originals. Historical reports and intentional
compatibility readers retain their original context.

## Verification

The separate verification build's `--render-design` checks actual embedded fonts and weights, glyph coverage,
numeric alignment, menu/tooltip inheritance, drawn labels, two separate logo
pieces sharing one ink, all icon frames' soft-white colour and transparency,
and high-contrast restoration. Existing suites
continue to cover every creatable node, routing/editing, numeric entry, meters,
search/help, window dimensions, zoom and DPI. See [testing](../TESTING.md).

The images below record the original branding implementation; they predate the
monochrome refresh. Windows Volume Mixer session identity is a separate
[tracked refinement](TWEAKS.md#tweak-011), not verified by replacing desktop icons.

![Original native workspace branding](images/workspace.png)

![Settings typography](images/branding-settings.png)
