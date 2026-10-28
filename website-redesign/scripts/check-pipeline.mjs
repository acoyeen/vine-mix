import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { createNodeEffect, paintNodeEffect } from '../src/pipeline-effects.js';

// A small DOM/clock fixture exercises the real custom element's asynchronous
// lifecycle without installing a browser or introducing runtime dependencies.
class Element extends EventTarget {
  constructor() {
    super();
    this.children = [];
    this.attributes = new Map();
    this.style = {};
    this.width = 1200;
  }
  append(...children) { this.children.push(...children); }
  replaceChildren(...children) { this.children = children; }
  setAttribute(key, value) { this.attributes.set(key, String(value)); }
  getAttribute(key) { return this.attributes.get(key) ?? null; }
  getBoundingClientRect() { return { width: this.width }; }
  getTotalLength() { return 700; }
  getPointAtLength(at) { return { x: at, y: 0 }; }
  scrollTo({ left }) { this.scrollLeft = left; }
  set innerHTML(value) {
    this.html = value;
    this.children = [];
    this.selectors = new Map(['.stage', 'svg', '.canvas-scroll'].map(key => [key, new Element()]));
  }
  get innerHTML() { return this.html; }
  querySelector(selector) { return this.selectors?.get(selector) ?? null; }
}
let declarativeRoot;
globalThis.HTMLElement = class extends Element {
  constructor() { super(); if (declarativeRoot) this.shadowRoot = declarativeRoot; }
  isConnected = true;
  attachShadow() { this.shadowRoot = new Element(); }
};
globalThis.Image = Element;
globalThis.FontFace = class {
  constructor(family) { this.family = family; }
  load() { return Promise.resolve(this); }
};
globalThis.document = Object.assign(new EventTarget(), {
  fonts: new Set(), hidden: false,
  createElement: () => new Element(), createElementNS: () => new Element(),
});
globalThis.matchMedia = () => Object.assign(new EventTarget(), { matches: false });
const resizeObservers = [], intersectionObservers = [];
class Observer {
  constructor(callback) { this.callback = callback; this.disconnected = false; }
  observe(target) { this.target = target; }
  disconnect() { this.disconnected = true; }
}
globalThis.ResizeObserver = class extends Observer {
  constructor(callback) { super(callback); resizeObservers.push(this); }
};
globalThis.IntersectionObserver = class extends Observer {
  constructor(callback) { super(callback); intersectionObservers.push(this); }
};
const frames = new Map();
let serial = 0, Pipeline;
globalThis.requestAnimationFrame = callback => { frames.set(++serial, callback); return serial; };
globalThis.cancelAnimationFrame = id => frames.delete(id);
globalThis.customElements = { define(name, implementation) { Pipeline = implementation; } };
await import('../src/pipeline.js');
const manifest = JSON.parse(await readFile(new URL('../dist/assets/pipeline/nodes/nodes.json', import.meta.url), 'utf8'));
const payload = () => structuredClone(manifest);
const deferred = () => {
  let resolve, reject;
  const promise = new Promise((yes, no) => { resolve = yes; reject = no; });
  return { promise, resolve, reject };
};
const disconnect = element => { element.isConnected = false; element.disconnectedCallback(); };

// Hydration must keep the pre-rendered styled shell and its reserved geometry.
{
  const { pipelineShell } = await import('../src/pipeline-shell.js');
  const shell = new Element();
  shell.innerHTML = pipelineShell('<style>/* initial layout */</style>');
  const stage = shell.querySelector('.stage');
  declarativeRoot = shell;
  const pipeline = new Pipeline();
  declarativeRoot = null;
  globalThis.fetch = async () => ({ ok: true, json: async () => payload() });
  await pipeline.connectedCallback();
  assert.equal(pipeline.shadowRoot, shell, 'Keep the declarative shadow root');
  assert.equal(pipeline.stage, stage, 'Hydrate the existing stage without replacing its layout');
  assert.equal(pipeline.stage.children.length, 8, 'Keep one wire canvas and seven nodes');
  assert.equal(pipeline.stage.style.aspectRatio, '3792/909', 'Match the initial CSS aspect ratio');
  disconnect(pipeline);
}

// Aborting a fetch does not retroactively cancel an already-completed response
// or another asynchronous continuation. A detached load must not mount a scene.
{
  const json = deferred(), parsing = deferred();
  globalThis.fetch = async () => ({ ok: true, json() { parsing.resolve(); return json.promise; } });
  const pipeline = new Pipeline();
  let ready = 0;
  pipeline.addEventListener('pipeline-ready', () => ready++);
  const loading = pipeline.connectedCallback();
  await parsing.promise;
  disconnect(pipeline);
  const observerCount = resizeObservers.length;
  json.resolve(payload());
  await loading;
  assert.equal(ready, 0, 'A detached load must not announce a ready pipeline');
  assert.equal(resizeObservers.length, observerCount, 'A detached load must not create observers');
  assert.deepEqual(pipeline.stage.children, [pipeline.svg], 'A detached load must leave only the reserved wire canvas');
}

