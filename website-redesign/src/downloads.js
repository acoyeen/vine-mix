import { loadRelease } from './releases.js';

const section = document.querySelector('.vm-downloads');
const find = selector => section.querySelector(selector);
const state = find('[data-release-state]');
const status = find('.vm-download-status');
let inFlight = false;
let lastAttempt = 0;
let verified = false;
let reconnectPending = false;

async function refresh(reconnected = false) {
  if (reconnected) reconnectPending = true;
  if (inFlight || document.hidden || (!reconnectPending && Date.now() - lastAttempt < 60000)) return;
  reconnectPending = false;
  lastAttempt = Date.now();
  inFlight = true;
  if (!verified) state.textContent = '(Checking latest…)';
  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), 12000);
  try {
    const release = await loadRelease(fetch, controller.signal);
    // Commit version, destinations and supporting resources together after validation.
    find('[data-release-version]').textContent = release.label;
    find('[data-release-link]').href = release.url;
    find('[data-download="installer"]').href = release.installer;
    find('[data-download="standalone"]').href = release.standalone;
    find('[data-release-notes]').href = release.url;
    find('[data-checksums]').href = release.checksums;
    find('[data-installation]').href = release.docs;
    state.textContent = '(Latest Release)';
    status.hidden = true;
    status.textContent = '';
    verified = true;
    reconnectPending = false;
  } catch {
    state.textContent = verified ? '(Last checked release)' : '(Saved release)';
    status.textContent = 'Couldn’t check for a newer release. You can still use these downloads or browse Older versions.';
    status.hidden = false;
  } finally {
    clearTimeout(timeout);
    inFlight = false;
    // An offline request may reject after connectivity has returned. Consume
    // one queued retry then; a successful pending request already refreshed it.
    if (reconnectPending && !document.hidden) refresh();
  }
}
refresh();
setInterval(refresh, 5 * 60 * 1000);
document.addEventListener('visibilitychange', () => { if (!document.hidden) refresh(); });
// A failed offline attempt must not delay discovery after connectivity returns.
window.addEventListener('online', () => refresh(true));
