# Forest shader provenance

Verified on **20 October 2026**.

The local landing-page shader adapts **frorest-green** by **Allan Mesquita Brito**,
published on 21st.dev on 13 September 2026. Its recipe identifier is
`4314dd3b-67c5-4cfd-b2d2-cc1184dddc47`, matching the maintainer's selected reference.

- [Canonical component and licence declaration](https://21st.dev/@allanmesquitab/components/frorest-green)
- [Original recipe editor](https://21st.dev/community/shaders/editor?from=4314dd3b-67c5-4cfd-b2d2-cc1184dddc47)
- [Published live-preview bundle used for extraction](https://cdn.21st.dev/allanmesquitab/frorest-green/default/bundle.1789265735080-5e869e70-885d-40d2-967e-c9d9ce7e18cc.html)

The canonical page identifies the style as **Waves**, generated using the
21st.dev Shader Builder, and declares **MIT License**. The publicly served page
metadata connects this component to the exact recipe identifier. The published
bundle contains its readable vertex/fragment GLSL and concrete preset values.
The component's separate registry source field was empty when researched; the
GLSL was extracted from the public preview bundle without executing its code.

The local file `dist/forest-shader.js` preserves the complete fragment shader
and original palette and configuration. Its precision selection, packed
uniforms, and conditional mediump seed/hash guards are part of the published
upstream shader, not VineMix changes. No GLSL equations, guards, or comments
were modified. The fragment contains 9,324 characters; the SHA-256 digest of
both the upstream extracted source and the local shader string passed to
`gl.shaderSource`, measured over UTF-8 text, is:

```
b46a4b09ee628eeae8d3bd003459fdfdbc024aa500a844455d0659f9d422ab49
```

The recipe palette is `#0D1B0F`, `#3E8E41`, `#C7F464`, and `#FFFDE1`. The
configuration is scale 1.26, intensity 0.35, paramA 0.28, warp 0, detail 1.824,
contrast 1.005, brightness 0, saturation 1, hue 0, vignette 0, blur 0, grain
0.042, seed 1, rotation 0, offsets 0, drift 0, OKLab 0, and time scale 0.575.
The preview initializes cursor presence to 0, so its stored cursor effect 2,
strength 0.65, and radius 0.46 do not create pointer interaction. The local
version preserves that behavior.

The React lifecycle wrapper and bundled application runtime were replaced with
a small native ES module. This is a wrapper adaptation, not a change to the
shader recipe. It renders one frame on mount, starts paused, and
exposes `setPaused(boolean)` and `destroy()`. The parent controls animation
policy. Motion uses at most 30 drawing frames per second and freezes time while
paused. On 23 October 2026, the wrapper's DPR ceiling was reduced from 2 to 1.5
and its pixel budget from 2 million to 900,000 to reduce the cost of continuous
background animation. This reduces the maximum fragment count per drawing
frame by 55%; it is a workload limit, not a measured device performance claim.
The soft waves are scaled to fill the viewport while the separate logo and
page text retain their normal display resolution. The shader source, palette,
and animation time scale remain unchanged.
The final capped render dimensions are rounded down instead of the upstream
wrapper's rounding to nearest, ensuring that the budget is never exceeded.
CSS dimensions are cached at mount and refreshed on ResizeObserver/window
resize notifications, so ordinary animation frames do not read layout.
Shader compile/link failures, unavailable WebGL, context loss, resize, and
resource cleanup are handled locally. No package, external request, embedded
preview, remote script, or third-party runtime is required by this module.

Parent-page animation policy is owned by `hero.js`; the wrapper starts paused
until that parent enables it. The 21 October five-second settling behavior is
superseded by the maintainer's 23 October request for continuous animation.
Reduced-motion and offscreen/hidden-tab pausing remain appropriate parent
controls and do not change the shader recipe.

## Licence provenance

The component page explicitly declares MIT. Its public metadata supplies no
separate licence URL or copyright notice. `dist/assets/licenses/frorest-green-MIT.txt`
therefore contains the standard MIT permission/warranty text plus a clearly
labelled attribution and source note. No copyright wording has been presented
as an exact upstream notice. Keep the attribution, permission text, and this
provenance with redistributed copies of the shader.

The original fragment includes attribution in its code comments to the Dave
Hoskins `hash12` technique. Those original comments are preserved verbatim.
