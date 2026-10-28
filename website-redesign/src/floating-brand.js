import { navigateToSection } from './page-navigation.js';

const clamp = value => Math.min(1, Math.max(0, value));
const smoothstep = value => value * value * (3 - 2 * value);

// Match the hero's original wordmark retraction, without its playback clock.
function collapseEase(progress) {
  const t = clamp(progress) ** 2;
  return t ** 3 * (t * (6 * t - 15) + 10);
}

export function mountFloatingBrand(header, { onActivate, onHeaderExit } = {}) {
  const brand = header?.querySelector('.site-brand');
  const symbol = brand?.querySelector('.brand-symbol');
  const lettering = brand?.querySelector('.brand-lettering');
  const clipWindow = brand?.querySelector('.brand-window');
  const main = document.querySelector('main');
  if (!brand || !symbol || !lettering || !clipWindow || !main) return null;

  const reduced = matchMedia('(prefers-reduced-motion: reduce)');
  const originalStyle = brand.getAttribute('style');
  const originalSymbol = symbol.getAttribute('transform');
  const originalLettering = lettering.getAttribute('transform');
  const originalClipX = clipWindow.getAttribute('x');
  const slot = document.createElement('span');
  slot.className = 'site-brand-slot';
  slot.setAttribute('aria-hidden', 'true');
  brand.before(slot);
  // Escape the hero's clipping/stacking context while preserving tab order:
  // skip link, home link, then the header's navigation inside main.
  main.before(brand);
  brand.classList.add('is-floating');

  let metrics;
  let collapsed = false;
  let progress = 0;
  let y = 12;
  let frame = 0;
  let lastTime = 0;
  let inactive = false;
  let destroyed = false;
  let pastHeader = false;
  let lastPaint = '';

  function targetProgress() {
    if (reduced.matches) return window.scrollY >= metrics.headerBottom ? 1 : 0;
    // Scrolling starts a complete transition, rather than scrubbing its pose.
    // Start the return in the same header zone, with a small gap to prevent
    // threshold chatter. Do not wait for the last pixel of scrolling.
    if (window.scrollY <= Math.max(0, metrics.start - 2)) collapsed = false;
    else if (window.scrollY > metrics.start) collapsed = true;
    return collapsed ? 1 : 0;
  }

  function targetY(target) {
    // Keep the compact mark docked until the return trigger is crossed.
    return target === 1 ? 12 : Math.max(12, metrics.top - window.scrollY);
  }

  function paint() {
    const collapse = collapseEase(progress);
    const movement = smoothstep(progress);
    const width = metrics.width + (44 - metrics.width) * collapse;
    const height = metrics.height + (44 - metrics.height) * collapse;
    const centre = (metrics.viewportWidth - width) / 2;
    const x = metrics.left + (centre - metrics.left) * movement;
    const signature = [x, y, width, height, collapse].map(value => value.toFixed(4)).join(',');
    if (signature !== lastPaint) {
      lastPaint = signature;
      brand.style.transform = `translate3d(${x}px, ${y}px, 0)`;
      brand.style.width = `${width}px`;
      brand.style.height = `${height}px`;
      symbol.setAttribute('transform', `translate(${437 * collapse} ${2 * collapse})`);
      lettering.setAttribute('transform', `translate(${-440 * collapse} ${2 * collapse})`);
      clipWindow.setAttribute('x', String(778 + 437 * collapse));
    }
    const exited = window.scrollY >= metrics.headerBottom;
    if (exited && !pastHeader) onHeaderExit?.();
    pastHeader = exited;
  }

  function animate(now) {
    frame = 0;
    if (destroyed || inactive || document.hidden) return;
    const target = targetProgress();
    const expanding = target === 0 && progress > 0;
    const elapsed = lastTime ? Math.min(64, Math.max(0, now - lastTime)) : 16;
    lastTime = now;
    const step = 1 - Math.exp(-elapsed / 65);
    progress = reduced.matches ? target : progress + (target - progress) * step;
    if (Math.abs(target - progress) < .0001) progress = target;
    // Restore from the last painted height on the existing animation clock.
    // Raw scroll would otherwise jump the mark down before it expands.
    const destinationY = targetY(target);
    y = expanding && progress !== target ? y + (destinationY - y) * step : destinationY;
    paint();
    if (progress !== target) frame = requestAnimationFrame(animate);
    else lastTime = 0;
  }

  function schedule() {
    if (!destroyed && !inactive && !document.hidden && !frame) frame = requestAnimationFrame(animate);
  }

  function cancel() {
    if (frame) cancelAnimationFrame(frame);
    frame = 0;
    lastTime = 0;
  }

  function measure(snap = false) {
    if (destroyed || inactive || document.hidden) return;
    const rect = slot.getBoundingClientRect();
    const headerRect = header.getBoundingClientRect();
    const top = rect.top + window.scrollY;
    const headerBottom = headerRect.bottom + window.scrollY;
    metrics = {
      left: rect.left, top, width: rect.width, height: rect.height,
      viewportWidth: document.documentElement.clientWidth,
      headerBottom,
      start: Math.max(0, top - 12),
    };
    const target = targetProgress();
    if (snap || reduced.matches) progress = target;
    // A resize can move the destination while an expansion is still underway.
    if (snap || reduced.matches || target === 1 || progress === 0) y = targetY(target);
    paint();
    schedule();
  }

  function onResize() { measure(); }
  function onPreference() { cancel(); measure(true); }
  function onPageHide() { inactive = true; cancel(); }
  function onPageShow() { inactive = false; measure(true); }
  function onVisibility() {
    if (document.hidden) cancel();
    else measure(true);
  }

  function onClick(event) {
    if (event.defaultPrevented || event.button > 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;
    if (!navigateToSection('#top')) return;
    event.preventDefault();
    onActivate?.();
  }

  window.addEventListener('scroll', schedule, { passive: true });
  window.addEventListener('resize', onResize);
  window.addEventListener('pagehide', onPageHide);
  window.addEventListener('pageshow', onPageShow);
  document.addEventListener('visibilitychange', onVisibility);
  reduced.addEventListener('change', onPreference);
  brand.addEventListener('click', onClick);
  const observer = typeof ResizeObserver === 'undefined' ? null : new ResizeObserver(onResize);
  observer?.observe(slot);
  observer?.observe(header);
  // Loading an existing anchor or restoring scroll must start in the correct pose.
  measure(true);

  return {
    destroy() {
      destroyed = true;
      cancel();
      observer?.disconnect();
      window.removeEventListener('scroll', schedule);
      window.removeEventListener('resize', onResize);
      window.removeEventListener('pagehide', onPageHide);
      window.removeEventListener('pageshow', onPageShow);
      document.removeEventListener('visibilitychange', onVisibility);
      reduced.removeEventListener('change', onPreference);
      brand.removeEventListener('click', onClick);
      brand.classList.remove('is-floating');
      slot.replaceWith(brand);
      for (const [element, name, value] of [
        [brand, 'style', originalStyle], [symbol, 'transform', originalSymbol],
        [lettering, 'transform', originalLettering], [clipWindow, 'x', originalClipX],
      ]) {
        if (value === null) element.removeAttribute(name);
        else element.setAttribute(name, value);
      }
    },
  };
}
