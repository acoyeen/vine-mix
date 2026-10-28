import assert from 'node:assert/strict';
import { mountFloatingBrand } from '../src/floating-brand.js';

// Exercise the actual controller with explicit layout measurements. This checks
// state, geometry and lifecycle; it does not pretend to be a browser renderer.
function eventTarget() {
  const listeners = new Map();
  return {
    listeners,
    addEventListener(type, callback) {
      if (!listeners.has(type)) listeners.set(type, new Set());
      listeners.get(type).add(callback);
    },
    removeEventListener(type, callback) { listeners.get(type)?.delete(callback); },
    emit(type, values = {}) {
      const event = { type, button: 0, defaultPrevented: false, ...values,
        preventDefault() { this.defaultPrevented = true; } };
      for (const callback of [...(listeners.get(type) || [])]) callback(event);
      return event;
    },
    listenerCount() { return [...listeners.values()].reduce((sum, set) => sum + set.size, 0); },
  };
}

function styleDeclaration() {
  const values = new Map();
  return new Proxy({
    setProperty(name, value) { values.set(name, String(value)); },
    getPropertyValue(name) { return values.get(name) || ''; },
    removeProperty(name) { const old = values.get(name); values.delete(name); return old || ''; },
  }, {
    get(target, key) {
      if (key === 'cssText') return [...values].map(([name, value]) => `${name}: ${value};`).join(' ');
      return key in target ? target[key] : values.get(key) || '';
    },
    set(target, key, value) {
      if (key === 'cssText') {
        values.clear();
        for (const entry of String(value).split(';')) {
          const colon = entry.indexOf(':');
          if (colon > -1) values.set(entry.slice(0, colon).trim(), entry.slice(colon + 1).trim());
        }
      } else values.set(key, String(value));
      return true;
    },
  });
}

