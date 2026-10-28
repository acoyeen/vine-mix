import assert from 'node:assert/strict';
import { mountDownloadAtmosphere } from '../src/download-atmosphere.js';

// Real controller, explicit browser lifecycle/clock. Pixel appearance is checked
// in the browser; this fixture checks bounded work and fallback behavior.
function events() {
  const listeners = new Map();
  return {
    addEventListener(type, fn) {
      if (!listeners.has(type)) listeners.set(type, new Set());
      listeners.get(type).add(fn);
    },
    removeEventListener(type, fn) { listeners.get(type)?.delete(fn); },
    emit(type, detail = {}) { for (const fn of [...(listeners.get(type) || [])]) fn({ type, ...detail }); },
    listenerCount: () => [...listeners.values()].reduce((sum, set) => sum + set.size, 0),
  };
}

function fixture({ reduced = false, contrast = false, hidden = false, missing = false, throwPaint = false } = {}) {
  const frames = new Map(), intersections = [], sizes = [];
  const stats = { mainContexts: 0, tileContexts: 0, tilePixels: 0, patterns: 0, paints: 0, measurements: 0, crests: [] };
  let serial = 0;
  let layout = { width: 1440, height: 840, top: 0, left: 0, bottom: 840, right: 1440 };
  const preferences = [Object.assign(events(), { matches: reduced }), Object.assign(events(), { matches: contrast })];
  const mainContext = {
    globalAlpha: 1,
    fillRect() { if (throwPaint) throw new Error('Lost drawing surface'); if (this.fillStyle === '#030405') stats.paints++; },
    beginPath() {}, closePath() {}, fill() {}, save() {}, restore() {}, translate() {},
    moveTo(x, y) {
      assert.ok(Number.isFinite(x) && Number.isFinite(y));
      if (stats.crests[stats.paints] === undefined) stats.crests[stats.paints] = y;
    },
    lineTo(x, y) { assert.ok(Number.isFinite(x) && Number.isFinite(y)); },
    createLinearGradient() { return { addColorStop() {} }; },
    createPattern() { stats.patterns++; return {}; },
  };
  const canvas = Object.assign(events(), {
    width: 300, height: 150,
    getContext(kind) { assert.equal(kind, '2d'); stats.mainContexts++; return missing ? null : mainContext; },
  });
  const host = {
    dataset: {},
    querySelector: selector => selector === 'canvas' ? canvas : null,
    getBoundingClientRect() { stats.measurements++; return { ...layout }; },
  };
  const doc = Object.assign(events(), {
    hidden,
    createElement(tag) {
      assert.equal(tag, 'canvas');
      return { width: 0, height: 0, getContext(kind) {
        assert.equal(kind, '2d'); stats.tileContexts++;
        return {
          createImageData(width, height) { stats.tilePixels += width * height; return { data: new Uint8ClampedArray(width * height * 4) }; },
          putImageData(data) {
            for (let i = 0; i < data.data.length; i += 4) {
              assert.equal(data.data[i], data.data[i + 1], 'Multiplicative grain stays neutral');
              assert.equal(data.data[i], data.data[i + 2]);
              assert.equal(data.data[i + 3], 255);
            }
          },
        };
      } };
    },
  });
  const win = Object.assign(events(), { devicePixelRatio: 3, innerWidth: 1440, innerHeight: 900 });
  globalThis.window = win;
  globalThis.document = doc;
  globalThis.matchMedia = query => preferences[query.includes('reduced-motion') ? 0 : 1];
  globalThis.requestAnimationFrame = callback => { frames.set(++serial, callback); return serial; };
  globalThis.cancelAnimationFrame = id => frames.delete(id);
  class Observer {
    constructor(callback) { this.callback = callback; this.disconnected = false; }
    observe(target) { this.target = target; }
    disconnect() { this.disconnected = true; }
  }
  globalThis.IntersectionObserver = class extends Observer { constructor(fn) { super(fn); intersections.push(this); } };
  globalThis.ResizeObserver = class extends Observer { constructor(fn) { super(fn); sizes.push(this); } };
  const controller = mountDownloadAtmosphere(host);
  return {
    controller, host, canvas, stats, frames, preferences, doc, win, intersections, sizes,
    visible(value = true) { intersections[0].callback([{ target: host, isIntersecting: value }]); },
    resize(width, height) {
      layout = { ...layout, width, height, right: layout.left + width, bottom: layout.top + height };
      sizes[0].callback([{ target: host, contentRect: { width, height } }]);
    },
    setTop(top) { layout = { ...layout, top, bottom: top + layout.height }; },
    tick(time) { const pending = [...frames.values()]; frames.clear(); pending.forEach(fn => fn(time)); },
    setPreference(index, value) { preferences[index].matches = value; preferences[index].emit('change'); },
    setHidden(value) { doc.hidden = value; doc.emit('visibilitychange'); },
  };
}

for (const option of [{}, { reduced: true }, { contrast: true }, { hidden: true }]) {
  const f = fixture(option);
  assert.equal(f.stats.mainContexts, 0, 'Mounting an offscreen module must not allocate a drawing context');
  assert.equal(f.frames.size, 0);
  if (Object.keys(option).length) {
    f.visible();
    assert.equal(f.stats.mainContexts, 0, 'A hidden/static-preference module must remain uninitialised');
    assert.equal(f.frames.size, 0);
    assert.equal(f.host.dataset.ready, 'false');
  }
  f.controller.destroy();
}

