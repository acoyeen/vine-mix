/**
 * P Shader — Diego Biaggioni (@digogois2), MIT License.
 * https://21st.dev/@digogois2/components/p-shader
 * Generated with the 21st.dev Shader Builder's Orb preset.
 * Published Component.tsx inspected as source on 2026-10-25.
 * Original FRAG SHA-256:
 * e89e54031e083c7e61a5c7e8c2855ded6a15d21f4d3942fcd76dc49f8bc14a8e
 *
 * The original Orb/noise/grain equations and active recipe values are retained:
 * scale 1.5, intensity 0.55, contrast 1.005, grain 0.042, seed 1, speed 0.86.
 * Original warm colours are replaced with the selected muted-mint palette.
 * Fixed-recipe specialization removes unused UV/hash22/OKLab/hue helpers and
 * disabled rotation, offset, drift, warp, blur, vignette, saturation, brightness,
 * cursor branches. Four palette entries replace the padded eight-entry array.
 *
 * React integration is replaced with the site's dependency-free lifecycle:
 * paused startup/accumulated time, 30 fps, 1.5 DPR / 900,000 pixels, checked
 * compilation, resize, context-loss fallback, and deterministic cleanup.
 * No code from Backroom's Apache-licensed fragment shader is included.
 */

const vertexSource = `attribute vec2 a_position;
void main() {
  gl_Position = vec4(a_position, 0.0, 1.0);
}`;

const fragmentSource = `#ifdef GL_FRAGMENT_PRECISION_HIGH
precision highp float;
#else
precision mediump float;
#endif

uniform vec3 u_colors[4];
uniform vec3 u_scene; // resolution.xy, animation time
#define u_resolution u_scene.xy
#define u_time u_scene.z

// Active values from the published Orb recipe.
const float u_scale = 1.5;
const float u_intensity = 0.55;
const float u_seed = 1.0;
const float u_contrast = 1.005;
const float u_grain = 0.042;

float hash21(vec2 p) {
#ifndef GL_FRAGMENT_PRECISION_HIGH
  p = mod(p, 31.0);
#endif
  p = fract(p * vec2(234.34, 435.345));
  p += dot(p, p + 34.23);
  return fract(p.x * p.y);
}

// Film grain uses Dave Hoskins' hash12, as credited by the published component.
float grainHash(vec2 p) {
  vec3 p3 = fract(vec3(p.xyx) * 0.1031);
  p3 += dot(p3, p3.yzx + 33.33);
  return fract((p3.x + p3.y) * p3.z);
}

float noise(vec2 p) {
  vec2 i = floor(p);
  vec2 f = fract(p);
  vec2 u = f * f * (3.0 - 2.0 * f);
  return mix(
    mix(hash21(i), hash21(i + vec2(1.0, 0.0)), u.x),
    mix(hash21(i + vec2(0.0, 1.0)), hash21(i + vec2(1.0, 1.0)), u.x),
    u.y);
}

float fbm(vec2 p) {
  float v = 0.0;
  float a = 0.5;
  for (int i = 0; i < 5; i++) {
    v += a * noise(p);
    p = p * 2.03 + vec2(17.0, 9.2);
    a *= 0.5;
  }
  return v;
}

// Fixed four-colour recipe; preserve the original smoothstep interpolation.
vec3 palette(float x) {
  float f = clamp(x, 0.0, 1.0) * 3.0;
  vec3 col = u_colors[0];
  for (int i = 0; i < 3; i++) {
    col = mix(col, u_colors[i + 1],
      smoothstep(0.0, 1.0, clamp(f - float(i), 0.0, 1.0)));
  }
  return col;
}

vec3 shade(vec2 p, float t) {
  float wob = fbm(p * 2.0 + t * 0.25 + u_seed) - 0.5;
  float d = length(p) + wob * u_intensity * 0.5;
  float core = 1.0 - smoothstep(0.0, 0.75, d);
  float halo = exp(-d * 2.5) * 0.5;
  return palette(clamp(core + halo, 0.0, 1.0));
}

void main() {
  vec2 p = (gl_FragCoord.xy - 0.5 * u_resolution.xy)
    / min(u_resolution.x, u_resolution.y);
  p *= u_scale;
  vec3 col = shade(p, u_time);
  col = (col - 0.5) * u_contrast + 0.5;
  col += (grainHash(
    gl_FragCoord.xy + vec2(u_seed * 17.0, u_seed * 31.0)) - 0.5) * u_grain;
  gl_FragColor = vec4(clamp(col, 0.0, 1.0), 1.0);
}
`;

