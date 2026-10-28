// Approved inverted word-reader sequence; the page owns playback and visibility.
// Focus and punctuation rhythm adapted from snowfluke/rsvp-speed-reader (MIT).
// See PROVENANCE.md and assets/licenses/SnowflukeRSVP-MIT.txt.
export function readerFocusIndex(text) {
  const length = text.length;
  return length <= 1 ? 0 : length <= 3 ? 1 : length <= 5 ? 2 : length <= 9 ? 3 : 4;
}

export function readerPhraseDurations(words, wpm = 350) {
  if (!words.length) return [];
  const weights = words.map(word => /[.!?]$/.test(word) ? 2 : /[,;:]$/.test(word) ? 1.5 : 1);
  const average = weights.reduce((sum, weight) => sum + weight, 0) / weights.length;
  return weights.map(weight => (60000 / wpm) * weight / average);
}

let readerSerial = 0;

export function mountHeroReader(element) {
  const phrases = [
    'Bring your voice and your music together in one mix.',
    'Choose what you hear and what you share with everyone else.',
    'Connect your apps and microphone with a few simple cables.',
    'Shape your sound with effects and see exactly where it goes.',
    'Your microphone and your music can share the same stage.',
    'Build a mix that sounds the way you want it to.',
    'Follow the cables and see where every sound is going.',
    'Keep your listening mix separate from the sound you send.',
    'A little more music. A little less microphone. Your call.',
    'Start with a sound and decide where it goes next.',
    'Bring the apps you use into the mix you make.',
    'Give your voice and your music their own space.',
    'Turn a collection of sounds into something worth sharing.',
    'Make room for your voice without losing the music.',
    'One workspace for the sounds that make your setup yours.',
    'Trace a sound from its source all the way to its output.',
    'Find the balance between what you say and what you play.',
    'A microphone. Some music. A mix that feels like you.',
    'Connect the sounds you want and leave the rest out.',
    'See the connections behind the sound you are creating.',
    'Give every sound a place in the bigger picture.',
    'From a simple voice mix to a workspace of your own.',
    'Move a level, hear the difference, find your balance.',
    'Your sound starts here. Where it goes is up to you.'
  ].map(phrase => phrase.split(' '));
  // Match the reference's default punctuation timing, normalized within each
  // phrase. Logo interludes are separate; optional length variability stays off.
  const phraseDurations = phrases.map(words => readerPhraseDurations(words));

  // The approved inverted sequence. Every effect uses this one playback clock.
  const symbolHold = 700;
  const revealDuration = 390;
  const wordmarkHold = 3800;
  const shineDelay = 250;
  const shineDuration = 550;
  const glintDuration = 340;
  const glintStartFraction = .68;
  const logoFadeDuration = 220;
  const logoGap = 250;
  const morphDuration = 400;
  const firstWordCrossfade = 150;
  const wordCrossfade = 50;

  const reduced = matchMedia('(prefers-reduced-motion: reduce)');
  const initialArtwork = element.querySelector('.brand-art');
  // Reuse the monochrome artwork without changing its vector geometry.
  const wordmarkTemplate = initialArtwork.cloneNode(true);
  const view = {
    stack: element.querySelector('.effect-stack'),
    from: element.querySelector('.outgoing'),
    to: element.querySelector('.incoming')
  };
  const brandCopies = new WeakMap();
  const clipPrefix = `hero-reader-${++readerSerial}`;
  let clipSerial = 0;

  // A null token is the brand sequence; wordIndex -1 is its phrase position.
  let phraseOrder = [];
  let paused = true;
  let ready = false;
  let destroyed = false;
  let reducedMotionActive = reduced.matches;
  let phraseIndex = takePhrase();
  let wordIndex = -1;
  let current = null;
  let elapsed = 0;
  let lastTime = null;
  let frame = 0;
  let transitioning = false;
  let brandEntrance = 0;
  let paintMode = '';

  const clamp = value => Math.min(1, Math.max(0, value));
  const ease = value => value * value * (3 - 2 * value);

  function takePhrase(previous = -1) {
    if (phraseOrder.length === 0) {
      phraseOrder = phrases.map((_, index) => index);
      for (let index = phraseOrder.length - 1; index > 0; index--) {
        const other = Math.floor(Math.random() * (index + 1));
        [phraseOrder[index], phraseOrder[other]] = [phraseOrder[other], phraseOrder[index]];
      }
      // Consume the whole pool before repeating, including round boundaries.
      const last = phraseOrder.length - 1;
      if (last > 0 && phraseOrder[last] === previous) {
        [phraseOrder[0], phraseOrder[last]] = [phraseOrder[last], phraseOrder[0]];
      }
    }
    return phraseOrder.pop();
  }

  function svgNode(tag, attributes = {}) {
    const node = document.createElementNS('http://www.w3.org/2000/svg', tag);
    Object.entries(attributes).forEach(([key, value]) => node.setAttribute(key, String(value)));
    return node;
  }

  function prepareBrand(artwork) {
    // Every overlapping copy owns its clip and gradient references.
    const id = `${clipPrefix}-clip-${++clipSerial}`;
    artwork.querySelectorAll('.logo-shine-def, .logo-shine').forEach(node => node.remove());
    artwork.querySelector('.lettering-mask').setAttribute('id', id);
    artwork.querySelector('.lettering-clip').setAttribute('clip-path', `url(#${id})`);
    const symbol = artwork.querySelector('.symbol-motion');
    const lettering = artwork.querySelector('.lettering-motion');
    const clipWindow = artwork.querySelector('.lettering-window');
    const shine = createWordmarkShine(artwork, symbol, lettering, id);
    return { symbol, lettering, clipWindow, ...shine };
  }

  function createWordmarkShine(artwork, symbol, lettering, id) {
    const originalSymbol = symbol.firstElementChild;
    const clip = svgNode('clipPath', {
      id: `${id}-shine-clip`, class: 'logo-shine-def', clipPathUnits: 'userSpaceOnUse'
    });
    // Direct clip shapes preserve the symbol transform and lettering counters.
    Array.from(originalSymbol.children).forEach(path => {
      const shape = path.cloneNode(true);
      shape.setAttribute('transform', originalSymbol.getAttribute('transform'));
      clip.append(shape);
    });
    lettering.querySelectorAll('path, rect').forEach(path => {
      const shape = path.cloneNode(true);
      if (shape.hasAttribute('fill-rule')) shape.setAttribute('clip-rule', shape.getAttribute('fill-rule'));
      clip.append(shape);
    });
    const gradient = svgNode('linearGradient', {
      id: `${id}-shine-gradient`, class: 'logo-shine-def shine-gradient',
      gradientUnits: 'userSpaceOnUse', x1: -140, y1: 0, x2: 140, y2: 0
    });
    // A single silver opacity peak remains visible against the pure-white art.
    [[0, 0], [.3, .12], [.5, .42], [.7, .12], [1, 0]].forEach(([offset, opacity]) => {
      gradient.append(svgNode('stop', { offset, 'stop-color': '#64676B', 'stop-opacity': opacity }));
    });
    artwork.querySelector('defs').append(clip, gradient);
    const shine = svgNode('g', {
      class: 'logo-shine', transform: 'translate(0 0)', 'pointer-events': 'none', 'aria-hidden': 'true'
    });
    const band = svgNode('rect', {
      class: 'shine-band', x: 520, y: 230, width: 1200, height: 260, opacity: 0,
      fill: `url(#${id}-shine-gradient)`, 'clip-path': `url(#${id}-shine-clip)`
    });
    const glint = svgNode('path', {
      class: 'shine-glint', fill: '#FFFFFF', opacity: 0,
      d: 'M0 -8 C.25 -1 1 -.25 8 0 C1 .25 .25 1 0 8 C-.25 1 -1 .25 -8 0 C-1 -.25 -.25 -1 0 -8Z'
    });
    glint.style.filter = 'drop-shadow(0 0 1px rgba(255, 255, 255, .55))';
    shine.append(band, glint);
    artwork.append(shine);
    return { band, gradient, glint };
  }

  function slideEase(progress) {
    // Sample the original collapse curve backwards for the approved reveal.
    const t = clamp(progress) ** 2;
    return t * t * t * (t * (6 * t - 15) + 10);
  }

  function poseBrand(layer, collapsedProgress) {
    const copy = brandCopies.get(layer);
    if (!copy) return;
    const amount = slideEase(collapsedProgress);
    if (copy.brandPose === amount) return;
    copy.brandPose = amount;
    // Original symbol centre (678,354) moves to artwork centre (1115,356).
    copy.symbol.setAttribute('transform', `translate(${437 * amount} ${2 * amount})`);
    copy.lettering.setAttribute('transform', `translate(${-440 * amount} ${2 * amount})`);
    copy.clipWindow.setAttribute('x', String(778 + 437 * amount));
  }

  function poseShine(layer, holdElapsed) {
    const copy = brandCopies.get(layer);
    if (!copy) return;
    const progress = (holdElapsed - shineDelay) / shineDuration;
    const active = !reduced.matches && progress > 0 && progress < 1;
    const shineProgress = clamp(progress);
    if (copy.shineActive !== active) {
      copy.shineActive = active;
      copy.band.setAttribute('opacity', active ? '1' : '0');
    }
    if (copy.shineProgress !== shineProgress) {
      copy.shineProgress = shineProgress;
      copy.gradient.setAttribute('gradientTransform', `translate(${410 + 1400 * ease(shineProgress)} 350) rotate(22)`);
    }
    const glintProgress = (holdElapsed - shineDelay - shineDuration * glintStartFraction) / glintDuration;
    const strength = !reduced.matches && glintProgress > 0 && glintProgress < 1
      ? Math.sin(Math.PI * glintProgress) ** 2 : 0;
    if (copy.glintStrength !== strength) {
      copy.glintStrength = strength;
      copy.glint.setAttribute('opacity', String(strength));
      copy.glint.setAttribute('transform', `translate(1650 304) scale(${3 * (.35 + .65 * strength)})`);
    }
  }

  function brandSpan() {
    return brandEntrance + symbolHold + revealDuration + wordmarkHold + logoFadeDuration + logoGap;
  }

  function paintBrandSequence() {
    const holdElapsed = elapsed - brandEntrance;
    const reveal = clamp((holdElapsed - symbolHold) / revealDuration);
    poseBrand(view.to, reduced.matches ? 0 : 1 - reveal);
    poseShine(view.to, holdElapsed - symbolHold - revealDuration);
  }

  function span(text, className) {
    const node = document.createElement('span');
    node.className = className;
    node.textContent = text;
    return node;
  }

  function setToken(layer, text) {
    paintMode = '';
    layer.classList.toggle('brand', text === null);
    brandCopies.delete(layer);
    if (text === null) {
      const artwork = wordmarkTemplate.cloneNode(true);
      brandCopies.set(layer, prepareBrand(artwork));
      layer.replaceChildren(artwork);
      poseBrand(layer, reduced.matches ? 0 : 1);
    } else {
      const anchor = readerFocusIndex(text);
      // The bold focus glyph anchors the word; surrounding spans have softer styling.
      layer.replaceChildren(span(text.slice(0, anchor), 'before'),
        span(text[anchor], 'focus-letter'), span(text.slice(anchor + 1), 'after'));
    }
  }

  function clearOutgoing() {
    view.from.replaceChildren();
    view.from.classList.remove('brand');
    brandCopies.delete(view.from);
  }

  function settle() {
    if (paintMode === 'settled') return;
    paintMode = 'settled';
    view.from.style.mixBlendMode = 'normal';
    view.to.style.mixBlendMode = 'normal';
    view.from.style.opacity = '0';
    view.from.style.filter = 'none';
    view.to.style.opacity = '1';
    view.to.style.filter = 'none';
    view.stack.style.filter = 'none';
  }

  function paintCrossfade(fraction) {
    if (paintMode !== 'crossfade') {
      paintMode = 'crossfade';
      view.stack.style.filter = 'none';
      // Complementary additive layers keep overlapping word pixels from flashing.
      view.from.style.mixBlendMode = 'plus-lighter';
      view.to.style.mixBlendMode = 'plus-lighter';
      view.from.style.filter = 'none';
      view.to.style.filter = 'none';
    }
    // The logo has already faded out; only the first word fades back in.
    view.from.style.opacity = wordIndex === 0 ? '0' : String(1 - fraction);
    view.to.style.opacity = String(fraction);
  }

  function paintMorph(fraction) {
    // Magic UI / 21st.dev Morphing Text, MIT: overlapping blur/opacity curves
    // and alpha threshold. See assets/licenses/MagicUI-MIT.txt and PROVENANCE.md.
    if (paintMode !== 'morph') {
      paintMode = 'morph';
      view.from.style.mixBlendMode = 'normal';
      view.to.style.mixBlendMode = 'normal';
      view.stack.style.filter = 'url(#reader-threshold) blur(0.00625em)';
    }
    const inverse = 1 - fraction;
    view.to.style.filter = `blur(${Math.min(8 / fraction - 8, 100) / 96}em)`;
    view.to.style.opacity = String(Math.pow(fraction, .4));
    view.from.style.filter = `blur(${Math.min(8 / inverse - 8, 100) / 96}em)`;
    view.from.style.opacity = String(Math.pow(inverse, .4));
  }

  function paint() {
    if (current === null) paintBrandSequence();
    const useMorph = current === null;
    // Punctuation normalization can shorten the first word below its original
    // fade duration. Always leave at least a quarter of its slot fully visible.
    const duration = useMorph ? morphDuration
      : Math.min(wordIndex === 0 ? firstWordCrossfade : wordCrossfade, tokenHold() * .75);
    const fraction = reduced.matches ? 1 : Math.min(1, elapsed / duration);
    if (!transitioning || fraction >= 1) {
      settle();
      transitioning = false;
      if (current === null && !reduced.matches) {
        const fadeStart = brandSpan() - logoFadeDuration - logoGap;
        const opacity = String(1 - ease(clamp((elapsed - fadeStart) / logoFadeDuration)));
        if (view.to.style.opacity !== opacity) view.to.style.opacity = opacity;
      }
      return;
    }
    if (useMorph) paintMorph(fraction);
    else paintCrossfade(fraction);
  }

  function next() {
    const previous = current;
    wordIndex++;
    if (wordIndex >= phrases[phraseIndex].length) {
      wordIndex = -1;
      phraseIndex = takePhrase(phraseIndex);
    }
    current = wordIndex < 0 ? null : phrases[phraseIndex][wordIndex];
    // Avoid constructing an outgoing logo after the deliberate empty pause.
    if (previous === null) clearOutgoing();
    else setToken(view.from, previous);
    setToken(view.to, current);
    brandEntrance = current === null ? morphDuration : 0;
    transitioning = true;
  }

  function tokenHold() {
    if (current === null) return brandSpan();
    return phraseDurations[phraseIndex][wordIndex];
  }

  function tick(now) {
    frame = 0;
    if (destroyed || paused || reduced.matches || document.hidden) return;
    if (lastTime !== null) elapsed += now - lastTime;
    lastTime = now;
    const hold = tokenHold();
    if (elapsed >= hold) {
      elapsed -= hold;
      next();
      // Avoid racing through missed words after a stalled foreground frame.
      if (elapsed >= tokenHold()) elapsed = 0;
    }
    paint();
    frame = requestAnimationFrame(tick);
  }

  function resetBrand() {
    phraseIndex = takePhrase(phraseIndex);
    wordIndex = -1;
    current = null;
    elapsed = 0;
    transitioning = false;
    brandEntrance = 0;
    clearOutgoing();
    setToken(view.to, null);
    settle();
  }

  function updatePlayback() {
    // Synchronize preferences even when a hidden tab's change event is delayed.
    if (reducedMotionActive !== reduced.matches) {
      reducedMotionActive = reduced.matches;
      resetBrand();
    }
    cancelAnimationFrame(frame);
    frame = 0;
    lastTime = null;
    if (ready && !destroyed && !paused && !reduced.matches && !document.hidden) {
      frame = requestAnimationFrame(tick);
    }
  }

  function setPaused(value) {
    const nextPaused = Boolean(value);
    if (destroyed || (paused === nextPaused && reducedMotionActive === reduced.matches)) return;
    paused = nextPaused;
    updatePlayback();
  }

  function onReducedMotionChange() {
    if (reducedMotionActive !== reduced.matches) updatePlayback();
  }

  function destroy() {
    if (destroyed) return;
    destroyed = true;
    cancelAnimationFrame(frame);
    frame = 0;
    lastTime = null;
    document.removeEventListener('visibilitychange', updatePlayback);
    reduced.removeEventListener('change', onReducedMotionChange);
  }

  // Pose synchronously before waiting on fonts: normal playback starts on the
  // centred symbol; reduced motion always presents the expanded white logo.
  brandCopies.set(view.to, prepareBrand(initialArtwork));
  poseBrand(view.to, reduced.matches ? 0 : 1);
  element.setAttribute('data-reader-ready', '');
  clearOutgoing();
  settle();
  document.addEventListener('visibilitychange', updatePlayback);
  reduced.addEventListener('change', onReducedMotionChange);
  Promise.all([
    document.fonts.load('570 1em "IBM Plex Sans Reader"'),
    document.fonts.load('700 1em "IBM Plex Sans"'),
    document.fonts.ready
  ]).catch(() => {
    // The vector and fallback font remain available if loading fails.
  }).then(() => {
    if (destroyed) return;
    ready = true;
    updatePlayback();
  });

  return { setPaused, destroy };
}