{
  const f = fixture();
  f.visible();
  assert.equal(f.stats.mainContexts, 1);
  assert.equal(f.stats.tileContexts, 4);
  assert.ok(f.stats.tilePixels <= 40_000);
  assert.equal(f.frames.size, 1, 'First visible delivery queues one size/draw update');
  assert.equal(f.host.dataset.ready, 'false', 'Canvas stays hidden before the first successful paint');
  f.tick(0);
  assert.equal(f.host.dataset.ready, 'true');
  assert.equal(f.host.dataset.state, 'playing');
  assert.ok(f.canvas.width * f.canvas.height <= 650_000);
  assert.ok(f.canvas.width / 1440 <= 1.25 && f.canvas.height / 840 <= 1.25);
  const initialMeasurements = f.stats.measurements;
  const initialPaints = f.stats.paints;
  for (let time = 1000 / 60; time <= 1000; time += 1000 / 60) f.tick(time);
  const paints = f.stats.paints - initialPaints;
  assert.ok(paints >= 28 && paints <= 32, `Expected a bounded 30fps paint cadence, observed ${paints}`);
  const firstSecond = f.stats.crests.filter(Number.isFinite);
  assert.ok(Math.max(...firstSecond) - Math.min(...firstSecond) > f.canvas.height * .05,
    'The wave must visibly travel in one second, not only update its grain');
  assert.equal(f.stats.measurements, initialMeasurements, 'Painting must not read layout');
  assert.equal(f.stats.patterns, 4, 'Animation must reuse the four grain patterns');
  f.visible();
  f.visible();
  assert.equal(f.frames.size, 1, 'Duplicate intersections must not duplicate the animation clock');
  assert.equal(f.stats.mainContexts, 1);

  f.resize(5000, 3000);
  f.resize(4000, 2000);
  assert.equal(f.frames.size, 1, 'Resize bursts must coalesce and suspend the old-size frame');
  f.tick(1100);
  assert.ok(f.canvas.width * f.canvas.height <= 650_000);
  assert.ok(Math.abs(f.canvas.width / f.canvas.height - 2) < .01);
  assert.equal(f.stats.measurements, initialMeasurements, 'ResizeObserver supplies cached dimensions');
  f.resize(300, 400);
  f.tick(1200);
  assert.equal(f.canvas.width, 375);
  assert.equal(f.canvas.height, 500);

  f.visible(false);
  assert.equal(f.frames.size, 0);
  assert.equal(f.host.dataset.state, 'paused');
  const paintsAtPause = f.stats.paints;
  f.resize(0, 0);
  f.tick(1300);
  assert.equal(f.stats.paints, paintsAtPause);
  f.visible();
  f.tick(1400);
  assert.equal(f.frames.size, 0, 'Zero-area layout must not retain an animation loop');
  f.resize(390, 900);
  f.tick(1500);
  assert.equal(f.frames.size, 1);
  f.setHidden(true);
  assert.equal(f.frames.size, 0);
  f.setHidden(false);
  f.tick(1600);
  assert.equal(f.frames.size, 1);

  for (const preference of [0, 1]) {
    f.setPreference(preference, true);
    assert.equal(f.frames.size, 0);
    assert.equal(f.host.dataset.ready, 'false');
    assert.equal(f.host.dataset.state, 'fallback');
    f.setPreference(preference, false);
    f.tick(1700 + preference * 100);
    assert.equal(f.frames.size, 1);
    assert.equal(f.host.dataset.ready, 'true');
  }
  assert.equal(f.stats.mainContexts, 1, 'Pauses and preference changes retain the existing context');

  f.win.emit('pagehide', { persisted: true });
  assert.equal(f.frames.size, 0);
  f.setTop(2000);
  f.win.emit('pageshow', { persisted: true });
  assert.equal(f.frames.size, 0, 'Restored offscreen content must stay paused');
  f.setTop(0);
  f.win.emit('pageshow', { persisted: true });
  f.tick(2000);
  assert.equal(f.frames.size, 1);
  assert.equal(f.stats.mainContexts, 1);

  f.controller.destroy();
  f.controller.destroy();
  assert.equal(f.frames.size, 0);
  assert.equal(f.doc.listenerCount() + f.win.listenerCount() + f.canvas.listenerCount(), 0);
  assert.equal(f.preferences.reduce((count, pref) => count + pref.listenerCount(), 0), 0);
  assert.ok(f.intersections[0].disconnected && f.sizes[0].disconnected);
  f.visible(); f.resize(1440, 840);
  assert.equal(f.frames.size, 0, 'Late observer delivery must be harmless after teardown');
  assert.equal(f.canvas.width * f.canvas.height, 1, 'Teardown releases the drawing buffer');
}

for (const option of [{ missing: true }, { throwPaint: true }]) {
  const f = fixture(option);
  f.visible(); f.tick(0);
  assert.equal(f.host.dataset.state, 'fallback');
  assert.equal(f.host.dataset.ready, 'false');
  assert.equal(f.frames.size, 0);
  f.setHidden(true); f.setHidden(false); f.visible();
  assert.equal(f.frames.size, 0, 'A drawing failure must remain on the static fallback');
  f.controller.destroy();
}

{
  const f = fixture();
  f.visible(); f.tick(0);
  f.win.emit('pagehide', { persisted: false });
  assert.equal(f.frames.size, 0);
  assert.equal(f.win.listenerCount(), 0);
  assert.equal(f.host.dataset.ready, 'false');
}

assert.doesNotThrow(() => mountDownloadAtmosphere(null).destroy());
console.log('Download atmosphere checks passed: lazy allocation, 30fps/DPR/pixel caps, cached resize, reusable grain, one clock, visibility/preferences, zero-area layout, BFCache, teardown and drawing-failure fallback.');
