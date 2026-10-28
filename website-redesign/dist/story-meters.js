// Synthetic visual levels only. No AudioContext, audio element, device capture,
// media permission, engine connection or sound output is involved.
const clamp = value => Math.max(0, Math.min(1, value));
const smooth = value => value * value * (3 - 2 * value);
const hash = value => {
  const n = Math.sin(value * 127.1 + 311.7) * 43758.5453;
  return n - Math.floor(n);
};
const noise = (time, seed) => {
  const cell = Math.floor(time), fraction = smooth(time - cell);
  return hash(cell + seed) * (1 - fraction) + hash(cell + seed + 1) * fraction;
};

// Unequal sentence lengths and breath gaps, repeated as a quiet visual rhythm.
// The level within each sentence varies continuously, rather than restarting
// a large attack for every syllable.
let phraseCycle = 0;
const phrases = Array.from({ length: 16 }, (_, index) => {
  const start = phraseCycle;
  const hold = 1.5 + 1.3 * hash(index + 37);
  const gap = .45 + .45 * hash(index + 59);
  phraseCycle += .09 + hold + .22 + gap;
  return { start, hold, end: phraseCycle };
});
const musicAccents = [-8.5, -10.5, -9.5, -10.5];
export const duckingExample = { thresholdDb: -42, ratio: 8 };

export function visualLevels(seconds, hasMusic) {
  const cycleTime = seconds % phraseCycle;
  const phrase = phrases.find(item => cycleTime < item.end) ?? phrases[phrases.length - 1];
  const phase = cycleTime - phrase.start;
  const envelope = smooth(clamp(phase / .09))
    * smooth(clamp((.09 + phrase.hold + .22 - phase) / .22));
  const speakingLevel = -16 + (noise(seconds * 2.3, 19) - .5) * 4
    + (noise(seconds * .7, 43) - .5) * 2;
  // Fine irregular movement at the held level and the quiet floor. Taper it
  // outside the transitions so the approved phrase attack/release stays intact.
  const speakingFlutter = (noise(seconds * 10.3, 101) - .5) * 1.8
    * smooth(clamp((phase - .18) / .08))
    * smooth(clamp((.09 + phrase.hold - phase) / .08));
  const quietFlutter = (noise(seconds * 8.7, 137) - .5)
    * smooth(clamp((phase - (.09 + phrase.hold + .22)) / .08))
    * smooth(clamp((phrase.end - cycleTime) / .08));
  const mic = -57 + (speakingLevel + 57) * envelope + speakingFlutter + quietFlutter;
  // A steady silent 120 BPM kick: quick rise, a short hold, then a rounded
  // decay to the ongoing music floor. Four-beat accents keep the pulse clear.
  const beat = Math.floor(seconds * 2), beatTime = seconds % .5;
  const kick = smooth(clamp(beatTime / .022))
    * (1 - smooth(clamp((beatTime - .10) / .24)));
  const musicFloor = -29 + 1.2 * noise(seconds * .8, 71);
  const music = musicFloor + (musicAccents[beat % 4] - musicFloor) * kick;
  // A connected trigger lowers the illustrated music while the voice rises.
  const reduction = hasMusic ? Math.max(0, mic - duckingExample.thresholdDb) * (1 - 1 / duckingExample.ratio) : 0;
  const share = hasMusic ? Math.min(-2, Math.max(mic, music - reduction) + 1.5) : mic;
  return { mic, tone: mic, music, duck: music - reduction, share };
}

export function mountStoryMeters(section) {
  const images = new Map([...section.querySelectorAll('[data-story-meter]')]
    .map(element => [element.dataset.storyMeter, {
      element, height: Number(element.dataset.meterHeight),
      laneHeight: Number(element.dataset.meterLane),
    }]));
  let active = false, hasMusic = false, hasTone = false, destroyed = false;
  let frame = 0, previousTime = null, elapsed = 0, lastPaint = -Infinity;
  const levels = { mic: -60, tone: -60, music: -60, duck: -60, share: -60 };
  const frameInterval = 1000 / 24;

  function tick(now) {
    frame = 0;
    if (!active || destroyed) return;
    const delta = previousTime === null ? 0 : Math.min(100, now - previousTime);
    previousTime = now;
    elapsed += delta;
    if (now - lastPaint >= frameInterval - .25) {
      const step = Math.min(.1, Number.isFinite(lastPaint) ? (now - lastPaint) / 1000 : 1 / 24);
      lastPaint = now;
      const targets = visualLevels(elapsed / 1000, hasMusic);
      for (const [id, image] of images) {
        if ((id === 'music' || id === 'duck') && !hasMusic) continue;
        if (id === 'tone' && !hasTone) continue;
        // Reach normal speaking level promptly, then hold small variations.
        // A slower release lets a sentence settle into its breath gap.
        const response = id === 'music' || id === 'duck'
          ? (targets[id] > levels[id] ? 44 : 12)
          : (targets[id] > levels[id] ? 36 : 10);
        levels[id] += (targets[id] - levels[id]) * (1 - Math.exp(-step * response));
        const unlit = 100 * (14 + image.laneHeight * (1 - clamp((levels[id] + 60) / 60))) / image.height;
        // Clip a native full-scale strip; its colour thresholds never stretch.
        image.element.style.clipPath = `inset(${unlit.toFixed(2)}% 0 ${(400 / image.height).toFixed(2)}% 0)`;
      }
    }
    frame = requestAnimationFrame(tick);
  }

  return {
    setActive(value, musicVisible = false, toneVisible = false) {
      if (destroyed) return;
      const nextMusic = Boolean(musicVisible), nextTone = Boolean(toneVisible);
      // A newly revealed effect joins the signal already travelling through it.
      if (nextTone && !hasTone) levels.tone = levels.mic;
      if (nextMusic && !hasMusic) {
        const targets = visualLevels(elapsed / 1000, true);
        levels.duck = Math.max(-60, levels.music - (targets.music - targets.duck));
      }
      hasMusic = nextMusic;
      hasTone = nextTone;
      if (active === Boolean(value)) return;
      active = Boolean(value);
      if (frame) cancelAnimationFrame(frame);
      frame = 0;
      previousTime = null;
      lastPaint = -Infinity;
      if (active) frame = requestAnimationFrame(tick);
    },
    destroy() {
      destroyed = true;
      active = false;
      if (frame) cancelAnimationFrame(frame);
      frame = 0;
    },
  };
}
