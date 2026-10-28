import assert from 'node:assert/strict';
import { mountPShader } from '../src/p-shader.js';

// Check the first-frame handoff, including the missing/lost WebGL fallback.
function fixture(missing = false) {
  const operations = [], attributes = new Set();
  const canvas = new EventTarget();
  Object.assign(canvas, { width: 300, height: 150,
    getBoundingClientRect: () => ({ width: 1280, height: 720 }),
    setAttribute(name) { operations.push(['ready']); attributes.add(name); },
    removeAttribute(name) { attributes.delete(name); },
  });
  const gl = new Proxy({
    isContextLost: () => false,
    createProgram: () => ({}), createBuffer: () => ({}), createShader: () => ({}),
    getShaderParameter: () => true, getProgramParameter: () => true,
    getAttribLocation: () => 0, getUniformLocation: () => ({}),
    viewport: (...size) => operations.push(['viewport', ...size]),
    uniform3f: (_, ...scene) => operations.push(['scene', ...scene]),
    drawArrays: () => operations.push(['draw']),
  }, { get(target, key) { return target[key] ?? (() => {}); } });
  canvas.getContext = () => missing ? null : gl;
  globalThis.window = Object.assign(new EventTarget(), { devicePixelRatio: 2 });
  globalThis.ResizeObserver = class { observe() {} disconnect() {} };
  globalThis.requestAnimationFrame = () => 1;
  globalThis.cancelAnimationFrame = () => {};
  let failures = 0;
  const controller = mountPShader(canvas, { onError: () => failures++ });
  return { canvas, controller, operations, attributes, failures: () => failures };
}
{
  const f = fixture();
  assert.deepEqual(f.operations.slice(-3).map(op => op[0]), ['scene', 'draw', 'ready']);
  const scene = f.operations.find(op => op[0] === 'scene');
  assert.deepEqual(scene.slice(1), [f.canvas.width, f.canvas.height, 0]);
  assert.ok(f.canvas.width * f.canvas.height <= 900000);
  assert.ok(Math.abs(f.canvas.width / f.canvas.height - 1280 / 720) < .002);
  assert.ok(f.attributes.has('data-shader-ready'));
  f.canvas.dispatchEvent(new Event('webglcontextlost'));
  assert.equal(f.failures(), 1);
  assert.equal(f.attributes.has('data-shader-ready'), false);
  f.controller.destroy();
}
{
  const f = fixture(true);
  assert.equal(f.failures(), 1);
  assert.equal(f.attributes.has('data-shader-ready'), false);
  assert.equal(f.operations.length, 0);
}
{
  const f = fixture();
  f.controller.destroy();
  assert.equal(f.attributes.has('data-shader-ready'), false);
}
console.log('P Shader checks passed: measured first frame before reveal, matching viewport/uniforms, pixel cap, unavailable/lost context and cleanup.');
