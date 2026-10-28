// Explicit section links can bypass the pinned illustration without collapsing
// its layout or replaying its scroll timeline in either direction.
let requestId = 0;
let activeTransition = null;

function cancelNavigation() {
  requestId++;
  activeTransition?.skipTransition();
  activeTransition = null;
}

function resolveDestination(destination) {
  if (typeof destination !== 'string') return destination;
  if (!destination.startsWith('#')) return null;
  try { return document.getElementById(decodeURIComponent(destination.slice(1))); }
  catch { return null; }
}

function crossesStory(from, to) {
  const story = document.getElementById('sound');
  if (!story?.classList.contains('is-enhanced')) return false;
  const rect = story.getBoundingClientRect();
  const start = rect.top + window.scrollY;
  const end = start + rect.height - window.innerHeight;
  return end > start && Math.min(from, to) < end && Math.max(from, to) > start;
}

export function navigateToSection(destination, { fade = false } = {}) {
  const target = resolveDestination(destination);
  const id = target?.getAttribute('id');
  if (!target || !id || target.isConnected === false) return false;

  cancelNavigation();
  const currentRequest = requestId;
  const from = window.scrollY;
  // The pinned story already reserves the logo clearance inside its full-height
  // panel. An extra anchor margin would push its bottom navigation offscreen.
  const margin = id === 'sound' && target.classList.contains('is-enhanced')
    ? 0 : Number.parseFloat(getComputedStyle(target).scrollMarginTop) || 0;
  const top = id === 'top' ? 0 : Math.max(0, target.getBoundingClientRect().top + from - margin);
  const reduced = matchMedia('(prefers-reduced-motion: reduce)').matches;
  const highContrast = matchMedia('(forced-colors: active)').matches;
  const bypass = crossesStory(from, top);
  const useFade = fade || bypass;
  let committed = false;

  function commit(behavior) {
    if (committed || currentRequest !== requestId || target.isConnected === false) return;
    committed = true;
    // Section shortcuts move the view without adding URL fragments or history.
    // Also retire fragments left by older versions, preserving the path/query.
    if (window.location.hash && resolveDestination(window.location.hash)) {
      window.history.replaceState(window.history.state, '', window.location.pathname + window.location.search);
    }
    window.scrollTo({ top, behavior });
    if (!target.hasAttribute('tabindex')) target.setAttribute('tabindex', '-1');
    target.focus({ preventScroll: true });
  }

  if (!useFade || reduced || highContrast || typeof document.startViewTransition !== 'function') {
    commit(useFade || reduced || highContrast ? 'instant' : 'smooth');
    return true;
  }

  try {
    const transition = document.startViewTransition(() => commit('instant'));
    activeTransition = transition;
    // Skipped or interrupted visual snapshots are normal, not navigation errors.
    transition.ready.catch(() => {});
    transition.updateCallbackDone.catch(() => commit('instant'));
    const settled = () => { if (activeTransition === transition) activeTransition = null; };
    transition.finished.then(settled, settled);
  } catch {
    commit('instant');
  }
  return true;
}

export function mountPageNavigation() {
  function onClick(event) {
    if (event.defaultPrevented || event.button > 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;
    const link = event.target?.closest?.('a[data-section-jump]');
    if (!link || link.hasAttribute('download') || (link.target && link.target !== '_self')) return;
    if (navigateToSection(link.getAttribute('href'), { fade: link.getAttribute('data-section-jump') === 'fade' })) event.preventDefault();
  }

  // Native history restoration must win over any queued transition callback.
  document.addEventListener('click', onClick);
  window.addEventListener('popstate', cancelNavigation);
  window.addEventListener('hashchange', cancelNavigation);
  window.addEventListener('pagehide', cancelNavigation);
  return {
    destroy() {
      cancelNavigation();
      document.removeEventListener('click', onClick);
      window.removeEventListener('popstate', cancelNavigation);
      window.removeEventListener('hashchange', cancelNavigation);
      window.removeEventListener('pagehide', cancelNavigation);
    },
  };
}
