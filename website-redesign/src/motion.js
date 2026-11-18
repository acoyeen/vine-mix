import { mountPShader } from './p-shader.js';
import { mountHeroReader } from './hero-reader.js';
import { mountScrollStory } from './scroll-story.js';

// The document is complete and flat before this module runs. Motion is a
// bounded enhancement; neither it nor WebGL is needed to read an audio route.
const root = document.documentElement;
const arrival = document.querySelector('.arrival');
const identity = document.querySelector('.arrival-identity');
const field = document.querySelector('.atmosphere');
const canvas = document.getElementById('background-canvas');
const hint = document.getElementById('scroll-hint');
const reducedMotion = matchMedia('(prefers-reduced-motion: reduce)');
const forcedColors = matchMedia('(forced-colors: active)');

let pageInactive = false;
let shaderAvailable = true;
let heroVisible = false;
let heroOwnsMotion = false;
let routeMoving = false;
let scrollFrame = 0;
let background = null;
let atmosphereOpacity = '';
const reader = mountHeroReader(document.querySelector('.hero-reader'));
const story = mountScrollStory(document.getElementById('sound'));

const pageVisible = () => !pageInactive && !document.hidden;

background = mountPShader(canvas, {
  onError() {
    shaderAvailable = false;
    field?.classList.add('shader-failed');
    // The node scene can work without WebGL.
    updateMotion();
  },
});

function updateMotion() {
  const paused = reducedMotion.matches;
  const canPlayBackground = pageVisible() && !paused && heroOwnsMotion && shaderAvailable && !forcedColors.matches;
  background?.setPaused(!canPlayBackground);
  reader.setPaused(!pageVisible() || paused || !heroOwnsMotion);
  story.setSuspended(!pageVisible() || paused || forcedColors.matches);
  if (canvas) canvas.dataset.motion = !shaderAvailable ? 'unavailable' : canPlayBackground ? 'playing' : 'paused';
  root.dataset.scene = canPlayBackground ? 'hero' : pageVisible() && !paused && routeMoving ? 'story' : 'static';
}

function updateHint() {
  if (!hint) return;
  // Ease out over the first small scroll, and retrace the fade on the way back.
  // Opacity keeps the link in place while visibility/inert retire it at zero.
  const remaining = !pageVisible() ? 0
    : reducedMotion.matches || forcedColors.matches ? Number(window.scrollY <= 1)
    : Math.max(0, Math.min(1, 1 - window.scrollY / 120));
  hint.style.opacity = String(remaining * remaining * (3 - 2 * remaining));
  hint.style.visibility = remaining > 0 ? '' : 'hidden';
  hint.inert = remaining === 0;
}

function paintScroll() {
  scrollFrame = 0;
  if (!pageVisible()) return;
  const height = window.innerHeight;
  const heroBounds = arrival?.getBoundingClientRect();
  const visibleHeroHeight = heroBounds
    ? Math.max(0, Math.min(height, heroBounds.bottom) - Math.max(0, heroBounds.top))
    : 0;
  heroVisible = visibleHeroHeight > 0;
  const heroRatio = heroBounds ? visibleHeroHeight / Math.max(1, Math.min(height, heroBounds.height)) : 0;
  // Fade only the decorative surface. The CSS edge blend also works without
  // JavaScript; reduced motion keeps that static treatment. Scroll position
  // controls the blend with no extra clock.
  const heroExit = heroBounds && !reducedMotion.matches
    ? Math.max(0, Math.min(1, -heroBounds.top / Math.max(1, heroBounds.height * .8)))
    : 0;
  const nextOpacity = (1 - heroExit * heroExit * (3 - 2 * heroExit)).toFixed(4);
  if (field && nextOpacity !== atmosphereOpacity) {
    atmosphereOpacity = nextOpacity;
    field.style.setProperty('--hero-atmosphere-opacity', nextOpacity);
  }
  // Below this threshold the background freezes before the node reveal moves.
  heroOwnsMotion = heroRatio > .2;
  routeMoving = story.update({ paused: false, reduced: reducedMotion.matches, highContrast: forcedColors.matches });
  updateMotion();
  updateHint();
}

function scheduleScroll() {
  updateHint();
  if (pageVisible() && !scrollFrame) scrollFrame = requestAnimationFrame(paintScroll);
}

function cancelPendingWork() {
  if (scrollFrame) cancelAnimationFrame(scrollFrame);
  scrollFrame = 0;
}

// This wrapper contains only the decorative reader, never links or page copy.
for (const eventName of ['dragstart', 'contextmenu', 'selectstart']) {
  identity?.addEventListener(eventName, event => event.preventDefault());
}

function handlePreferenceChange() {
  updateMotion();
  scheduleScroll();
}
reducedMotion.addEventListener('change', handlePreferenceChange);
forcedColors.addEventListener('change', handlePreferenceChange);
document.addEventListener('visibilitychange', () => {
  if (pageVisible()) {
    // A hidden page can acquire a new scroll position through navigation.
    // Refresh that position before allowing either decorative scene to resume.
    paintScroll();
    return;
  }
  cancelPendingWork();
  updateMotion();
  updateHint();
});
window.addEventListener('scroll', scheduleScroll, { passive: true });
window.addEventListener('resize', scheduleScroll, { passive: true });
window.addEventListener('pagehide', event => {
  pageInactive = true;
  cancelPendingWork();
  updateMotion();
  if (!event.persisted) {
    background?.destroy();
    reader.destroy();
    story.destroy();
  }
});
window.addEventListener('pageshow', () => {
  pageInactive = false;
  // Re-measure first so a restored scroll position never starts the background
  // while the reader is already below it. A BFCache return retains its state.
  paintScroll();
});

paintScroll();
