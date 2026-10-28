import assert from 'node:assert/strict';
import { mountPageNavigation, navigateToSection } from '../src/page-navigation.js';

function eventTarget() {
  const listeners = new Map();
  return {
    addEventListener(type, listener) {
      if (!listeners.has(type)) listeners.set(type, new Set());
      listeners.get(type).add(listener);
    },
    removeEventListener(type, listener) { listeners.get(type)?.delete(listener); },
    emit(type, values = {}) {
      const event = { button: 0, defaultPrevented: false, ...values,
        preventDefault() { this.defaultPrevented = true; } };
      for (const listener of listeners.get(type) || []) listener(event);
      return event;
    },
    listenerCount() { return [...listeners.values()].reduce((sum, set) => sum + set.size, 0); },
  };
}

function fixture({ from = 0, enhanced = true, transitions = true, reduced = false, contrast = false, hash = '' } = {}) {
  const win = Object.assign(eventTarget(), { scrollY: from, innerHeight: 900 });
  const doc = eventTarget();
  const scrolls = [], history = [], replacements = [], snapshots = [];
  function section(id, top, height, margin = 72) {
    const attrs = new Map([['id', id]]);
    return {
      isConnected: true, margin,
      classList: { contains: name => id === 'sound' && name === 'is-enhanced' && enhanced },
      getAttribute: name => attrs.get(name) ?? null,
      hasAttribute: name => attrs.has(name),
      setAttribute: (name, value) => attrs.set(name, value),
      getBoundingClientRect: () => ({ top: top - win.scrollY, bottom: top + height - win.scrollY, height }),
      focus(options) { doc.activeElement = this; this.focusOptions = options; },
    };
  }
  const top = section('top', 0, 900, 0);
  const story = section('sound', 900, 4320);
  const pipeline = section('audio-pipeline', 5220, 900);
  const faq = section('faq', 6120, 900);
  const download = section('download', 7020, 900);
  const sections = new Map([top, story, pipeline, faq, download].map(node => [node.getAttribute('id'), node]));
  doc.getElementById = id => sections.get(id) || null;
  win.location = { hash, pathname: '/', search: '?ref=website' };
  win.history = { state: { saved: true }, pushState(state, title, hash) {
    history.push({ state, title, hash }); win.location.hash = hash;
  }, replaceState(state, title, url) {
    replacements.push({ state, title, url }); win.location.hash = '';
  } };
  win.scrollTo = options => { scrolls.push(options); win.scrollY = options.top; };
  if (transitions) doc.startViewTransition = callback => {
    let resolveUpdate, resolveFinished;
    const view = {
      ready: Promise.resolve(),
      updateCallbackDone: new Promise(resolve => { resolveUpdate = resolve; }),
      finished: new Promise(resolve => { resolveFinished = resolve; }),
      skipped: false,
      skipTransition() { this.skipped = true; },
      run() { callback(); resolveUpdate(); resolveFinished(); },
    };
    snapshots.push(view);
    return view;
  };
  const globals = {
    window: win, document: doc,
    getComputedStyle: node => ({ scrollMarginTop: `${node.margin}px` }),
    matchMedia: query => ({ matches: query.includes('reduced-motion') ? reduced : contrast }),
  };
  const originals = new Map(Object.keys(globals).map(key => [key, Object.getOwnPropertyDescriptor(globalThis, key)]));
  Object.entries(globals).forEach(([key, value]) => Object.defineProperty(globalThis, key, { value, configurable: true, writable: true }));
  const controller = mountPageNavigation();
  function link(href, extra = {}) {
    const attrs = new Map([['href', href], ['data-section-jump', extra.fade ? 'fade' : '']]);
    if (extra.download) attrs.set('download', '');
    const node = {
      target: extra.target || '',
      getAttribute: name => attrs.get(name) ?? null,
      hasAttribute: name => attrs.has(name),
      closest: selector => selector === 'a[data-section-jump]' ? node : null,
    };
    return node;
  }
  return {
    win, doc, top, story, pipeline, sections, scrolls, history, replacements, snapshots, controller, link,
    restore() {
      controller.destroy();
      assert.equal(win.listenerCount() + doc.listenerCount(), 0, 'Navigation listeners must be removed');
      for (const [key, descriptor] of originals) {
        if (descriptor) Object.defineProperty(globalThis, key, descriptor); else delete globalThis[key];
      }
    },
  };
}

