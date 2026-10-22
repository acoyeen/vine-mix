import { mountForest } from './forest-shader.js';

const field = document.querySelector('.atmosphere');
const canvas = document.getElementById('forest-canvas');
const arrival = document.querySelector('.arrival');
const product = document.querySelector('.product-story');
const reduce = matchMedia('(prefers-reduced-motion: reduce)');
let introComplete = false;
let heroVisible = true;
let frame = 0;

const forest = mountForest(canvas, { onError() {
  field.classList.add('shader-failed');
} });

function updateMotion() {
  const paused = reduce.matches || introComplete || document.hidden || !heroVisible;
  forest.setPaused(paused);
  canvas.dataset.motion = paused ? 'paused' : 'playing';
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
  if (!frame) frame = requestAnimationFrame(paintScroll);
}

document.documentElement.classList.add('scroll-ready');
// A brief introduction settles into a still frame, avoiding continuous GPU
// work and the need for a persistent decorative-motion control.
setTimeout(() => { introComplete = true; updateMotion(); }, 5000);
reduce.addEventListener('change', () => { updateMotion(); scheduleScroll(); });
document.addEventListener('visibilitychange', updateMotion);
new IntersectionObserver(entries => {
  heroVisible = entries[0].isIntersecting && entries[0].intersectionRatio > 0;
  updateMotion();
}, { threshold: 0 }).observe(arrival);
window.addEventListener('scroll', scheduleScroll, { passive: true });
window.addEventListener('resize', scheduleScroll, { passive: true });
window.addEventListener('pagehide', () => forest.setPaused(true));
window.addEventListener('pageshow', () => { updateMotion(); scheduleScroll(); });
updateMotion();
scheduleScroll();