function fixture({ reduced = false, scroll = 0 } = {}) {
  const win = Object.assign(eventTarget(), { scrollY: scroll, scrollX: 0, innerWidth: 1200, innerHeight: 800 });
  const doc = Object.assign(eventTarget(), { hidden: false, visibilityState: 'visible' });
  const media = Object.assign(eventTarget(), { matches: reduced });
  const frames = new Map(), observers = [], scrollCalls = [], historyCalls = [];
  let frameId = 0, time = 0;
  const layout = { left: 60, top: 30, width: 184, height: 48, headerTop: 20, headerBottom: 90 };

  function element(tag = 'div', className = '') {
    const attrs = new Map(), classes = new Set(className.split(/\s+/).filter(Boolean));
    const node = Object.assign(eventTarget(), {
      tagName: tag.toUpperCase(), style: styleDeclaration(), children: [], parentNode: null,
      classList: {
        add(...names) { names.forEach(name => classes.add(name)); },
        remove(...names) { names.forEach(name => classes.delete(name)); },
        contains(name) { return classes.has(name); },
        toggle(name, force) {
          const enabled = force === undefined ? !classes.has(name) : force;
          if (enabled) classes.add(name); else classes.delete(name);
          return enabled;
        },
      },
      setAttribute(name, value) {
        if (name === 'class') this.className = value;
        else if (name === 'style') this.style.cssText = value;
        else attrs.set(name, String(value));
      },
      getAttribute(name) {
        if (name === 'class') return this.className || null;
        if (name === 'style') return this.style.cssText || null;
        return attrs.get(name) ?? null;
      },
      hasAttribute(name) { return this.getAttribute(name) !== null; },
      removeAttribute(name) {
        if (name === 'style') this.style.cssText = '';
        else if (name === 'class') this.className = '';
        else attrs.delete(name);
      },
      append(...nodes) { nodes.forEach(child => this.insertBefore(child, null)); },
      appendChild(child) { return this.insertBefore(child, null); },
      insertBefore(child, reference) {
        child.remove();
        const index = reference ? this.children.indexOf(reference) : this.children.length;
        assert.ok(index >= 0, 'insertBefore reference must belong to the parent');
        this.children.splice(index, 0, child); child.parentNode = this;
        return child;
      },
      before(child) { this.parentNode.insertBefore(child, this); },
      replaceWith(child) { if (this.parentNode) { this.before(child); this.remove(); } },
      replaceChild(child, old) { old.replaceWith(child); return old; },
      remove() {
        if (this.parentNode) {
          this.parentNode.children.splice(this.parentNode.children.indexOf(this), 1);
          this.parentNode = null;
        }
      },
      contains(other) { return this === other || this.children.some(child => child.contains(other)); },
      querySelector(selector) {
        const matches = child => selector.startsWith('.') ? child.classList.contains(selector.slice(1))
          : selector.startsWith('#') ? child.getAttribute('id') === selector.slice(1)
          : child.tagName.toLowerCase() === selector;
        for (const child of this.children) {
          if (matches(child)) return child;
          const found = child.querySelector(selector);
          if (found) return found;
        }
        return null;
      },
      focus(options) { doc.activeElement = this; this.focusOptions = options; },
      getBoundingClientRect() {
        if (this === header) return { left: layout.left, right: win.innerWidth - layout.left,
          top: layout.headerTop - win.scrollY, bottom: layout.headerBottom - win.scrollY,
          width: win.innerWidth - 2 * layout.left, height: layout.headerBottom - layout.headerTop };
        return { left: layout.left, top: layout.top - win.scrollY, width: layout.width, height: layout.height,
          right: layout.left + layout.width, bottom: layout.top + layout.height - win.scrollY };
      },
    });
    Object.defineProperties(node, {
      className: { get: () => [...classes].join(' '), set: value => { classes.clear(); String(value).split(/\s+/).filter(Boolean).forEach(name => classes.add(name)); } },
      parentElement: { get: () => node.parentNode },
      nextSibling: { get: () => node.parentNode?.children[node.parentNode.children.indexOf(node) + 1] || null },
      isConnected: { get: () => doc.body?.contains(node) || false },
    });
    return node;
  }

  const body = element('body'), main = element('main'), header = element('header');
  main.setAttribute('id', 'top');
  const brand = element('a', 'site-brand'), symbol = element('g', 'brand-symbol');
  const lettering = element('g', 'brand-lettering'), window = element('rect', 'brand-window');
  const svg = element('svg', 'site-brand-art'), menu = element('button');
  brand.setAttribute('href', '#top');
  window.setAttribute('x', '778');
  brand.append(svg); svg.append(symbol, lettering, window);
  header.append(brand, menu); main.append(header); body.append(main);
  doc.body = body; doc.documentElement = { clientWidth: win.innerWidth };
  doc.activeElement = body;
  doc.createElement = tag => element(tag);
  doc.querySelector = selector => body.querySelector(selector);
  doc.getElementById = id => body.querySelector(`#${id}`);
  win.document = doc;
  win.location = { hash: '', href: 'https://example.test/' };
  win.history = { pushState(...args) { historyCalls.push(args); win.location.hash = args[2]; } };
  win.scrollTo = options => scrollCalls.push(options);
  win.matchMedia = () => media;
  win.requestAnimationFrame = callback => { frames.set(++frameId, callback); return frameId; };
  win.cancelAnimationFrame = id => frames.delete(id);
  const globals = {
    window: win, document: doc, history: win.history, location: win.location,
    requestAnimationFrame: win.requestAnimationFrame, cancelAnimationFrame: win.cancelAnimationFrame,
    matchMedia: win.matchMedia,
    getComputedStyle: () => ({ scrollMarginTop: '0px' }),
    ResizeObserver: class {
      constructor(callback) { this.callback = callback; this.targets = new Set(); observers.push(this); }
      observe(node) { this.targets.add(node); }
      disconnect() { this.targets.clear(); this.disconnected = true; }
    },
  };
  const originals = new Map(Object.keys(globals).map(key => [key, Object.getOwnPropertyDescriptor(globalThis, key)]));
  Object.entries(globals).forEach(([key, value]) => Object.defineProperty(globalThis, key, { value, writable: true, configurable: true }));
  return {
    win, doc, body, main, header, brand, symbol, lettering, window, menu, media, layout,
    frames, observers, scrollCalls, historyCalls,
    tick() { time += 1000 / 60; const callbacks = [...frames.values()]; frames.clear(); callbacks.forEach(callback => callback(time)); },
    settle() {
      for (let count = 0; frames.size && count < 600; count++) this.tick();
      assert.equal(frames.size, 0, 'The floating logo must stop scheduling frames when settled');
    },
    scrollTo(y) { win.scrollY = y; win.emit('scroll'); },
    restore() {
      for (const [key, descriptor] of originals) {
        if (descriptor) Object.defineProperty(globalThis, key, descriptor); else delete globalThis[key];
      }
    },
  };
}