// The entry link arrives at the pinned interval's start and keeps smooth motion.
{
  const view = fixture();
  const event = view.doc.emit('click', { target: view.link('#sound') });
  assert.equal(event.defaultPrevented, true);
  assert.deepEqual(view.scrolls, [{ top: 900, behavior: 'smooth' }]);
  assert.equal(view.snapshots.length, 0);
  assert.equal(view.history.length, 0, 'Section navigation must not add browser history');
  assert.equal(view.win.location.hash, '');
  assert.deepEqual(view.win.history.state, { saved: true });
  assert.equal(view.doc.activeElement, view.story);
  assert.deepEqual(view.story.focusOptions, { preventScroll: true });
  assert.equal(view.story.getAttribute('tabindex'), '-1');
  navigateToSection('#sound');
  assert.equal(view.history.length + view.replacements.length, 0, 'Repeated navigation must leave a clean URL untouched');
  view.restore();
}

// Every header link opts into the existing fade, including entry to the story
// and static/mobile layouts where there is no pinned interval to cross.
for (const enhanced of [true, false]) {
  for (const [destination, expected] of [['#sound', enhanced ? 900 : 828], ['#faq', 6048], ['#download', 6948]]) {
    const view = fixture({ enhanced });
    const target = view.sections.get(destination.slice(1));
    const event = view.doc.emit('click', { target: view.link(destination, { fade: true }) });
    assert.equal(event.defaultPrevented, true);
    assert.equal(view.scrolls.length + view.history.length, 0, 'Header navigation must wait for the original view snapshot');
    assert.equal(view.snapshots.length, 1, `${destination} must fade with enhanced=${enhanced}`);
    view.snapshots[0].run();
    assert.deepEqual(view.scrolls, [{ top: expected, behavior: 'instant' }], 'A fade visits only its destination');
    assert.equal(view.history.length + view.replacements.length, 0, 'Header navigation must preserve the URL and history');
    assert.equal(view.win.location.hash, '');
    assert.deepEqual(view.win.history.state, { saved: true });
    assert.equal(view.doc.activeElement, target);
    assert.deepEqual(target.focusOptions, { preventScroll: true });
    assert.equal(target.getAttribute('tabindex'), '-1');
    navigateToSection(destination, { fade: true });
    view.snapshots[1].run();
    assert.equal(view.history.length, 0, 'Repeated header activation must not add history');
    view.restore();
  }
}

// Requested header fades jump directly when snapshots or motion are unavailable.
for (const enhanced of [true, false]) {
  for (const options of [{ transitions: false }, { reduced: true }, { contrast: true }]) {
    for (const [destination, expected] of [['#sound', enhanced ? 900 : 828], ['#faq', 6048], ['#download', 6948]]) {
      const view = fixture({ enhanced, ...options });
      const event = view.doc.emit('click', { target: view.link(destination, { fade: true }) });
      assert.equal(event.defaultPrevented, true);
      assert.deepEqual(view.scrolls, [{ top: expected, behavior: 'instant' }]);
      assert.equal(view.snapshots.length, 0);
      assert.equal(view.doc.activeElement, view.sections.get(destination.slice(1)));
      assert.equal(view.history.length + view.replacements.length, 0);
      assert.equal(view.win.location.hash, '');
      view.restore();
    }
  }
}

// Return-to-top never scrolls through the long animation, in either direction.
for (const [from, destination, expected] of [[6000, '#top', 0], [2600, '#audio-pipeline', 5148]]) {
  const view = fixture({ from });
  assert.equal(navigateToSection(destination), true);
  assert.equal(view.scrolls.length, 0, 'The original view is captured before relocation');
  assert.equal(view.snapshots.length, 1);
  view.snapshots[0].run();
  assert.deepEqual(view.scrolls, [{ top: expected, behavior: 'instant' }], 'Only the destination may be visited');
  assert.equal(view.doc.activeElement, destination === '#top' ? view.top : view.pipeline);
  assert.equal(view.story.getBoundingClientRect().height, 4320, 'Skipping must not collapse story geometry');
  assert.equal(view.story.classList.contains('is-enhanced'), true);
  view.restore();
}

// Browser fallbacks and motion preferences must still bypass the pinned route.
for (const options of [{ transitions: false }, { reduced: true }, { contrast: true }]) {
  const view = fixture({ from: 6000, ...options });
  navigateToSection('#top');
  assert.deepEqual(view.scrolls, [{ top: 0, behavior: 'instant' }]);
  assert.equal(view.snapshots.length, 0);
  view.restore();
}
{
  const view = fixture({ from: 3000, enhanced: false });
  navigateToSection('#top');
  assert.deepEqual(view.scrolls, [{ top: 0, behavior: 'smooth' }], 'Flat routes need no special transition');
  navigateToSection('#sound');
  assert.deepEqual(view.scrolls.at(-1), { top: 828, behavior: 'smooth' }, 'Static story headings still need their normal anchor clearance');
  view.restore();
}

