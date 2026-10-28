import assert from 'node:assert/strict';
import { selectRelease, loadRelease, REPOSITORY, FEED } from '../src/releases.js';

const release = (number, changes = {}) => ({
  tag_name: `v${number}`, draft: false, published_at: '2026-10-26T00:00:00Z',
  assets: [`VineMix-${number}-Setup-x64.exe`, `VineMix-${number}-Standalone-x64.exe`, 'SHA256SUMS.txt'].map(name => ({
    name, size: 100, state: 'uploaded', browser_download_url: `${REPOSITORY}/releases/download/v${number}/${name}`,
  })), ...changes,
});
const response = (items, headers = {}) => ({ ok: true, headers: new Headers(headers), json: async () => items });

assert.equal(selectRelease([release('0.3.0-beta.9'), release('0.3.0-beta.10')]).label, '0.3.0 · Beta 10');
assert.equal(selectRelease([release('0.3.0-beta.10'), release('0.3.0')]).tag, 'v0.3.0');
assert.equal(selectRelease([release('0.4.0-beta.1'), release('0.3.0')]).tag, 'v0.4.0-beta.1');
assert.equal(selectRelease([release('1.0.0', { draft: true }), release('0.3.0')]).tag, 'v0.3.0');
assert.equal(selectRelease([release('1.0.0', { published_at: null }), release('0.3.0')]).tag, 'v0.3.0');
assert.equal(selectRelease([release('0.3.0'), release('nightly'), release('0.3.0-beta.01')]).tag, 'v0.3.0');
assert.equal(selectRelease([release('0.3.0'), release('1.0.0', { tag_name: ['v1.0.0'] })]).tag, 'v0.3.0',
  'A malformed record must not poison an otherwise valid release list');
assert.throws(() => selectRelease([release('0.4.0', { assets: [] }), release('0.3.0')]), /not ready/);
assert.throws(() => selectRelease([release('0.4.0', { assets: {} }), release('0.3.0')]), /not ready/);
const noisy = release('0.3.0'); noisy.assets.unshift(null, {});
assert.equal(selectRelease([noisy]).tag, 'v0.3.0');
const invalidSize = release('0.3.0'); invalidSize.assets[0].size = '100';
assert.throws(() => selectRelease([invalidSize]), /not ready/);
const unsafe = release('0.3.0');
unsafe.assets[0].browser_download_url = 'https://example.invalid/setup.exe';
assert.throws(() => selectRelease([unsafe]), /not ready/);
const older = release('0.3.0'); older.assets.pop();
assert.equal(selectRelease([older]).checksums, `${REPOSITORY}/releases/tag/v0.3.0`);

let requests = [];
const signal = new AbortController().signal;
const latest = await loadRelease(async (url, options) => {
  requests.push({ url, options });
  return response([release(requests.length === 1 ? '0.3.0' : '0.4.0')],
    requests.length === 1 ? { link: '<next>; rel="next"' } : {});
}, signal);
assert.equal(latest.tag, 'v0.4.0');
assert.deepEqual(requests.map(request => request.url), [`${FEED}?per_page=100&page=1`, `${FEED}?per_page=100&page=2`]);
assert.ok(requests.every(request => request.options.signal === signal && request.options.credentials === 'omit'));
await assert.rejects(loadRelease(async () => ({ ok: false, status: 403 })), /403/);
await assert.rejects(loadRelease(async () => response({})), /Invalid/);
await assert.rejects(loadRelease(async () => { throw Error('offline'); }), /offline/);
requests = [];
await assert.rejects(loadRelease(async url => {
  requests.push(url); return response([release('0.3.0')], { link: '<next>; rel="next"' });
}), /discovery limit/);
assert.equal(requests.length, 10);

// Exercise the actual page entry point, including fallback preservation and
// the online event. No network, browser package, or live release is required.
function eventTarget() {
  const listeners = new Map();
  return {
    addEventListener(type, listener) {
      if (!listeners.has(type)) listeners.set(type, []);
      listeners.get(type).push(listener);
    },
    emit(type) { for (const listener of listeners.get(type) || []) listener({ type }); },
  };
}
const selectors = ['[data-release-state]', '.vm-download-status', '[data-release-version]', '[data-release-link]',
  '[data-download="installer"]', '[data-download="standalone"]', '[data-release-notes]', '[data-checksums]', '[data-installation]'];