// Muted mint light over deep green-black, behind the monochrome foreground.
const colors = new Float32Array([
  8 / 255, 15 / 255, 13 / 255,    // #080F0D
  24 / 255, 57 / 255, 46 / 255,   // #18392E
  72 / 255, 125 / 255, 102 / 255, // #487D66
  154 / 255, 203 / 255, 180 / 255, // #9ACBB4
]);

/**
 * Draw one initial frame, then animate only after setPaused(false).
 * The page owns visibility, reduced-motion, and high-contrast policy.
 * @param {HTMLCanvasElement} canvas
 * @param {{onError?: (error: Error) => void}} [options]
 * @returns {{setPaused: (paused: boolean) => void, destroy: () => void}}
 */
export function mountPShader(canvas, { onError } = {}) {
  let gl = null;
  let program = null;
  let buffer = null;
  let resizeObserver = null;
  let animationFrame = 0;
  let resizeFrame = 0;
  let paused = true;
  let destroyed = false;
  let failed = false;
  let sceneTime = 0;
  let previousTimestamp = null;
  let previousDraw = -Infinity;
  let sceneUniform = null;
  let cssWidth = 1;
  let cssHeight = 1;
  const frameInterval = 1000 / 30;
  const maxDevicePixelRatio = 1.5;
  const maxRenderPixels = 900_000;
  const timeScale = 0.86;

  function stopFrame() {
    if (animationFrame) cancelAnimationFrame(animationFrame);
    animationFrame = 0;
    previousTimestamp = null;
  }

  function releaseResources() {
    stopFrame();
    if (resizeFrame) cancelAnimationFrame(resizeFrame);
    resizeFrame = 0;
    resizeObserver?.disconnect();
    resizeObserver = null;
    window.removeEventListener('resize', handleResize);
    canvas?.removeEventListener?.('webglcontextlost', handleContextLost);
    if (gl && !gl.isContextLost()) {
      if (buffer) gl.deleteBuffer(buffer);
      if (program) gl.deleteProgram(program);
    }
    buffer = null;
    program = null;
    sceneUniform = null;
  }

  function fail(error) {
    if (failed || destroyed) return;
    failed = true;
    canvas?.removeAttribute?.('data-shader-ready');
    paused = true;
    releaseResources();
    if (typeof onError === 'function') onError(error);
  }

  function handleContextLost() {
    fail(new Error('The P Shader WebGL context was lost.'));
  }

  function resize() {
    const dpr = Math.min(window.devicePixelRatio || 1, maxDevicePixelRatio);
    const width = Math.max(1, Math.round(cssWidth * dpr));
    const height = Math.max(1, Math.round(cssHeight * dpr));
    const ratio = Math.min(1, Math.sqrt(maxRenderPixels / (width * height)));
    const renderWidth = Math.max(1, Math.floor(width * ratio));
    const renderHeight = Math.max(1, Math.floor(height * ratio));
    if (canvas.width !== renderWidth || canvas.height !== renderHeight) {
      canvas.width = renderWidth;
      canvas.height = renderHeight;
      gl.viewport(0, 0, renderWidth, renderHeight);
    }
  }

  function draw() {
    if (destroyed || failed || !gl || gl.isContextLost()) return;
    resize();
    gl.uniform3f(sceneUniform, canvas.width, canvas.height, sceneTime * timeScale);
    gl.drawArrays(gl.TRIANGLES, 0, 3);
  }

  function measureAndDraw() {
    resizeFrame = 0;
    if (destroyed || failed) return;
    // Animation frames use cached dimensions; no scroll/pointer listeners.
    const bounds = canvas.getBoundingClientRect();
    cssWidth = bounds.width;
    cssHeight = bounds.height;
    draw();
  }

  function handleResize() {
    if (destroyed || failed || resizeFrame) return;
    // Window and ResizeObserver notifications can describe the same resize.
    // Coalesce their layout read and redraw into a single frame.
    resizeFrame = requestAnimationFrame(measureAndDraw);
  }

  function animate(timestamp) {
    animationFrame = 0;
    if (paused || destroyed || failed) return;
    if (previousTimestamp !== null) {
      sceneTime += Math.min((timestamp - previousTimestamp) / 1000, 0.1);
    }
    previousTimestamp = timestamp;
    if (timestamp - previousDraw >= frameInterval - 0.25) {
      draw();
      previousDraw = timestamp;
    }
    animationFrame = requestAnimationFrame(animate);
  }

  const controller = {
    setPaused(value) {
      if (destroyed || failed) return;
      const nextPaused = Boolean(value);
      if (paused === nextPaused) return;
      paused = nextPaused;
      stopFrame();
      previousDraw = -Infinity;
      if (!paused) animationFrame = requestAnimationFrame(animate);
    },
    destroy() {
      if (destroyed) return;
      destroyed = true;
      canvas?.removeAttribute?.('data-shader-ready');
      paused = true;
      releaseResources();
      gl = null;
    },
  };

  function compile(type, source) {
    const shader = gl.createShader(type);
    if (!shader) throw new Error('P Shader could not allocate a WebGL shader.');
    gl.shaderSource(shader, source);
    gl.compileShader(shader);
    if (!gl.getShaderParameter(shader, gl.COMPILE_STATUS)) {
      const details = gl.getShaderInfoLog(shader) || 'Unknown compilation error.';
      gl.deleteShader(shader);
      throw new Error(`P Shader compilation failed: ${details}`);
    }
    return shader;
  }

  try {
    if (!canvas || typeof canvas.getContext !== 'function') {
      throw new Error('P Shader requires a canvas element.');
    }
    gl = canvas.getContext('webgl', { antialias: false });
    if (!gl) throw new Error('WebGL is unavailable.');
    program = gl.createProgram();
    if (!program) throw new Error('P Shader could not allocate a WebGL program.');
    let vertex = null;
    let fragment = null;
    try {
      vertex = compile(gl.VERTEX_SHADER, vertexSource);
      fragment = compile(gl.FRAGMENT_SHADER, fragmentSource);
      gl.attachShader(program, vertex);
      gl.attachShader(program, fragment);
      gl.linkProgram(program);
      if (!gl.getProgramParameter(program, gl.LINK_STATUS)) {
        throw new Error(`P Shader linking failed: ${gl.getProgramInfoLog(program) || 'Unknown linking error.'}`);
      }
    } finally {
      if (vertex) gl.deleteShader(vertex);
      if (fragment) gl.deleteShader(fragment);
    }
    gl.useProgram(program);
    buffer = gl.createBuffer();
    if (!buffer) throw new Error('P Shader could not allocate a WebGL buffer.');
    gl.bindBuffer(gl.ARRAY_BUFFER, buffer);
    gl.bufferData(gl.ARRAY_BUFFER, new Float32Array([-1, -1, 3, -1, -1, 3]), gl.STATIC_DRAW);
    const position = gl.getAttribLocation(program, 'a_position');
    if (position < 0) throw new Error('P Shader position attribute is missing.');
    gl.enableVertexAttribArray(position);
    gl.vertexAttribPointer(position, 2, gl.FLOAT, false, 0, 0);
    const colorsUniform = gl.getUniformLocation(program, 'u_colors[0]');
    sceneUniform = gl.getUniformLocation(program, 'u_scene');
    if (colorsUniform === null || sceneUniform === null) {
      throw new Error('P Shader uniforms are missing.');
    }
    gl.uniform3fv(colorsUniform, colors);
    canvas.addEventListener('webglcontextlost', handleContextLost);
    window.addEventListener('resize', handleResize);
    if (typeof ResizeObserver === 'function') {
      resizeObserver = new ResizeObserver(handleResize);
      resizeObserver.observe(canvas);
    }
    // Initialize the viewport even when canvas attributes already match.
    gl.viewport(0, 0, canvas.width, canvas.height);
    measureAndDraw();
    // Reveal only after the first frame has its measured viewport and uniforms.
    canvas.setAttribute('data-shader-ready', '');
  } catch (error) {
    fail(error instanceof Error ? error : new Error(String(error)));
  }
  return controller;
}