function near(actual, expected, message, tolerance = .05) {
  assert.ok(Number.isFinite(actual) && Math.abs(actual - expected) <= tolerance, `${message}: ${actual} vs ${expected}`);
}

function pose(view) {
  const match = /^translate3d\(([-\d.e]+)px, ([-\d.e]+)px, 0\)$/.exec(view.brand.style.transform);
  assert.ok(match, 'The fixed anchor must have a finite, readable translation');
  const result = { x: Number(match[1]), y: Number(match[2]), width: Number.parseFloat(view.brand.style.width),
    height: Number.parseFloat(view.brand.style.height),
    clip: Number(view.window.getAttribute('x')) };
  assert.ok(Object.values(result).every(Number.isFinite));
  assert.ok(result.width >= 44, 'The collapsed home link must retain a 44 px hit area');
  assert.ok(result.height >= 44, 'The home link must retain a 44 px minimum hit height');
  return result;
}

function assertTranslation(element, x, y) {
  const match = /^translate\(([-\d.e]+) ([-\d.e]+)\)$/.exec(element.getAttribute('transform'));
  assert.ok(match, 'SVG artwork must retain a finite translation');
  near(Number(match[1]), x, 'SVG horizontal endpoint', .0001);
  near(Number(match[2]), y, 'SVG vertical endpoint', .0001);
}

function assertExpanded(view) {
  const p = pose(view);
  near(p.x, view.layout.left, 'Expanded anchor returns to its measured header position');
  near(p.y, view.layout.top - view.win.scrollY, 'Expanded anchor returns to its header height');
  near(p.width, view.layout.width, 'Expanded anchor keeps the original wordmark width');
  near(p.height, view.layout.height, 'Expanded anchor keeps the original header height');
  near(p.clip, 778, 'Expanded lettering retains the original clipping edge');
  assertTranslation(view.symbol, 0, 0);
  assertTranslation(view.lettering, 0, 0);
}

function assertDocked(view) {
  const p = pose(view);
  near(p.x + p.width / 2, view.doc.documentElement.clientWidth / 2, 'Collapsed anchor is centered in the viewport');
  near(p.y, 12, 'Collapsed anchor stays clear of the top edge');
  near(p.width, 44, 'The final anchor uses the intended compact hit area');
  near(p.height, 44, 'The final anchor has a square hit area for its round background');
  near(p.clip, 1215, 'Collapsed lettering is fully behind the approved clipping edge');
  assertTranslation(view.symbol, 437, 2);
  assertTranslation(view.lettering, -440, 2);
}