const nodes = new Map(selectors.map(selector => [selector, { href: `saved:${selector}`, textContent: 'Saved', hidden: true }]));
const doc = Object.assign(eventTarget(), { hidden: false, querySelector: () => ({ querySelector: selector => nodes.get(selector) }) });
const win = eventTarget();
const timers = new Map(), intervals = [];
let clock = 1000000, fetchCalls = 0, timerId = 0;
let result = async () => { throw Error('offline'); };
const globals = {
  document: doc, window: win,
  fetch: (...args) => { fetchCalls++; return result(...args); },
  setTimeout: callback => { timers.set(++timerId, callback); return timerId; },
  clearTimeout: id => timers.delete(id),
  setInterval: (callback, delay) => { intervals.push({ callback, delay }); return intervals.length; },
};
const originals = new Map(Object.keys(globals).map(key => [key, Object.getOwnPropertyDescriptor(globalThis, key)]));
const originalNow = Date.now;
const settle = () => new Promise(resolve => setImmediate(resolve));
const linkSnapshot = () => selectors.slice(3).map(selector => nodes.get(selector).href);
const state = nodes.get('[data-release-state]'), status = nodes.get('.vm-download-status');
try {
  for (const [key, value] of Object.entries(globals)) Object.defineProperty(globalThis, key, { value, configurable: true, writable: true });
  Date.now = () => clock;
  const saved = linkSnapshot();
  await import('../src/downloads.js');
  await settle();
  assert.equal(fetchCalls, 1);
  assert.deepEqual(linkSnapshot(), saved, 'Offline discovery must preserve every saved link');
  assert.equal(state.textContent, '(Saved release)');
  assert.equal(status.hidden, false);

  result = async () => response([release('0.4.0-beta.1')]);
  clock += 1000;
  win.emit('online');
  await settle();
  assert.equal(fetchCalls, 2, 'Reconnect must retry immediately, even inside the usual 60-second throttle');
  assert.equal(state.textContent, '(Latest Release)');
  assert.equal(nodes.get('[data-release-version]').textContent, '0.4.0 · Beta 1');
  assert.equal(status.hidden, true);
  const verified = linkSnapshot();

  result = async () => response([release('0.5.0', { assets: [] })]);
  clock += 60000;
  doc.emit('visibilitychange');
  await settle();
  assert.deepEqual(linkSnapshot(), verified, 'An incomplete newer release must not partially replace checked links');
  assert.equal(state.textContent, '(Last checked release)');
  assert.equal(status.hidden, false);

  doc.hidden = true;
  win.emit('online');
  await settle();
  assert.equal(fetchCalls, 3, 'Hidden pages must not refresh');
  doc.hidden = false;
  let finish;
  result = () => new Promise(resolve => { finish = resolve; });
  win.emit('online');
  win.emit('online');
  assert.equal(fetchCalls, 4, 'Concurrent online events must share the in-flight refresh');
  finish(response([release('0.4.0-beta.1')]));
  await settle();
  assert.equal(fetchCalls, 4, 'A successful pending request must not cause a redundant reconnect fetch');

  // Connectivity can return before a request started offline actually rejects.
  // Retry that failure once after it settles, without parallel requests.
  clock += 60000;
  let fail;
  result = () => new Promise((resolve, reject) => { fail = reject; });
  doc.emit('visibilitychange');
  assert.equal(fetchCalls, 5);
  win.emit('online');
  win.emit('online');
  assert.equal(fetchCalls, 5, 'Reconnect must not overlap a pending request');
  result = async () => response([release('0.4.0-beta.2')]);
  fail(Error('Request started offline'));
  await settle();
  assert.equal(fetchCalls, 6, 'A failed pending request must consume one queued reconnect retry');
  assert.equal(state.textContent, '(Latest Release)');
  assert.equal(nodes.get('[data-release-version]').textContent, '0.4.0 · Beta 2');

  clock += 60000;
  result = () => new Promise((resolve, reject) => { fail = reject; });
  doc.emit('visibilitychange');
  win.emit('online');
  result = async () => { throw Error('Still offline'); };
  fail(Error('Request started offline'));
  await settle();
  assert.equal(fetchCalls, 8, 'A failed reconnect retry must stop rather than form a retry loop');
  assert.equal(state.textContent, '(Last checked release)');
  assert.equal(nodes.get('[data-release-version]').textContent, '0.4.0 · Beta 2');
  assert.equal(timers.size, 0, 'Completed attempts must clear their timeout');
  assert.equal(intervals.length, 1);
  assert.equal(intervals[0].delay, 5 * 60 * 1000);
} finally {
  Date.now = originalNow;
  for (const [key, descriptor] of originals) {
    if (descriptor) Object.defineProperty(globalThis, key, descriptor); else delete globalThis[key];
  }
}

console.log('Release checks passed: versions, complete matching assets, trusted URLs, pagination, offline fallback, reconnect and in-flight protection.');
