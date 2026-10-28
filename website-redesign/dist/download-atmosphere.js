// Original Canvas 2D interpretation of the flowing field and film-grain idea in
// scott clayton's Music Reactive Hero Section. No reference code or audio is used.
// https://21st.dev/@lovesickfromthe6ix/components/music-reactive-hero-section
const FRAME_INTERVAL = 1000 / 30;
const MAX_PIXELS = 650_000;
const MAX_DPR = 1.25;
const TILE_SIZE = 96;

export function mountDownloadAtmosphere(host) {
  const canvas = host?.querySelector('canvas');
  if (!canvas) return { destroy() {} };

  const reduced = matchMedia('(prefers-reduced-motion: reduce)');
  const contrast = matchMedia('(forced-colors: active)');
  let context = null;
  let patterns = [];
  let visible = false;
  let inactive = false;
  let destroyed = false;
  let failed = false;
  let animationFrame = 0;
  let resizeFrame = 0;
  let needsMeasure = true;
  let cssWidth = 0;
  let cssHeight = 0;
  let sceneTime = 0;
  let previousTime = null;
  let previousDraw = -Infinity;
  let grainFrame = 0;
  let resizeObserver = null;
  let intersectionObserver = null;

  host.dataset.state = 'paused';
  host.dataset.ready = 'false';
  const allowed = () => !destroyed && !failed && visible && !inactive &&
    !document.hidden && !reduced.matches && !contrast.matches;

  function stop() {
    if (animationFrame) cancelAnimationFrame(animationFrame);
    if (resizeFrame) cancelAnimationFrame(resizeFrame);
    animationFrame = resizeFrame = 0;
    previousTime = null;
    previousDraw = -Infinity;
  }

  function fail() {
    failed = true;
    stop();
    host.dataset.state = 'fallback';
    host.dataset.ready = 'false';
    patterns = [];
    context = null;
  }

  function initialise() {
    if (context) return true;
    try {
      context = canvas.getContext('2d', { alpha: false });
      if (!context) throw new Error('Canvas 2D unavailable');
      // Small reusable full-range tiles modulate the illuminated field instead
      // of washing the black base with grey. No full-frame random allocation.
      let seed = 0x564d4958;
      patterns = Array.from({ length: 4 }, () => {
        const tile = document.createElement('canvas');
        tile.width = tile.height = TILE_SIZE;
        const tileContext = tile.getContext('2d', { alpha: false });
        if (!tileContext) throw new Error('Grain canvas unavailable');
        const pixels = tileContext.createImageData(TILE_SIZE, TILE_SIZE);
        for (let i = 0; i < pixels.data.length; i += 4) {
          seed ^= seed << 13; seed ^= seed >>> 17; seed ^= seed << 5;
          const tone = (seed >>> 0) & 255;
          pixels.data[i] = pixels.data[i + 1] = pixels.data[i + 2] = tone;
          pixels.data[i + 3] = 255;
        }
        tileContext.putImageData(pixels, 0, 0);
        const pattern = context.createPattern(tile, 'repeat');
        if (!pattern) throw new Error('Grain pattern unavailable');
        return pattern;
      });
      return true;
    } catch {
      fail();
      return false;
    }
  }

  function paint() {
    const width = canvas.width, height = canvas.height;
    context.globalCompositeOperation = 'source-over';
    context.globalAlpha = 1;
    context.fillStyle = '#030405';
    context.fillRect(0, 0, width, height);

    // Moving contours reveal a brighter horizontal field. Four differently
    // paced, breathing crests replace the initial slow, parallel-edge ribbons.
    // Rates are radians/second, independent of display or rendering frame rate.
    // Warm underlayers peek between the two stronger green foreground waves.
    const colors = ['80,68,30', '81,38,32', '48,76,60', '48,72,57'];
    const opacities = [.68, .55, .98, .98];
    const rates = [2.4, 1.8, 3, 1.2];
    const frequencies = [4.4, 5.6, 7.1, 3.2];
    for (let band = 0; band < 4; band++) {
      const centre = height * (.47 + band * .019);
      const breathing = .82 + .22 * Math.sin(sceneTime * .85 + band * 1.3);
      const amplitude = height * (.105 + band * .01) * breathing;
      const phase = sceneTime * rates[band] + band * Math.PI / 2;
      const gradient = context.createLinearGradient(0, centre - amplitude * 1.3, 0, centre + amplitude * 1.7);
      gradient.addColorStop(0, `rgba(${colors[band]},0)`);
      gradient.addColorStop(.44, `rgba(${colors[band]},${opacities[band]})`);
      gradient.addColorStop(1, `rgba(${colors[band]},0)`);
      context.fillStyle = gradient;
      context.beginPath();
      for (let x = 0; x <= width + 6; x += 6) {
        const across = x / width;
        const bend = Math.sin(across * frequencies[band] + phase) * amplitude +
          Math.sin(across * frequencies[band] * 2 + phase * .8) * amplitude * .16 +
          Math.sin(across * frequencies[band] * .5 - phase * .5) * amplitude * .38;
        const y = centre + bend;
        if (!x) context.moveTo(x, y); else context.lineTo(x, y);
      }
      context.lineTo(width + 6, height);
      context.lineTo(0, height);
      context.closePath();
      context.fill();
    }

    context.save();
    const offsetX = (grainFrame * 29) % TILE_SIZE;
    const offsetY = (grainFrame * 43) % TILE_SIZE;
    context.translate(offsetX, offsetY);
    context.globalCompositeOperation = 'multiply';
    context.globalAlpha = .82;
    context.fillStyle = patterns[grainFrame++ % patterns.length];
    context.fillRect(-offsetX, -offsetY, width, height);
    context.restore();

    // Keep the surrounding surface near-black without dimming the wave peaks.
    const edge = context.createLinearGradient(0, 0, 0, height);
    edge.addColorStop(0, '#030405');
    edge.addColorStop(.18, 'rgba(3,4,5,.7)');
    edge.addColorStop(.35, 'rgba(3,4,5,0)');
    edge.addColorStop(.72, 'rgba(3,4,5,0)');
    edge.addColorStop(.9, 'rgba(3,4,5,.7)');
    edge.addColorStop(1, '#030405');
    context.fillStyle = edge;
    context.fillRect(0, 0, width, height);
    host.dataset.ready = 'true';
  }

  function animate(timestamp) {
    animationFrame = 0;
    if (!allowed()) return;
    if (previousTime !== null) sceneTime += Math.min((timestamp - previousTime) / 1000, .1);
    previousTime = timestamp;
    if (timestamp - previousDraw >= FRAME_INTERVAL) {
      try { paint(); } catch { fail(); return; }
      previousDraw = Number.isFinite(previousDraw)
        ? timestamp - ((timestamp - previousDraw) % FRAME_INTERVAL) : timestamp;
    }
    animationFrame = requestAnimationFrame(animate);
  }

  function applySize() {
    resizeFrame = 0;
    if (!allowed()) return;
    if (needsMeasure) {
      const bounds = host.getBoundingClientRect();
      cssWidth = bounds.width;
      cssHeight = bounds.height;
      needsMeasure = false;
    }
    if (!(cssWidth > 0 && cssHeight > 0)) {
      host.dataset.state = 'paused';
      return;
    }
    const dpr = Math.min(MAX_DPR, window.devicePixelRatio || 1);
    const scale = Math.min(dpr, Math.sqrt(MAX_PIXELS / (cssWidth * cssHeight)));
    const width = Math.max(1, Math.floor(cssWidth * scale));
    const height = Math.max(1, Math.floor(cssHeight * scale));
    if (canvas.width !== width || canvas.height !== height) {
      canvas.width = width;
      canvas.height = height;
    }
    try { paint(); } catch { fail(); return; }
    host.dataset.state = 'playing';
    if (!animationFrame) animationFrame = requestAnimationFrame(animate);
  }

  function requestSize() {
    if (!allowed() || resizeFrame) return;
    // Measurements and allocation are outside the drawing loop; several
    // observer/window resize notifications share a single update.
    if (animationFrame) cancelAnimationFrame(animationFrame);
    animationFrame = 0;
    previousTime = null;
    resizeFrame = requestAnimationFrame(applySize);
  }

  function update() {
    if (destroyed || failed) return;
    if (!allowed()) {
      stop();
      const fallback = reduced.matches || contrast.matches;
      host.dataset.state = fallback ? 'fallback' : 'paused';
      if (fallback) host.dataset.ready = 'false';
      return;
    }
    if (!initialise()) return;
    if (!animationFrame && !resizeFrame) requestSize();
  }

  function windowResize() { needsMeasure = true; requestSize(); }
  function pageHide(event) {
    inactive = true;
    if (!event.persisted) { controller.destroy(); return; }
    update();
  }
  function pageShow() {
    if (destroyed || failed) return;
    inactive = false;
    const bounds = host.getBoundingClientRect();
    visible = bounds.width > 0 && bounds.height > 0 && bounds.bottom > 0 &&
      bounds.top < window.innerHeight && bounds.right > 0 && bounds.left < window.innerWidth;
    needsMeasure = true;
    update();
  }

  const controller = {
    destroy() {
      if (destroyed) return;
      destroyed = true;
      stop();
      intersectionObserver?.disconnect();
      resizeObserver?.disconnect();
      reduced.removeEventListener('change', update);
      contrast.removeEventListener('change', update);
      document.removeEventListener('visibilitychange', update);
      window.removeEventListener('resize', windowResize);
      window.removeEventListener('pagehide', pageHide);
      window.removeEventListener('pageshow', pageShow);
      canvas.removeEventListener('contextlost', fail);
      patterns = [];
      context = null;
      canvas.width = canvas.height = 1;
      host.dataset.ready = 'false';
      host.dataset.state = 'fallback';
    },
  };

  if (typeof IntersectionObserver !== 'function') {
    fail();
    return controller;
  }
  intersectionObserver = new IntersectionObserver(entries => {
    if (destroyed || failed) return;
    const entry = entries.find(item => item.target === host);
    if (!entry) return;
    visible = entry.isIntersecting;
    update();
  }, { threshold: 0 });
  intersectionObserver.observe(host);
  if (typeof ResizeObserver === 'function') {
    resizeObserver = new ResizeObserver(entries => {
      if (destroyed || failed) return;
      const entry = entries.find(item => item.target === host);
      if (!entry) return;
      cssWidth = entry.contentRect.width;
      cssHeight = entry.contentRect.height;
      needsMeasure = false;
      requestSize();
    });
    resizeObserver.observe(host);
  }
  reduced.addEventListener('change', update);
  contrast.addEventListener('change', update);
  document.addEventListener('visibilitychange', update);
  window.addEventListener('resize', windowResize, { passive: true });
  window.addEventListener('pagehide', pageHide);
  window.addEventListener('pageshow', pageShow);
  canvas.addEventListener('contextlost', fail);
  update();
  return controller;
}

if (typeof document !== 'undefined') {
  const host = document.querySelector('.vm-download-atmosphere');
  if (host) mountDownloadAtmosphere(host);
}
