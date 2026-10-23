// Adapted from Magic UI's MIT-licensed Morphing Text. See assets/licenses/MagicUI-MIT.txt.
// The blur/opacity curves and SVG alpha threshold follow the upstream component.
// Blur scales with type size, matching upstream pixels at its 96px display size.
export function mountMorphingText(element, { texts = [], intervalMs = 5000, morphMs = 1000 } = {}) {
  const from = element?.querySelector('[data-morph-from]');
  const to = element?.querySelector('[data-morph-to]');
  if (!from || !to) return { setPaused() {}, destroy() {} };

  const phrases = [...new Set(texts.filter(text => typeof text === 'string').map(text => text.trim()).filter(Boolean))];
  const interval = Number.isFinite(intervalMs) && intervalMs > 0 ? intervalMs : 5000;
  const duration = Number.isFinite(morphMs) ? Math.max(0, Math.min(morphMs, interval)) : 1000;
  let current = 0;
  let paused = true;
  let destroyed = false;
  let timer = 0;
  let frame = 0;
  let nextStart = 0;

  function shuffle(items) {
    for (let i = items.length - 1; i > 0; i--) {
      const j = Math.floor(Math.random() * (i + 1));
      [items[i], items[j]] = [items[j], items[i]];
    }
    return items;
  }

  // The server-rendered first phrase counts as the first item in the first bag.
  let bag = shuffle(phrases.map((_, index) => index).slice(1));

  function nextIndex() {
    if (!bag.length) {
      bag = shuffle(phrases.map((_, index) => index));
      if (bag[0] === current) {
        const other = 1 + Math.floor(Math.random() * (bag.length - 1));
        [bag[0], bag[other]] = [bag[other], bag[0]];
      }
    }
    return bag.shift();
  }

  function settle() {
    from.textContent = phrases[current] ?? '';
    from.style.filter = 'none';
    from.style.opacity = '100%';
    to.textContent = '';
    to.style.filter = 'none';
    to.style.opacity = '0%';
  }

  function cancelPending() {
    clearTimeout(timer);
    cancelAnimationFrame(frame);
    timer = 0;
    frame = 0;
  }

  function hold() {
    if (paused || destroyed || phrases.length < 2) return;
    element.dataset.morphState = 'holding';
    timer = setTimeout(begin, Math.max(0, nextStart - performance.now()));
  }

  function begin() {
    timer = 0;
    if (paused || destroyed) return;
    const startedAt = performance.now();
    nextStart = startedAt + interval;
    from.textContent = phrases[current];
    current = nextIndex();
    to.textContent = phrases[current];
    element.dataset.morphState = 'morphing';

    function draw(now) {
      frame = 0;
      if (paused || destroyed) return;
      const fraction = duration ? Math.min(1, Math.max(0, (now - startedAt) / duration)) : 1;
      const inverse = 1 - fraction;
      to.style.filter = `blur(${Math.min(8 / fraction - 8, 100) / 96}em)`;
      to.style.opacity = `${Math.pow(fraction, 0.4) * 100}%`;
      from.style.filter = `blur(${Math.min(8 / inverse - 8, 100) / 96}em)`;
      from.style.opacity = `${Math.pow(inverse, 0.4) * 100}%`;

      if (fraction < 1) {
        frame = requestAnimationFrame(draw);
      } else {
        settle();
        // A stalled foreground frame must not trigger a burst of missed changes.
        if (nextStart <= now) nextStart = now + interval;
        hold();
      }
    }

    draw(startedAt);
  }

  settle();
  element.dataset.morphState = 'paused';

  return {
    setPaused(value) {
      const nextPaused = Boolean(value);
      if (destroyed || paused === nextPaused) return;
      paused = nextPaused;
      cancelPending();
      settle();
      element.dataset.morphState = paused ? 'paused' : 'static';
      if (!paused) {
        nextStart = performance.now() + interval;
        hold();
      }
    },
    destroy() {
      if (destroyed) return;
      destroyed = true;
      cancelPending();
      settle();
      element.dataset.morphState = 'destroyed';
    }
  };
}