// Reconnection gets its own load. A stale success or failure must leave the new
// mounted scene, listeners and observers intact.
for (const outcome of ['resolve', 'reject']) {
  const oldJson = deferred(), parsing = deferred();
  globalThis.fetch = async () => ({ ok: true, json() { parsing.resolve(); return oldJson.promise; } });
  const pipeline = new Pipeline();
  let ready = 0;
  pipeline.addEventListener('pipeline-ready', () => ready++);
  const oldLoad = pipeline.connectedCallback();
  await parsing.promise;
  disconnect(pipeline);
  globalThis.fetch = async () => ({ ok: true, json: async () => payload() });
  pipeline.isConnected = true;
  await pipeline.connectedCallback();
  assert.equal(ready, 1);
  assert.equal(pipeline.stage.children.length, 8);
  const scene = pipeline.scene;
  const observerCount = resizeObservers.length;
  if (outcome === 'resolve') oldJson.resolve(payload());
  else oldJson.reject(new Error('Old response failed'));
  await oldLoad;
  assert.equal(ready, 1, 'A stale response must not announce readiness again');
  assert.equal(pipeline.scene, scene, 'A stale response must preserve the current layout');
  assert.equal(pipeline.stage.children.length, 8, 'A stale response must not duplicate or erase current artwork');
  assert.equal(resizeObservers.length, observerCount, 'A stale response must not replace current observers');
  disconnect(pipeline);
}

{
  globalThis.fetch = async () => ({ ok: true, json: async () => payload() });
  const pipeline = new Pipeline();
  await pipeline.connectedCallback();
  const resize = resizeObservers.at(-1), intersection = intersectionObservers.at(-1);
  intersection.callback([{ isIntersecting: true }]);
  assert.equal(frames.size, 1, 'Visible pipeline must run one animation clock');
  pipeline.stage.width = 0;
  pipeline.sizeWires();
  for (const wire of pipeline.wires) {
    assert.ok(Number.isFinite(Number(wire.pulse.style.strokeWidth)), 'Hidden pipeline must retain finite stroke widths');
    assert.ok(Number.isFinite(Number(wire.gradient.getAttribute('r'))), 'Hidden pipeline must retain finite gradient radii');
  }
  pipeline.stage.width = 1200;
  pipeline.sizeWires();
  disconnect(pipeline);
  assert.equal(frames.size, 0, 'Disconnect must cancel animation');
  assert.equal(resize.disconnected, true);
  assert.equal(intersection.disconnected, true);
  assert.doesNotThrow(() => resize.callback(), 'Already queued resize delivery must be harmless after disconnect');
  pipeline.visible = false;
  intersection.callback([{ isIntersecting: true }]);
  assert.equal(pipeline.visible, false, 'Already queued intersection delivery must not change detached state');
  assert.equal(frames.size, 0);
}

{
  const light = createNodeEffect(manifest[1], 'check-sockets');
  let groupWrites = 0;
  for (const group of Object.values(light.groups)) {
    group.style = new Proxy(group.style, {
      set(target, key, value) { if (key === 'display') groupWrites++; target[key] = value; return true; },
    });
  }
  paintNodeEffect(light, 16, 0, 900);
  assert.equal(light.sockets[0].style.opacity, 1, 'Input socket must acknowledge arrival');
  assert.equal(light.sockets[1].style.opacity, 0, 'Output must wait for node activity');
  const firstSelection = groupWrites;
  for (let time = 20; time <= 900; time += 10) {
    paintNodeEffect(light, time, 0, 900);
    assert.equal(light.sockets[1].style.opacity, 0, 'Output must remain unlit for the full 900 ms transit');
  }
  paintNodeEffect(light, 932, 0, 900);
  assert.equal(light.sockets[1].style.opacity, 1, 'Output must complete its rise after node activity');
  paintNodeEffect(light, 1312, 0, 900);
  assert.equal(light.sockets[1].style.opacity, 0, 'Output must finish its original release');
  assert.equal(groupWrites, firstSelection, 'Unchanged treatment must not rewrite hidden-group visibility every frame');
  paintNodeEffect(light, 450, 0, 900, 'sheen');
  assert.equal(light.groups.sockets.style.display, 'none');
  assert.equal(light.groups.sheen.style.display, '');
  paintNodeEffect(light, 932, 0, 900, 'sockets');
  assert.equal(light.groups.sheen.style.display, 'none');
  assert.equal(light.groups.sockets.style.display, '');
  assert.equal(light.sockets[1].style.opacity, 1);
}

console.log('Pipeline checks passed: pending-load cancellation, reconnect isolation, stale success/error delivery, observer cleanup, one animation clock, finite hidden-layout sizing, unchanged socket handoff and treatment switching without redundant group writes.');