// One anchor survives reparenting, full collapse, reverse scrolling and disposal.
{
  const view = fixture();
  let activated = 0, exited = 0;
  view.brand.setAttribute('style', 'outline-offset: 7px;');
  const initialStyle = view.brand.getAttribute('style');
  const controller = mountFloatingBrand(view.header, {
    onActivate: () => activated++, onHeaderExit: () => exited++,
  });
  assert.ok(controller);
  const slot = view.header.querySelector('.site-brand-slot');
  assert.ok(slot && slot.getAttribute('aria-hidden') === 'true');
  assert.deepEqual(view.header.children, [slot, view.menu], 'The slot must preserve header layout order');
  assert.deepEqual(view.body.children, [view.brand, view.main], 'The original link must precede main in keyboard order');
  assert.equal(view.brand.getAttribute('href'), '#top');
  view.settle(); assertExpanded(view);

  view.scrollTo(18); view.settle(); assertExpanded(view);
  view.scrollTo(0); view.settle();

  const initial = pose(view);
  view.scrollTo(250);
  for (let index = 0; index < 12; index++) view.win.emit('scroll');
  assert.equal(view.frames.size, 1, 'Repeated scroll events must share a single pending frame');
  view.tick();
  const moving = pose(view);
  assert.ok(moving.x > initial.x && moving.x < 578, 'Normal motion must travel gradually toward the center');
  assert.ok(moving.width < initial.width && moving.width > 44, 'Retraction must begin gradually');
  assert.equal(exited, 1, 'Leaving the header must close its mobile menu once');
  let last = moving;
  for (let count = 0; view.frames.size && count < 600; count++) {
    view.tick(); const current = pose(view);
    assert.ok(current.x >= last.x && current.width <= last.width && current.clip >= last.clip,
      'Forward movement and retraction must not overshoot or reverse');
    last = current;
  }
  assert.equal(view.frames.size, 0, 'Forward motion must settle without an endless animation loop');
  assertDocked(view);
  view.scrollTo(2000); view.settle(); assertDocked(view);
  assert.equal(exited, 1, 'Remaining below the header must not repeatedly close its menu');

  for (const modifiers of [{ ctrlKey: true }, { metaKey: true }, { shiftKey: true }, { altKey: true }, { button: 1 }, { defaultPrevented: true }]) {
    const event = view.brand.emit('click', modifiers);
    assert.equal(event.defaultPrevented, Boolean(modifiers.defaultPrevented), 'Modified/native clicks must remain untouched');
  }
  assert.equal(activated, 0); assert.equal(view.scrollCalls.length, 0); assert.equal(view.historyCalls.length, 0);
  const click = view.brand.emit('click');
  assert.equal(click.defaultPrevented, true);
  assert.equal(activated, 1);
  assert.deepEqual(view.scrollCalls.at(-1), { top: 0, behavior: 'smooth' });
  assert.equal(view.historyCalls.length, 0, 'Returning home must leave the URL and browser history unchanged');
  view.brand.emit('click');
  assert.equal(view.historyCalls.length, 0, 'Repeated home clicks must not add history entries');

  view.scrollTo(0); view.tick();
  const reversing = pose(view);
  assert.ok(reversing.x > view.layout.left && reversing.x < 578, 'Returning upward must reverse the movement smoothly');
  view.settle(); assertExpanded(view);
  view.scrollTo(1000); view.settle();
  assert.equal(exited, 2, 'Leaving the header again should close a newly opened menu');

  // Resize recomputes viewport and slot geometry, including mobile dimensions.
  view.win.innerWidth = view.doc.documentElement.clientWidth = 600;
  Object.assign(view.layout, { left: 24, top: 28, width: 160, height: 44, headerTop: 12, headerBottom: 68 });
  view.win.emit('resize'); view.settle(); assertDocked(view);
  view.scrollTo(0); view.settle(); assertExpanded(view);
  view.layout.left = 30;
  assert.ok(view.observers.length && view.observers[0].targets.has(slot));
  view.observers[0].callback(); view.settle(); assertExpanded(view);

  // Background tabs and cached page exits must not leave animation clocks alive.
  view.scrollTo(1000); assert.equal(view.frames.size, 1);
  view.doc.hidden = true; view.doc.emit('visibilitychange');
  assert.equal(view.frames.size, 0);
  view.win.emit('scroll'); assert.equal(view.frames.size, 0);
  view.doc.hidden = false; view.doc.emit('visibilitychange'); view.settle(); assertDocked(view);
  view.scrollTo(0); view.win.emit('pagehide'); assert.equal(view.frames.size, 0);
  view.win.emit('resize'); view.win.emit('scroll'); assert.equal(view.frames.size, 0);
  view.win.emit('pageshow'); view.settle(); assertExpanded(view);

  view.scrollTo(1000);
  controller.destroy(); controller.destroy();
  assert.equal(view.frames.size, 0);
  assert.deepEqual(view.header.children, [view.brand, view.menu], 'Destroy must restore the same original link in place');
  assert.deepEqual(view.body.children, [view.main]);
  assert.equal(view.brand.getAttribute('style'), initialStyle, 'Existing inline style must be restored exactly');
  assert.equal(view.brand.classList.contains('is-floating'), false);
  assert.equal(view.symbol.getAttribute('transform'), null);
  assert.equal(view.lettering.getAttribute('transform'), null);
  assert.equal(view.window.getAttribute('x'), '778');
  assert.ok(view.observers.every(observer => observer.disconnected));
  assert.equal(view.win.listenerCount() + view.doc.listenerCount() + view.media.listenerCount() + view.brand.listenerCount(), 0);
  view.win.emit('scroll'); view.win.emit('pageshow'); view.media.emit('change');
  assert.equal(view.frames.size, 0, 'Disposed controllers must not restart');
  view.restore();
}

