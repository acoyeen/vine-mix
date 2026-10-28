import { sceneSize, storyFrame, clamp } from './story-layout.js';
import { mountStoryMeters } from './story-meters.js';

const chapterStops = [.16, .48, .97];
const descriptions = [
  'A microphone branches into Listening output for headphones and Virtual output for sharing. Each output has its own level and mute.',
  'The microphone now passes through Low / Mid / High before reaching both outputs. The illustrative tone values change as you scroll.',
  'The microphone still passes through Low / Mid / High to Listening output and Virtual output. Music is added through Ducking to Virtual output. A separate connection from the tone node to the Ducking trigger lowers music when you speak; that trigger does not add audible voice.',
];
const svgNS = 'http://www.w3.org/2000/svg';
let nextStoryId = 0;

export function mountScrollStory(section) {
  const world = section.querySelector('.story-world');
  const stage = section.querySelector('.story-stage');
  const copies = [...section.querySelectorAll('[data-chapter]')];
  const nodes = new Map([...section.querySelectorAll('[data-story-node]')].map(node => [node.dataset.storyNode, node]));
  const wires = new Map();
  const cables = section.querySelector('.story-cables');
  const maskPrefix = `story-cable-${++nextStoryId}`;
  const definitions = document.createElementNS(svgNS, 'defs');
  cables.append(definitions);
  const fits = matchMedia('(min-width: 900px) and (min-height: 680px)');
  const toneImages = [...nodes.get('tone').querySelectorAll('img:not(.story-meter)')];
  const toneFrames = Array.from({ length: 9 }, (_, index) => `assets/nodes/tone-${String(index).padStart(2, '0')}.png`);
  const preloads = [];
  const progressBar = section.querySelector('.story-progress-fill');
  const progressTrack = section.querySelector('.story-progress-track');
  const skipLink = section.querySelector('.story-skip');
  const meters = mountStoryMeters(section);
  let enhanced = false;
  let measuredWidth = 0;
  let measuredHeight = 0;
  let measuredHeading = 0;
  let lastPaint = '';
  let chapter = -1;
  let currentProgress = 0;
  let destroyed = false;
  let suspended = true;
  let metersVisible = false;
  let musicVisible = false;
  let toneVisible = false;

  const updateMeters = () => meters.setActive(metersVisible && !suspended, musicVisible, toneVisible);

  for (const wire of storyFrame(0).wires) {
    // A separate solid reveal mask lets the detector keep its own dashed
    // stroke while every route draws from its source toward its destination.
    const mask = document.createElementNS(svgNS, 'mask');
    const maskId = `${maskPrefix}-${wire.id}`;
    mask.setAttribute('id', maskId);
    mask.setAttribute('maskUnits', 'userSpaceOnUse');
    mask.setAttribute('maskContentUnits', 'userSpaceOnUse');
    mask.setAttribute('x', '0');
    mask.setAttribute('y', '0');
    mask.setAttribute('width', String(sceneSize.width));
    mask.setAttribute('height', String(sceneSize.height));
    const reveal = document.createElementNS(svgNS, 'path');
    reveal.setAttribute('pathLength', '1');
    reveal.style.fill = 'none';
    reveal.style.stroke = 'white';
    reveal.style.strokeWidth = '9';
    reveal.style.strokeLinecap = 'butt';
    reveal.style.strokeDasharray = '1';
    reveal.style.strokeDashoffset = '1';
    mask.append(reveal);
    definitions.append(mask);
    const group = document.createElementNS(svgNS, 'g');
    group.setAttribute('class', `story-wire ${wire.kind}`);
    group.setAttribute('mask', `url(#${maskId})`);
    const halo = document.createElementNS(svgNS, 'path');
    halo.setAttribute('class', 'story-wire-halo');
    const path = document.createElementNS(svgNS, 'path');
    path.setAttribute('class', 'story-wire-line');
    group.append(halo, path);
    cables.append(group);
    wires.set(wire.id, { group, paths: [halo, path], reveal });
  }

  function measure() {
    const width = stage.clientWidth;
    const height = window.innerHeight;
    const headingHeight = section.querySelector('.story-heading').offsetHeight;
    if (width === measuredWidth && height === measuredHeight && headingHeight === measuredHeading) return;
    measuredWidth = width;
    measuredHeight = height;
    measuredHeading = headingHeight;
    // Top logo clearance, bottom shortcut row, heading gap and progression track.
    // Reserve this space so short desktop windows still fit the full diagram.
    const sceneSpacing = 72 + 80 + 32 + 26;
    const availableHeight = Math.max(280, height - headingHeight - sceneSpacing);
    const scale = Math.min(1, width / sceneSize.width, availableHeight / sceneSize.height);
    section.style.setProperty('--story-scale', scale);
    section.style.setProperty('--story-stage-height', `${sceneSize.height * scale}px`);
    world.style.left = `${(width - sceneSize.width * scale) / 2}px`;
  }

  function paintSkip(opacity) {
    if (!skipLink) return;
    skipLink.style.opacity = String(opacity);
    skipLink.style.visibility = opacity > 0 ? '' : 'hidden';
    skipLink.inert = opacity === 0;
  }

  function paint(progress, paused) {
    if (progressBar) progressBar.style.transform = `scaleX(${progress})`;
    // Let the completed story give way to the next canvas without a leftover line.
    // Keep the track in layout so node scale and scroll timing stay unchanged.
    const progressVisibility = clamp((1 - progress) / .04);
    const controlsOpacity = progressVisibility * progressVisibility * (3 - 2 * progressVisibility);
    if (progressTrack) progressTrack.style.opacity = String(controlsOpacity);
    paintSkip(controlsOpacity);
    const effective = paused ? chapterStops[storyFrame(progress).chapter] : progress;
    const key = effective.toFixed(5);
    if (lastPaint === key) return;
    lastPaint = key;
    const frame = storyFrame(effective);
    musicVisible = frame.poses.music[2] > .05;
    toneVisible = frame.poses.tone[2] > .05;
    for (const [id, [x, y, opacity]] of Object.entries(frame.poses)) {
      const node = nodes.get(id);
      node.style.transform = `translate(${x.toFixed(2)}px,${y.toFixed(2)}px)`;
      node.style.opacity = opacity.toFixed(3);
      node.style.visibility = opacity > .001 ? 'visible' : 'hidden';
    }
    for (const wire of frame.wires) {
      const view = wires.get(wire.id);
      view.group.style.opacity = wire.opacity.toFixed(3);
      for (const path of view.paths) path.setAttribute('d', wire.d);
      view.reveal.setAttribute('d', wire.d);
      view.reveal.style.strokeDashoffset = String(1 - wire.draw);
    }
    const step = frame.toneAmount * 8;
    const lower = Math.floor(step), upper = Math.min(8, lower + 1);
    for (const [index, source] of [toneFrames[lower], toneFrames[upper]].entries()) {
      if (toneImages[index].getAttribute('src') !== source) toneImages[index].src = source;
    }
    toneImages[1].style.opacity = String(step - lower);
    if (chapter !== frame.chapter) {
      chapter = frame.chapter;
      section.dataset.chapter = String(chapter);
      section.setAttribute('aria-labelledby', ['sound-title', 'shape-title', 'duck-title'][chapter]);
      copies.forEach((copy, index) => {
        copy.classList.toggle('is-current', index === chapter);
        copy.setAttribute('aria-hidden', String(index !== chapter));
        copy.inert = index !== chapter;
      });
      world.setAttribute('aria-label', descriptions[chapter]);
    }
  }

  function update({ paused, reduced, highContrast }) {
    if (destroyed) return false;
    const shouldEnhance = fits.matches && !reduced && !highContrast;
    if (enhanced !== shouldEnhance) {
      enhanced = shouldEnhance;
      section.classList.toggle('is-enhanced', enhanced);
      lastPaint = '';
      chapter = -1;
      measuredWidth = 0;
      if (!enhanced) {
        copies.forEach(copy => { copy.removeAttribute('aria-hidden'); copy.inert = false; });
        section.removeAttribute('data-chapter');
        section.setAttribute('aria-labelledby', 'sound-title');
      }
    }
    if (!enhanced) {
      // Static/mobile chapters retire the shortcut as their bottom reaches
      // the viewport edge, before the following pipeline enters the canvas.
      const remaining = clamp((section.getBoundingClientRect().bottom - window.innerHeight) / 120);
      paintSkip(reduced || highContrast ? Number(remaining > 0) : remaining * remaining * (3 - 2 * remaining));
      metersVisible = false;
      updateMeters();
      return false;
    }
    measure();
    const bounds = section.getBoundingClientRect();
    if (!preloads.length && bounds.top < innerHeight * 2 && bounds.bottom > 0) {
      for (const source of toneFrames) {
        const img = new Image();
        img.src = source;
        preloads.push(img);
      }
    }
    currentProgress = clamp(-bounds.top / Math.max(1, bounds.height - window.innerHeight));
    // The key avoids style writes when fully above/below the story.
    paint(currentProgress, paused);
    const stageBounds = stage.getBoundingClientRect();
    metersVisible = !paused && stageBounds.top < innerHeight && stageBounds.bottom > 0;
    updateMeters();
    return !paused && bounds.top < window.innerHeight && bounds.bottom > 0;
  }

  // All chapters share an intrinsic grid row. A font swap or resize can change
  // its height; re-fit the scene instead of letting it cover the heading.
  const headingObserver = new ResizeObserver(() => { if (enhanced && !destroyed) measure(); });
  headingObserver.observe(section.querySelector('.story-heading'));

  return {
    update,
    setSuspended(value) { suspended = Boolean(value); updateMeters(); },
    destroy() { destroyed = true; preloads.length = 0; headingObserver.disconnect(); meters.destroy(); paintSkip(1); },
  };
}
