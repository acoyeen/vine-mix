# Bundled IBM Plex fonts

Researched and verified on **20 October 2026**. VineMix bundles unmodified static
TrueType files from the official [IBM/plex repository](https://github.com/IBM/plex),
pinned to commit
[`71f558992315a12ca1f06a2215d57962a1992162`](https://github.com/IBM/plex/tree/71f558992315a12ca1f06a2215d57962a1992162).
Only the local filenames differ from upstream; the font binaries are unchanged.
No system font installation is required.

| Local asset in `desktop/VineMix.Desktop/Assets/Fonts/` | Upstream source at the pinned commit | WPF family / face | Weight | Font version |
| --- | --- | --- | --- | --- |
| `IBM-Plex-Sans-Regular.ttf` | [IBMPlexSans-Regular.ttf](https://github.com/IBM/plex/blob/71f558992315a12ca1f06a2215d57962a1992162/packages/plex-sans/fonts/complete/ttf/IBMPlexSans-Regular.ttf) | IBM Plex Sans / Regular | 400 | 3.005 |
| `IBM-Plex-Sans-Medium.ttf` | [IBMPlexSans-Medium.ttf](https://github.com/IBM/plex/blob/71f558992315a12ca1f06a2215d57962a1992162/packages/plex-sans/fonts/complete/ttf/IBMPlexSans-Medium.ttf) | IBM Plex Sans / Medium | 500 | 3.005 |
| `IBM-Plex-Sans-SemiBold.ttf` | [IBMPlexSans-SemiBold.ttf](https://github.com/IBM/plex/blob/71f558992315a12ca1f06a2215d57962a1992162/packages/plex-sans/fonts/complete/ttf/IBMPlexSans-SemiBold.ttf) | IBM Plex Sans / SemiBold | 600 | 3.005 |
| `IBM-Plex-Mono-Regular.ttf` | [IBMPlexMono-Regular.ttf](https://github.com/IBM/plex/blob/71f558992315a12ca1f06a2215d57962a1992162/packages/plex-mono/fonts/complete/ttf/IBMPlexMono-Regular.ttf) | IBM Plex Mono / Regular | 400 | 2.005 |

Use the resource family suffixes `#IBM Plex Sans` and `#IBM Plex Mono`. WPF was
checked against the local directory: all four requested faces resolve to their
actual files without simulated weight or style. The legacy Win32 family names
for the heavier Sans files are `IBM Plex Sans Medm` and `IBM Plex Sans SmBld`;
their typographic family is consistently `IBM Plex Sans`.

## Coverage and rendering considerations

The three Sans files each expose 895 mapped Unicode code points. Mono exposes
1,049. Verification covered ASCII letters/digits, accented Latin, degree,
plus/minus, micro sign, multiplication, Greek alpha/mu, Cyrillic A, en/em dashes,
bullet, ellipsis, directional arrows, mathematical minus, infinity and
approximately-equal signs. Sans covers that entire sample. Mono covers it except
Greek alpha (`U+03B1`) and Greek mu (`U+03BC`); the micro sign (`U+00B5`) is present.

Sans also covers the non-ASCII punctuation, checkmark and arrow glyphs in the
current desktop source. The test fixtures contain CJK names and emoji that need
ordinary WPF fallback. The former fullwidth plus (`U+FF0B`) on the empty-canvas
Add node button was outside these fonts' maps and was replaced with ASCII `+`
during integration. Do not disable font fallback
for user-provided device or node names. Do not use these fonts as substitutes for
the application's dedicated icon glyphs.

The advance width for digits `0` through `9` is uniformly 0.6 em in all four
files. Mono is intended for numerical readouts and shortcuts. Only its regular
weight is bundled; requesting a heavier Mono weight can invoke synthesis. Only
upright Sans 400/500/600 are bundled; bold or italic requests also need care.
Native-size application rendering, layout and display-scaling checks remain
necessary after integration.

## License

The fonts are copyright 2017 IBM Corp., with Reserved Font Name **Plex**, under
the **SIL Open Font License 1.1**. The exact upstream
[LICENSE.txt](https://github.com/IBM/plex/blob/71f558992315a12ca1f06a2215d57962a1992162/LICENSE.txt)
is retained as both `Assets/Fonts/IBM-Plex-LICENSE.txt` and
`licenses/IBM-Plex-OFL.txt`. Include this license with redistributed font files.
The fonts are bundled as part of VineMix; IBM's identity and endorsement are
not implied.

## SHA-256

```text
975dcda37d80f038dcd143c22e33ca2d97a0cc5a929aace1c749153b0fe1afa5  IBM-Plex-Sans-Regular.ttf
331c8639d7598b2cde62a911a71db195e30cb655cd6bdf2e324a7e984955f907  IBM-Plex-Sans-Medium.ttf
a20caf8286023a6a7a85e40b1d2a4ae9fc3e3b1f9eda8f4c542dd4986af67bb1  IBM-Plex-Sans-SemiBold.ttf
7c6fbddca4b700be918f5f6183d9bd4464fa427fe435f0b480d77fe2bb8c5a43  IBM-Plex-Mono-Regular.ttf
7e6b2818edbd8f6a01ae80641cc8f16a51080d08fb4e532be3a0b6f74adb07da  IBM-Plex-LICENSE.txt / IBM-Plex-OFL.txt
```

Verification used the TrueType table directory and naming records plus Windows
WPF `GlyphTypeface`, character maps and `Typeface.TryGetGlyphTypeface`. All four
files are static fonts (no `fvar` table), with normal upright styles and
installable embedding permission reported in their metadata. These checks
confirm the bundled assets and local WPF resolution, not the final packaged
application's rendering.