// A single partial scroll completes the same animation as a full scroll step.
function collapseTrace(scroll) {
  const view = fixture(), controller = mountFloatingBrand(view.header);
  view.settle();
  view.scrollTo(scroll);
  const trace = [];
  // No further scroll events: the controller must finish under its own clock.
  for (let count = 0; view.frames.size && count < 600; count++) {
    view.tick(); trace.push(pose(view));
  }
  assert.equal(view.frames.size, 0, 'A triggered collapse must finish without more scrolling');
  assertDocked(view);
  controller.destroy(); view.restore();
  return trace;
}
{
  const full = collapseTrace(250);
  for (const scroll of [19, 150, 2000]) {
    assert.deepEqual(collapseTrace(scroll), full, 'Crossing the trigger by any distance must preserve the full animation trajectory and pace');
  }
  // Captured from the previous full-scroll animation before changing its trigger.
  // These values protect the existing 65 ms smoothing and both easing curves.
  assert.equal(full.length, 36, 'The existing endpoint-settling frame count must remain unchanged');
  for (const [frame, x, width, clip] of [
    [1, 114.68885206361284, 183.85949545438206, 778.4385749031075],
    [6, 497.30061579272046, 85.28313906676583, 1086.1376301987382],
    [12, 574.2785410822454, 44.9157443120464, 1212.1415695402552],
    [18, 577.8402709488487, 44.010707457963434, 1214.9665774347857],
  ]) {
    near(full[frame - 1].x, x, `Frame ${frame} movement retains its original pace`, 1e-8);
    near(full[frame - 1].width, width, `Frame ${frame} retraction retains its original pace`, 1e-8);
    near(full[frame - 1].clip, clip, `Frame ${frame} lettering retains its original pace`, 1e-8);
  }
}

// Returning home carries the visible vertical position into the expansion.
// These checks include SVG centering, since a smooth hit box alone is insufficient.
function symbolScreenY(view) {
  const p = pose(view), artworkHeight = view.layout.width * 320 / 1230;
  const collapse = (p.clip - 778) / 437;
  return p.y + (p.height - artworkHeight) / 2 + 2 * collapse * view.layout.width / 1230;
}
for (const pausedReturn of [false, true]) {
  const view = fixture(), controller = mountFloatingBrand(view.header);
  view.settle(); view.scrollTo(400); view.settle();
  if (pausedReturn) {
    view.scrollTo(17); view.settle(); assertDocked(view);
    near(pose(view).y, 12, 'A compact logo must not lift before its expansion starts');
  }
  const origin = pose(view), visibleOrigin = symbolScreenY(view), trace = [];
  view.scrollTo(0); view.tick(); trace.push(pose(view));
  assert.ok(trace[0].y > origin.y && trace[0].y < origin.y + (view.layout.top - origin.y) / 2,
    'The first expansion frame must begin at the painted height instead of jumping to the header');
  assert.ok(symbolScreenY(view) - visibleOrigin < 6,
    'The first visible SVG movement must not include the old 18 px anchor jump');
  for (let count = 0; view.frames.size && count < 600; count++) {
    const previous = pose(view), previousSymbol = symbolScreenY(view);
    view.tick(); const current = pose(view); trace.push(current);
    assert.ok(current.y >= previous.y && current.y <= view.layout.top,
      'The returning anchor must approach its header height without reversing or overshooting');
    assert.ok(symbolScreenY(view) >= previousSymbol,
      'The calculated artwork position must remain monotonic during expansion');
  }
  assert.equal(trace.length, 36, 'Vertical restoration shares the existing expansion clock');
  for (const [frame, x, width, clip] of [
    [1, 496.446677553422, 85.7548587147488, 1084.6651910118198],
    [6, 114.10580464127386, 183.8642274403859, 778.4238043467955],
    [12, 62.82505870313052, 183.99998575392266, 778.0000444681128],
    [18, 60.13351517207744, 183.99999999859986, 778.0000000043705],
  ]) {
    near(trace[frame - 1].x, x, `Expansion frame ${frame} keeps its horizontal path`, 1e-8);
    near(trace[frame - 1].width, width, `Expansion frame ${frame} keeps its retraction timing`, 1e-8);
    near(trace[frame - 1].clip, clip, `Expansion frame ${frame} keeps its lettering timing`, 1e-8);
  }
  assertExpanded(view); controller.destroy(); view.restore();
}

