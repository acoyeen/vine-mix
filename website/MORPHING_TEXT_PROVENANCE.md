# Morphing headline provenance

Research and adaptation date: 23 October 2026.

The landing headline uses a small native JavaScript adaptation of **Morphing Text**, the Magic UI component by Dillion Verma requested by the maintainer through [21st.dev](https://21st.dev/@dillionverma/components/morphing-text). That listing identifies the component as MIT licensed and links to the [Magic UI component documentation](https://magicui.design/docs/components/morphing-text). The documentation credits [@luis-code](https://luis-code.vercel.app).

The source and license were read directly from Magic UI's repository at revision `efe76fc5755eca1142dd4f74c1d6f0ace2887e3c`:

- [Component source](https://github.com/magicuidesign/magicui/blob/efe76fc5755eca1142dd4f74c1d6f0ace2887e3c/apps/www/registry/magicui/morphing-text.tsx)
- [MIT license](https://github.com/magicuidesign/magicui/blob/efe76fc5755eca1142dd4f74c1d6f0ace2887e3c/LICENSE.md), distributed locally as `dist/assets/licenses/MagicUI-MIT.txt`

`dist/morphing-text.js` preserves the overlapping text, blur interpolation `min(8 / fraction - 8, 100)`, opacity interpolation `fraction ** 0.4`, and SVG alpha-threshold technique. Blur distances are normalized to the upstream 96px large-display type size: the interpolated value is divided by 96 and expressed in `em`. The combined-layer finishing blur similarly uses `0.00625em` instead of the upstream `0.6px`. These values reproduce the upstream distances at 96px while keeping morphing letterforms visible at VineMix's smaller responsive headline sizes. With fixed pixel blur, browser review found that these smaller letterforms disappeared near the midpoint.

The threshold's last matrix row is `0 0 0 255 -140`; its RGB rows are the identity matrix. The reference applies `url(#threshold) blur(0.6px)` to the combined text layer. VineMix uses a distinct filter identifier and places its soft drop shadow on an outer wrapper so the threshold does not harden the shadow.

The React hook and continuous frame loop were replaced with a native controller. It starts paused, displays the first phrase crisply, and exposes `setPaused(boolean)` and `destroy()`. Once enabled, the first transition begins after five seconds. Subsequent transition starts are five seconds apart under normal foreground scheduling, with a one-second morph and four seconds of readable text. Timers handle the hold; animation frames run only during a morph. A pause cancels both, settles the selected phrase, and resumes with a fresh five-second hold. The host supplies page visibility, viewport, reduced-motion and manual-pause decisions.

Phrases use a randomized shuffled bag. Every distinct phrase appears once per bag, including the initial phrase in the first bag; a new bag cannot begin with the last phrase of its predecessor. Empty and one-phrase inputs remain static. These sequencing and lifecycle changes are local adaptations, not features claimed for the upstream component. No React or other runtime dependency was added.