// Modified clicks, external/native links and missing targets remain native.
for (const fade of [false, true]) {
  const view = fixture();
  for (const modifiers of [{ ctrlKey: true }, { metaKey: true }, { shiftKey: true }, { altKey: true }, { button: 1 }, { defaultPrevented: true }]) {
    const event = view.doc.emit('click', { target: view.link('#sound', { fade }), ...modifiers });
    assert.equal(event.defaultPrevented, Boolean(modifiers.defaultPrevented));
  }
  for (const target of [view.link('#sound', { fade, target: '_blank' }), view.link('#sound', { fade, download: true }),
    view.link('https://example.test/', { fade }), view.link('#missing', { fade }), view.link('#%broken', { fade })]) {
    assert.equal(view.doc.emit('click', { target }).defaultPrevented, false);
  }
  assert.equal(view.scrolls.length + view.history.length + view.snapshots.length, 0);
  view.restore();
}

// Header transitions use the same latest-request cancellation as story bypasses.
{
  const view = fixture({ enhanced: false });
  view.doc.emit('click', { target: view.link('#sound', { fade: true }) });
  view.doc.emit('click', { target: view.link('#faq', { fade: true }) });
  assert.equal(view.snapshots[0].skipped, true);
  view.snapshots[0].run();
  assert.equal(view.scrolls.length + view.history.length, 0, 'An obsolete header callback cannot relocate the page');
  view.snapshots[1].run();
  assert.deepEqual(view.scrolls, [{ top: 6048, behavior: 'instant' }]);
  assert.equal(view.history.length + view.replacements.length, 0);
  assert.equal(view.doc.activeElement, view.sections.get('faq'));
  view.restore();
}

// A skipped View Transition still runs its callback: only the latest request wins.
{
  const view = fixture({ from: 2600 });
  navigateToSection('#audio-pipeline');
  navigateToSection('#top');
  assert.equal(view.snapshots[0].skipped, true);
  view.snapshots[0].run();
  assert.equal(view.scrolls.length, 0, 'An obsolete request cannot change scroll or focus');
  view.snapshots[1].run();
  assert.deepEqual(view.scrolls, [{ top: 0, behavior: 'instant' }]);
  assert.equal(view.history.length + view.replacements.length, 0);
  view.restore();
}

// Back/forward, native hash navigation and page exits win over pending work.
for (const event of ['popstate', 'hashchange', 'pagehide']) {
  const view = fixture({ from: 2600 });
  navigateToSection('#audio-pipeline');
  view.win.emit(event);
  assert.equal(view.snapshots[0].skipped, true);
  view.snapshots[0].run();
  assert.equal(view.scrolls.length + view.history.length, 0);
  view.restore();
}
{
  const view = fixture({ from: 2600 });
  navigateToSection('#audio-pipeline');
  view.controller.destroy();
  view.snapshots[0].run();
  assert.equal(view.scrolls.length + view.history.length, 0, 'Disposal invalidates delayed callbacks');
  view.restore();
}

// A failing snapshot API cannot strand a valid destination.
{
  const view = fixture({ from: 6000 });
  view.doc.startViewTransition = () => { throw new Error('Snapshot unavailable'); };
  navigateToSection('#top');
  assert.deepEqual(view.scrolls, [{ top: 0, behavior: 'instant' }]);
  view.restore();
}

// Old shared section links are cleaned on navigation without losing query/state
// or adding a Back-button entry. Unknown fragments are left alone.
for (const hash of ['#top', '#faq', '#download', '#unknown']) {
  const view = fixture({ from: 6000, transitions: false, hash });
  navigateToSection('#top');
  assert.equal(view.history.length, 0);
  if (hash === '#unknown') {
    assert.equal(view.replacements.length, 0);
    assert.equal(view.win.location.hash, hash);
  } else {
    assert.deepEqual(view.replacements, [{ state: { saved: true }, title: '', url: '/?ref=website' }]);
    assert.equal(view.win.location.hash, '');
    navigateToSection('#faq');
    assert.equal(view.replacements.length, 1, 'A clean URL must not be rewritten again');
  }
  view.restore();
}

console.log('Page navigation checks passed: unchanged clean URLs/history, legacy fragment cleanup, desktop/mobile fades, pinned-story bypass, focus, preference/API fallbacks, native link guards, repeated requests, history interruption and lifecycle cleanup.');