// Interruptions continue from the last paint, including an early collapse reversal.
{
  const view = fixture(), controller = mountFloatingBrand(view.header);
  view.settle(); view.scrollTo(400); view.tick();
  const partial = pose(view);
  view.scrollTo(0); view.tick();
  assert.ok(pose(view).y > partial.y && pose(view).y < 21,
    'Reversing a partial collapse must also avoid a vertical jump');
  const beforeResize = pose(view).y;
  Object.assign(view.layout, { left: 24, top: 28, width: 160, height: 44, headerTop: 12, headerBottom: 68 });
  view.win.innerWidth = view.doc.documentElement.clientWidth = 600;
  view.win.emit('resize');
  near(pose(view).y, beforeResize, 'Resize must retain the painted height during expansion', 1e-8);
  view.tick();
  assert.ok(pose(view).y > beforeResize && pose(view).y < 28,
    'The next frame must ease toward the resized header height');
  view.scrollTo(400); view.settle(); assertDocked(view);
  view.scrollTo(0); view.tick();
  assert.ok(pose(view).y > 12 && pose(view).y < 20,
    'A later return must start from the latest compact pose, not a stale expansion origin');
  view.media.matches = true; view.media.emit('change');
  assertExpanded(view); view.settle();
  view.media.matches = false; view.media.emit('change');
  view.scrollTo(400); view.settle(); view.scrollTo(0); view.tick();
  view.win.emit('pagehide'); view.win.emit('pageshow');
  assertExpanded(view); view.settle();
  controller.destroy(); view.restore();
}

// Returning into the header starts expansion before top, without more scroll events.
for (const [top, returnScroll] of [[30, 16], [30, 12], [28, 14]]) {
  const view = fixture(), controller = mountFloatingBrand(view.header);
  if (top !== view.layout.top) {
    Object.assign(view.layout, { left: 24, top, width: 160, height: 44, headerTop: 12, headerBottom: 68 });
    view.win.innerWidth = view.doc.documentElement.clientWidth = 600;
    view.win.emit('resize');
  }
  view.settle(); view.scrollTo(400); view.settle(); assertDocked(view);
  view.scrollTo(returnScroll); view.tick();
  const first = pose(view);
  assert.ok(first.width > 44 && first.width < view.layout.width,
    'The first frame inside the return threshold must expand the logo before scroll reaches zero');
  assert.ok(first.x < (view.doc.documentElement.clientWidth - 44) / 2,
    'Horizontal restoration must start in the same first frame');
  assert.ok(first.y > 12 && first.y < top - returnScroll,
    'Early expansion must retain the continuous vertical return');
  // No more scrolling: returning into the header is sufficient to finish.
  view.settle(); assertExpanded(view);
  assert.equal(view.win.scrollY, returnScroll, 'Expansion must finish at the partial return position');
  controller.destroy(); view.restore();
}

// Jitter inside the 2 px gap retains whichever target is already active.
{
  const view = fixture(), controller = mountFloatingBrand(view.header);
  view.settle();
  view.scrollTo(18); view.settle(); assertExpanded(view);
  view.scrollTo(18.25); view.tick();
  const first = pose(view);
  view.scrollTo(17.75); view.tick();
  assert.ok(pose(view).width < first.width, 'Crossing just below the trigger must not reverse a collapse');
  view.scrollTo(18.1); view.tick();
  view.scrollTo(18.25); view.settle(); assertDocked(view);
  view.scrollTo(16.25); view.settle();
  near(pose(view).width, 44, 'The compact target stays latched inside the return hysteresis gap');
  near(pose(view).clip, 1215, 'Small reverse scrolling must not reveal partial lettering');
  view.scrollTo(16); view.tick();
  const expanding = pose(view);
  assert.ok(expanding.width > 44 && expanding.width < 184, 'Crossing the return threshold begins expansion immediately');
  view.scrollTo(16.25); view.tick();
  assert.ok(pose(view).width > expanding.width, 'Small downward jitter must not interrupt a triggered expansion');
  view.settle(); assertExpanded(view);
  for (const scroll of [17.75, 16.1, 18, 0, .5, 1.25, 18]) {
    view.scrollTo(scroll); view.settle(); assertExpanded(view);
  }
  controller.destroy(); view.restore();
}

