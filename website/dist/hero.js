import { mountForest } from './forest-shader.js?v=20261023b';
import { mountMorphingText } from './morphing-text.js?v=20261023-scaled';

const field = document.querySelector('.atmosphere');
const canvas = document.getElementById('forest-canvas');
const arrival = document.querySelector('.arrival');
const product = document.querySelector('.product-story');
const mark = document.querySelector('.hero-mark');
const hint = document.getElementById('scroll-hint');
const motionButton = document.getElementById('motion-toggle');
const reduce = matchMedia('(prefers-reduced-motion: reduce)');
const finePointer = matchMedia('(hover: hover) and (pointer: fine)');
let userPaused = null;
let heroVisible = true;
let hasScrolled = scrollY > 0;
let hintTimer = 0;
let frame = 0;
let pointerFrame = 0;
let pointerX = 0;
let pointerY = 0;

const headline = mountMorphingText(document.getElementById('morphing-headline'), {
  texts: [
    'Your sound. Your rules.',
    'Choose who hears what.',
    'Bring music to your mic.',
    'Be VRChat’s unofficial DJ.',
    'See where sound goes.',
  ],
  intervalMs: 5000,
  morphMs: 1000,
});
const forest = mountForest(canvas, { onError() {
  field.classList.add('shader-failed');
} });

function updateMotion() {
  const requestedPause = userPaused ?? reduce.matches;
  const paused = requestedPause || document.hidden || !heroVisible;
  forest.setPaused(paused);
  headline.setPaused(paused);
  canvas.dataset.motion = field.classList.contains('shader-failed') ? 'unavailable' : paused ? 'paused' : 'playing';
  motionButton.setAttribute('aria-pressed', String(requestedPause));
  const label = requestedPause ? 'Play animations' : 'Pause animations';
  motionButton.setAttribute('aria-label', label);
  motionButton.title = label;
}

function dismissHint() {
  hasScrolled = true;
  clearTimeout(hintTimer);
  hintTimer = 0;
  hint.hidden = true;
}

function updateHint() {
  if (scrollY > 0) dismissHint();
  if (hasScrolled || document.hidden || !heroVisible) {
    clearTimeout(hintTimer);
    hintTimer = 0;
    hint.hidden = true;
  } else if (!hintTimer && hint.hidden) {
    hintTimer = setTimeout(() => {
      hintTimer = 0;
      if (!hasScrolled && scrollY === 0 && !document.hidden && heroVisible) hint.hidden = false;
    }, 5000);
  }
}

function paintScroll() {
  frame = 0;
  const height = innerHeight;
  const arrivalBounds = arrival.getBoundingClientRect();
  const productBounds = product.getBoundingClientRect();
  const clamp = value => Math.max(0, Math.min(1, value));
  const departure = clamp(-arrivalBounds.top / Math.max(1, arrivalBounds.height * .85));
  const entry = clamp((height - productBounds.top) / (height * .9));
  const progress = clamp(-productBounds.top / Math.max(1, productBounds.height - height));
  document.documentElement.style.setProperty('--field-opacity', String(1 - departure));
  arrival.style.setProperty('--arrival-progress', String(reduce.matches ? 0 : departure));
  product.style.setProperty('--product-reveal', String(entry));
  product.style.setProperty('--detail-reveal', String(clamp((progress - .15) / .4)));
}

function scheduleScroll() {
  if (scrollY > 0) dismissHint();
  if (!frame) frame = requestAnimationFrame(paintScroll);
}

function paintPointer() {
  pointerFrame = 0;
  mark.style.setProperty('--mark-x', `${pointerX * 5}px`);
  mark.style.setProperty('--mark-y', `${pointerY * 4}px`);
  mark.style.setProperty('--mark-tilt-x', `${-pointerY * 3}deg`);
  mark.style.setProperty('--mark-tilt-y', `${pointerX * 3}deg`);
}

function resetPointer() {
  pointerX = pointerY = 0;
  if (pointerFrame) cancelAnimationFrame(pointerFrame);
  paintPointer();
}

arrival.addEventListener('pointermove', event => {
  if (!finePointer.matches || reduce.matches || userPaused || !heroVisible || event.pointerType === 'touch') return;
  pointerX = Math.max(-1, Math.min(1, event.clientX / innerWidth * 2 - 1));
  pointerY = Math.max(-1, Math.min(1, event.clientY / innerHeight * 2 - 1));
  if (!pointerFrame) pointerFrame = requestAnimationFrame(paintPointer);
}, { passive: true });
arrival.addEventListener('pointerleave', resetPointer);
motionButton.addEventListener('click', () => {
  userPaused = !(userPaused ?? reduce.matches);
  resetPointer();
  updateMotion();
});
hint.addEventListener('click', dismissHint);
reduce.addEventListener('change', () => {
  userPaused = null;
  resetPointer();
  updateMotion();
  scheduleScroll();
});
finePointer.addEventListener('change', resetPointer);
document.addEventListener('visibilitychange', () => { resetPointer(); updateMotion(); updateHint(); });
new IntersectionObserver(entries => {
  heroVisible = entries[0].isIntersecting && entries[0].intersectionRatio >= .001;
  if (!heroVisible) resetPointer();
  updateMotion();
  updateHint();
}, { threshold: [0, .001] }).observe(arrival);
window.addEventListener('scroll', scheduleScroll, { passive: true });
window.addEventListener('resize', scheduleScroll, { passive: true });
window.addEventListener('pagehide', () => { forest.setPaused(true); headline.setPaused(true); clearTimeout(hintTimer); hintTimer = 0; });
window.addEventListener('pageshow', () => { updateMotion(); updateHint(); scheduleScroll(); });
document.documentElement.classList.add('scroll-ready');
motionButton.hidden = false;
updateMotion();
updateHint();
scheduleScroll();
