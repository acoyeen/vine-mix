// Connection geometry inspired by Magic UI Animated Beam (MIT).
// The illustration is local and silent; it does not capture or process audio.
(() => {
  const stage = document.getElementById('hero-stage');
  const core = document.getElementById('mix-core');
  const toggle = document.getElementById('motion-toggle');
  if (!stage || !core || !toggle) return;
  const svg = stage.querySelector('svg');
  const routes = [...stage.querySelectorAll('.cable-route')];
  const small = matchMedia('(max-width: 700px)');
  const reduced = matchMedia('(prefers-reduced-motion: reduce)');
  let frame;

  function draw() {
    const bounds = stage.getBoundingClientRect();
    svg.setAttribute('viewBox', `0 0 ${bounds.width} ${bounds.height}`);
    const geometry = routes.map(route => {
      const from = document.getElementById(route.dataset.from);
      const to = document.getElementById(route.dataset.to);
      const fromBounds = (from === core ? core : from.querySelector('.node-face')).getBoundingClientRect();
      const toBounds = (to === core ? core : to.querySelector('.node-face')).getBoundingClientRect();
      const offset = Number(route.dataset.offset);
      if (small.matches) {
        const sx = (fromBounds.left + fromBounds.right) / 2 - bounds.left + (from === core ? offset : 0);
        const sy = fromBounds.bottom - bounds.top;
        const ex = (toBounds.left + toBounds.right) / 2 - bounds.left + (to === core ? offset : 0);
        const ey = toBounds.top - bounds.top;
        const bend = (ey - sy) * 0.62;
        return [route, `M ${sx},${sy} C ${sx},${sy + bend} ${ex},${ey - bend} ${ex},${ey}`];
      }
      const sx = fromBounds.right - bounds.left;
      const sy = (fromBounds.top + fromBounds.bottom) / 2 - bounds.top + (from === core ? offset : 0);
      const ex = toBounds.left - bounds.left;
      const ey = (toBounds.top + toBounds.bottom) / 2 - bounds.top + (to === core ? offset : 0);
      const bend = (ex - sx) * 0.56;
      return [route, `M ${sx},${sy} C ${sx + bend},${sy} ${ex - bend},${ey} ${ex},${ey}`];
    });
    geometry.forEach(([route, path]) => route.querySelectorAll('path').forEach(p => p.setAttribute('d', path)));
  }
  function scheduleDraw() {
    cancelAnimationFrame(frame);
    frame = requestAnimationFrame(draw);
  }
  function setPaused(paused) {
    document.body.classList.toggle('motion-paused', paused);
    const label = paused ? 'Play motion' : 'Pause motion';
    toggle.querySelector('span').textContent = label;
    toggle.setAttribute('aria-label', label);
    toggle.querySelector('img').src = paused ? 'assets/play.svg' : 'assets/pause.svg';
  }
  toggle.hidden = false;
  document.body.classList.add('motion-ready');
  setPaused(reduced.matches);
  toggle.addEventListener('click', () => setPaused(!document.body.classList.contains('motion-paused')));
  reduced.addEventListener('change', event => setPaused(event.matches));
  small.addEventListener('change', scheduleDraw);
  new ResizeObserver(scheduleDraw).observe(stage);
  document.fonts.ready.then(scheduleDraw);
  scheduleDraw();
})();