// Restored and resized intermediate scroll positions always resolve to an endpoint.
for (const scroll of [19, 150]) {
  const view = fixture({ scroll }), controller = mountFloatingBrand(view.header);
  assertDocked(view); view.settle();
  view.win.innerWidth = view.doc.documentElement.clientWidth = 600;
  Object.assign(view.layout, { left: 24, top: 28, width: 160, height: 44, headerTop: 12, headerBottom: 68 });
  view.win.emit('resize'); view.settle(); assertDocked(view);
  controller.destroy(); view.restore();
}
{
  const view = fixture({ scroll: 17 }), controller = mountFloatingBrand(view.header);
  assertExpanded(view); view.settle();
  view.layout.top = 28; // The new measured trigger is 16 px.
  view.win.emit('resize'); view.settle(); assertDocked(view);
  view.scrollTo(0); view.settle();
  view.scrollTo(150); view.tick();
  assert.ok(pose(view).width > 44);
  view.win.emit('pagehide');
  assert.equal(view.frames.size, 0);
  view.win.emit('pageshow'); assertDocked(view); view.settle();
  controller.destroy(); view.restore();
}

// Reduced motion snaps between usable endpoints, including a live preference change.
{
  const view = fixture({ reduced: true });
  const controller = mountFloatingBrand(view.header);
  view.settle(); assertExpanded(view);
  view.scrollTo(60); view.tick();
  near(pose(view).width, 184, 'Reduced motion keeps the complete wordmark while the header remains');
  assert.equal(view.frames.size, 0);
  view.scrollTo(100); view.tick(); assertDocked(view);
  assert.equal(view.frames.size, 0, 'Reduced motion must not interpolate through extra frames');
  view.brand.emit('click');
  assert.deepEqual(view.scrollCalls.at(-1), { top: 0, behavior: 'instant' });
  view.scrollTo(0); view.tick(); assertExpanded(view);
  view.media.matches = false; view.media.emit('change'); view.settle();
  view.scrollTo(1000); view.tick();
  assert.ok(pose(view).width > 44, 'Turning reduced motion off restores smooth retraction');
  view.media.matches = true; view.media.emit('change');
  assertDocked(view); view.settle();
  controller.destroy(); view.restore();
}

// Scroll restoration starts at its final pose instead of flashing the hero logo.
{
  const view = fixture({ scroll: 1500 });
  const controller = mountFloatingBrand(view.header);
  assertDocked(view); view.settle();
  controller.destroy();
  view.symbol.remove();
  assert.equal(mountFloatingBrand(view.header), null, 'Missing inline artwork must preserve the native link');
  assert.equal(mountFloatingBrand(null), null);
  assert.equal(view.brand.parentNode, view.header);
  assert.equal(view.frames.size, 0);
  view.restore();
}

// A refresh can expose a temporary scroll offset before pageshow restores the
// real position. The original header must stay untouched during that interval.
for (const restoredScroll of [0, 1500]) {
  const view = fixture({ scroll: 26 });
  view.doc.readyState = 'interactive';
  const { mountSiteHeader } = await import('../src/header.js');
  const controller = mountSiteHeader(view.header);
  assert.equal(view.brand.parentNode, view.header);
  assert.equal(view.symbol.getAttribute('transform'), null);
  assert.equal(view.frames.size, 0, 'Do not animate transient startup scroll');
  view.win.emit('pageshow');
  assert.equal(view.brand.parentNode, view.header, 'Keep native markup through pageshow');
  view.win.scrollY = restoredScroll;
  view.tick();
  if (restoredScroll === 0) assertExpanded(view); else assertDocked(view);
  view.settle();
  view.scrollTo(restoredScroll ? 0 : 1500); view.settle();
  if (restoredScroll === 0) assertDocked(view); else assertExpanded(view);
  controller.destroy();
  assert.equal(view.frames.size, 0);
  view.restore();
}
{
  const view = fixture();
  view.doc.readyState = 'complete';
  const { mountSiteHeader } = await import('../src/header.js');
  const controller = mountSiteHeader(view.header);
  assert.equal(view.frames.size, 1, 'A late module still initializes on the next frame');
  controller.destroy();
  view.tick();
  assert.equal(view.brand.parentNode, view.header, 'Disposal cancels pending enhancement');
  assert.equal(view.frames.size, 0);
  view.restore();
}

console.log('Floating brand checks passed: settled refresh initialization, restored-scroll endpoints, startup cleanup, complete collapse after partial scroll, unchanged timing/curves, continuous expansion height, trigger hysteresis, reverse/restore/resize endpoints, return-to-top, reduced motion and lifecycle cleanup.');
