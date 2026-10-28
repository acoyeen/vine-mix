import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { createHash } from 'node:crypto';
import { mountStoryMeters, visualLevels, duckingExample } from '../src/story-meters.js';
import { mountScrollStory } from '../src/scroll-story.js';

const native = JSON.parse(readFileSync(new URL('../reference-assets/nodes/meter-geometry.json', import.meta.url), 'utf8'));
const duckingNative = JSON.parse(readFileSync(new URL('../reference-assets/nodes/ducking-settings.json', import.meta.url), 'utf8'));
const duckingImage = readFileSync(new URL('../dist/assets/nodes/ducking.png', import.meta.url));
assert.equal(duckingNative.mode.toLowerCase(), 'dynamic', 'The native example must show Dynamic ducking');
assert.deepEqual(duckingExample, { thresholdDb: duckingNative.thresholdDb, ratio: duckingNative.ratio }, 'The simulated reduction must match the independently rendered native controls');
assert.equal(duckingNative.thresholdDb, -42, 'The stronger example must use the chosen -42dB threshold');
assert.equal(duckingNative.ratio, 8, 'The stronger example must use the chosen 8:1 ratio');
assert.equal(duckingNative.attackMs, 10, 'The native attack control must remain unchanged');
assert.equal(duckingNative.releaseMs, 250, 'The native release control must remain unchanged');
assert.equal(duckingImage.readUInt32BE(16), duckingNative.width);
assert.equal(duckingImage.readUInt32BE(20), duckingNative.height);
assert.equal(createHash('sha256').update(duckingImage).digest('hex'), duckingNative.imageSha256.toLowerCase(), 'Ducking metadata must describe the exact served native image');
const html = readFileSync(new URL('../src/index.html', import.meta.url), 'utf8');
assert.equal(native.virtualOutputMuted, false, 'The shared output must be unmuted');
assert.equal(native.listeningOutputMuted, true, 'The listening output must stay muted');
const meterIds = ['duck', 'mic', 'music', 'share', 'tone'];
assert.deepEqual([...html.matchAll(/data-story-meter="([^"]+)"/g)].map(match => match[1]).sort(), meterIds, 'Every active native node must have a meter while Listening stays muted');
assert.deepEqual(native.records.map(record => record.id).sort(), meterIds);
for (const record of native.records) {
  const image = readFileSync(new URL(`../dist/assets/nodes/${record.file}`, import.meta.url));
  assert.equal(image.readUInt32BE(16), record.width, 'Native meter strip width must match metadata');
  assert.equal(image.readUInt32BE(20), record.height, 'Native meter strip height must match metadata');
  assert.equal(record.clipCapLit, false, 'Synthetic levels must not fabricate clipping');
  assert.equal(record.synthetic, true);
  const tag = [...html.matchAll(/<img\b[^>]*data-story-meter="([^"]+)"[^>]*>/g)].find(match => match[1] === record.id)?.[0];
  assert.ok(tag, `Native meter image missing: ${record.id}`);
  assert.ok(tag.includes(`data-meter-height="${record.height}"`) && tag.includes(`data-meter-lane="${record.lanes[0].height}"`), 'The runtime must use measured native lane geometry');
}

// Independent signal checks over ten minutes, including quiet and voiced parts.
const distinct = new Set();
const speakingRuns = [], quietRuns = [];
let speaking = [], quietLength = 0, quietSamples = 0;
const quietFloor = [], firstPlateau = [];
for (let step = 0; step <= 36000; step++) {
  const time = step / 60;
  const solo = visualLevels(time, false), mixed = visualLevels(time, true);
  for (const level of [...Object.values(solo), ...Object.values(mixed)]) assert.ok(Number.isFinite(level) && level >= -60 && level <= 0, 'Synthetic levels must stay within the native meter scale');
  assert.equal(solo.share, solo.mic, 'Before music arrives, the shared signal follows the microphone');
  assert.equal(solo.mic, mixed.mic, 'Adding music must not change the microphone source');
  assert.equal(solo.music, mixed.music, 'Ducking must not change the music source meter');
  assert.equal(solo.tone, solo.mic, 'The tone meter must follow the full illustrated voice chain');
  assert.equal(mixed.tone, mixed.mic, 'Adding music must not change the tone signal');
  const duckedMusic = mixed.music - Math.max(0, mixed.mic - duckingNative.thresholdDb) * (1 - 1 / duckingNative.ratio);
  assert.equal(mixed.duck, duckedMusic, 'The Ducking meter must show processed music after the native 8:1 reduction');
  assert.ok(mixed.duck <= mixed.music, 'Ducking must never raise the music level');
  assert.equal(mixed.share, Math.min(-2, Math.max(mixed.mic, duckedMusic) + 1.5), 'The shared meter must reflect the stronger native ducking setting');
  assert.ok(solo.mic <= -12, 'Voice flutter must remain near normal speaking level instead of stabbing toward full scale');
  if (solo.mic < -56) quietFloor.push(solo.mic);
  if (time >= .3 && time <= 1.5) firstPlateau.push(solo.mic);
  if (solo.mic > -22) speaking.push(solo.mic);
  else if (speaking.length) { speakingRuns.push(speaking); speaking = []; }
  if (solo.mic < -52) { quietSamples++; quietLength++; }
  else if (quietLength) { quietRuns.push(quietLength); quietLength = 0; }
  distinct.add(mixed.share.toFixed(1));
}
assert.ok(distinct.size > 100, 'Continuous level animation must contain meaningful variation');
assert.ok(quietSamples > 6000 && quietSamples < 14000, 'Speech must include distinct breath gaps while spending most time speaking');
assert.ok(speakingRuns.length > 150, 'The demonstration must continue alternating complete phrases and pauses');
for (const run of speakingRuns) {
  assert.ok(run.length / 60 >= 1.5 && run.length / 60 <= 3.1, 'Speaking must hold for a complete phrase, not reset between syllables');
  const plateau = run.slice(12, -12);
  assert.ok(Math.max(...plateau) - Math.min(...plateau) <= 7, 'Speaking plateaus and fine flutter must contain only small volume variation');
  assert.ok(plateau.every((level, index) => !index || Math.abs(level - plateau[index - 1]) < 1), 'Within-phrase motion must be smooth without repeated sharp spikes');
}
for (const run of quietRuns.slice(1)) assert.ok(run / 60 >= .4 && run / 60 <= 1.05, 'Quiet gaps must be distinct but brief');
assert.ok(visualLevels(.15, false).mic > -20, 'The first phrase must reach normal speaking volume promptly');
assert.ok(quietFloor.every(level => level >= -57.51), 'Quiet flutter must stay within half a decibel of the floor');
assert.ok(quietFloor.filter(level => level < -57.1).length > 1000 && quietFloor.filter(level => level > -56.9).length > 1000, 'The quiet floor must have fine movement on both sides of its resting level');
const turns = firstPlateau.reduce((count, level, index, values) => index < 2 ? count
  : count + (Math.sign(level - values[index - 1]) !== Math.sign(values[index - 1] - values[index - 2]) ? 1 : 0), 0);
assert.ok(turns >= 6, 'The speaking plateau must have fine irregular movement instead of only a slow swell');
for (const [time, expected] of [[.05, -33.689973868158546], [.1, -16.811304434521446], [.125, -16.672701461748787], [.15, -16.5165015676317], [.18, -16.312616911924025]]) {
  assert.ok(Math.abs(visualLevels(time, false).mic - expected) < 1e-9, 'Fine flutter must preserve the approved initial rise');
}
// Captured from the approved voice before changing the music rhythm. Cover
// plateaus, floor flutter and later phrases so music cannot reshape the voice.
for (const [time, expected] of [[.3, -15.29893444511579], [.7, -16.28351667079394], [1.1, -17.333112812675502], [1.5, -15.686630659531515], [2, -56.96414875815151], [2.2, -57.160099456861296], [3.1, -15.827932098956333], [4.2, -17.941027022607525], [7.5, -15.21544763720006], [14.6, -56.903671530826024], [31.25, -57.118832437395895], [80.4, -15.335930896119503], [333.3, -35.25886866371113]]) {
  assert.ok(Math.abs(visualLevels(time, true).mic - expected) < 1e-9, 'The approved voice must remain unchanged when music gets a beat');
}
for (const [time, expected] of [[.01, -19.645512938511878], [.05, -8.5], [.16, -11.56467352676377], [.23, -19.525173446423093], [.3, -26.64304232953576], [.45, -28.065339796986542], [.55, -10.5], [1.05, -9.5], [1.55, -10.5], [7.23, -20.035993693455133], [31.4, -28.744668784016945]]) {
  assert.ok(Math.abs(visualLevels(time, true).music - expected) < 1e-9, 'Stronger ducking must preserve the approved unprocessed music rhythm');
}

for (let beat = 0; beat < 240; beat++) {
  const at = phase => visualLevels(beat * .5 + phase, true).music;
  assert.ok(at(.05) > -11 && at(.45) < -27, 'Each 120 BPM beat must clearly rise above its ongoing music floor');
  assert.ok(at(.05) - at(.45) > 16, 'Kick contrast must remain clear on every beat');
  assert.ok(Math.abs(at(.04) - at(.09)) < .1, 'Each kick must briefly hold its level after arriving');
  assert.ok(at(.16) > at(.23) && at(.23) > at(.30), 'The held kick must decay smoothly toward the music floor');
}

function clock() {
  const pending = new Map();
  let serial = 0;
  globalThis.requestAnimationFrame = callback => { pending.set(++serial, callback); return serial; };
  globalThis.cancelAnimationFrame = id => pending.delete(id);
  return {
    pending,
    tick(time) {
      const callbacks = [...pending.values()];
      pending.clear();
      callbacks.forEach(callback => callback(time));
    },
  };
}

function element(dataset = {}) {
  const attributes = new Map(), classes = new Set();
  const styles = {}, writes = [];
  return {
    dataset, writes, children: [], textContent: '',
    style: new Proxy(styles, {
      set(target, key, value) { target[key] = value; writes.push([key, value]); return true; },
      get(target, key) { return key === 'setProperty' ? (name, value) => { target[name] = value; writes.push([name, value]); } : target[key]; },
    }),
    classList: { toggle(name, on) { if (on) classes.add(name); else classes.delete(name); } },
    setAttribute(name, value) { attributes.set(name, String(value)); },
    getAttribute(name) { return attributes.get(name) ?? null; },
    removeAttribute(name) { attributes.delete(name); },
    append(...children) { this.children.push(...children); },
    querySelectorAll() { return []; },
  };
}

function fixture() {
  const meters = new Map(native.records.map(record => [record.id, element({ storyMeter: record.id, meterHeight: String(record.height), meterLane: String(record.lanes[0].height) })]));
  const section = { querySelectorAll: selector => selector === '[data-story-meter]' ? [...meters.values()] : [] };
  const snapshots = () => Object.fromEntries([...meters].map(([id, meter]) => [id, meter.style.clipPath]));
  const count = id => meters.get(id).writes.length;
  const level = id => {
    const meter = meters.get(id), record = native.records.find(item => item.id === id);
    const top = Number(/^inset\(([\d.]+)%/.exec(meter.style.clipPath)[1]);
    return (1 - (top * record.height / 100 - record.lanes[0].y) / record.lanes[0].height) * 60 - 60;
  };
  const assertClips = () => {
    for (const [id, meter] of meters) for (const [key, value] of meter.writes) {
      assert.equal(key, 'clipPath', 'Animation must clip the fixed native strip, not stretch it');
      const match = /^inset\(([\d.]+)% 0 ([\d.]+)% 0\)$/.exec(value);
      assert.ok(match, `Finite clipping values required: ${id}: ${value}`);
      const top = Number(match[1]), bottom = Number(match[2]);
      assert.ok(top >= 0 && bottom >= 0 && top + bottom <= 100.01, 'Clipping must stay inside the native meter');
      const record = native.records.find(item => item.id === id);
      assert.ok(top >= 100 * record.lanes[0].y / record.height - .01, 'Clipping must exclude the native clip cap');
    }
  };
  return { section, meters, snapshots, count, level, assertClips };
}

{
  const raf = clock(), view = fixture(), controller = mountStoryMeters(view.section);
  assert.equal(raf.pending.size, 0, 'Mounting an offscreen story must not start a clock');
  controller.setActive(true, false);
  for (let i = 0; i < 20; i++) controller.setActive(true, false);
  assert.equal(raf.pending.size, 1, 'Repeated visible-state updates must share one clock');
  for (let i = 0; i <= 288; i++) {
    raf.tick(i * 1000 / 144);
    assert.equal(raf.pending.size, 1, 'An active meter controller must own exactly one callback');
  }
  assert.ok(view.count('mic') >= 40 && view.count('mic') <= 49, 'Paint rate must remain capped at24fps over2seconds');
  assert.equal(view.count('share'), view.count('mic'));
  assert.equal(view.count('music'), 0, 'The hidden music node must not receive meter writes');
  assert.equal(view.count('duck'), 0, 'The hidden ducking node must not receive meter writes');
  assert.equal(view.count('tone'), 0, 'The hidden tone node must not receive meter writes');
  controller.setActive(true, false, true);
  raf.tick(2050);
  assert.ok(view.count('tone') > 0, 'The tone meter joins when its node becomes visible');
  assert.equal(view.count('music'), 0, 'Revealing tone must not animate hidden music');
  assert.equal(view.count('duck'), 0, 'Revealing tone must not animate hidden ducking');
  controller.setActive(true, true, true);
  raf.tick(2100);
  assert.ok(view.count('music') > 0, 'The music meter joins when its node becomes visible');
  assert.ok(view.count('duck') > 0, 'The processed-music meter joins when Ducking becomes visible');
  controller.setActive(false, true, true);
  const paused = view.snapshots(), counts = [...view.meters.values()].map(meter => meter.writes.length);
  assert.equal(raf.pending.size, 0, 'Pause/offscreen state must cancel the clock');
  raf.tick(100000);
  assert.deepEqual(view.snapshots(), paused, 'Stopped meters must preserve their last frame');
  assert.deepEqual([...view.meters.values()].map(meter => meter.writes.length), counts, 'Stopped meters must not write styles');
  controller.setActive(true, true, true);
  assert.equal(raf.pending.size, 1);
  controller.destroy(); controller.destroy(); controller.setActive(true, true);
  assert.equal(raf.pending.size, 0, 'Destroy must be idempotent and prevent restart');
  view.assertClips();
}

// Revealing Tone must join the already moving voice rather than start a second
// pretend microphone attack. Hiding it must stop its writes until it rejoins.
{
  const raf = clock(), view = fixture(), controller = mountStoryMeters(view.section);
  controller.setActive(true, false, false);
  for (let i = 0; i <= 144; i++) raf.tick(i * 1000 / 144);
  assert.equal(view.count('tone'), 0);
  controller.setActive(true, false, true);
  for (let i = 145; i <= 216; i++) {
    raf.tick(i * 1000 / 144);
    if (view.count('tone')) assert.ok(Math.abs(view.level('tone') - view.level('mic')) < .02, 'A visible tone meter must match the current voice immediately and throughout the phrase');
  }
  const toneWrites = view.count('tone');
  controller.setActive(true, false, false);
  for (let i = 217; i <= 432; i++) raf.tick(i * 1000 / 144);
  assert.equal(view.count('tone'), toneWrites, 'Hiding Tone must stop its meter writes');
  controller.setActive(true, false, true);
  for (let i = 433; i <= 444; i++) raf.tick(i * 1000 / 144);
  assert.ok(Math.abs(view.level('tone') - view.level('mic')) < .02, 'Revealing Tone again must rejoin the current voice without lag');
  controller.destroy();
  view.assertClips();
}

// The Ducking strip is the processed music output. Its steady beat should be
// lower during speech and recover to the original music during a quiet gap.
{
  const raf = clock(), view = fixture(), controller = mountStoryMeters(view.section);
  controller.setActive(true, true, true);
  const speakingReduction = [], recoveredGap = [];
  for (let i = 0; i <= 432; i++) {
    const time = i / 144, before = view.count('duck');
    raf.tick(time * 1000);
    if (view.count('duck') > before) {
      const reduction = view.level('music') - view.level('duck');
      assert.ok(reduction >= -.02, 'The painted Ducking output must never exceed the unprocessed music');
      if (time >= .3 && time <= 1.5) speakingReduction.push(reduction);
      if (time >= 2.1 && time <= 2.45) recoveredGap.push(reduction);
    }
  }
  assert.ok(speakingReduction.every(value => value > 21 && value < 25), 'Visible Ducking beats must show the stronger roughly22–24dB dip during stable speech');
  assert.ok(recoveredGap.every(value => Math.abs(value) < .5), 'The Ducking output must recover to the original music in the quiet gap');
  const musicWrites = view.count('music'), duckWrites = view.count('duck');
  controller.setActive(true, false, true);
  for (let i = 433; i <= 504; i++) raf.tick(i * 1000 / 144);
  assert.equal(view.count('music'), musicWrites, 'Hidden music must stop writing');
  assert.equal(view.count('duck'), duckWrites, 'Hidden Ducking must stop writing');
  console.log(`Effect meters: Tone follows the painted voice within 0.02dB; painted Ducking reduces music ${Math.min(...speakingReduction).toFixed(1)}–${Math.max(...speakingReduction).toFixed(1)}dB during speech and recovers within ${Math.max(...recoveredGap.map(Math.abs)).toFixed(2)}dB during the gap.`);
  controller.destroy();
  view.assertClips();
}

function resumeAfter(gap) {
  const raf = clock(), view = fixture(), controller = mountStoryMeters(view.section);
  controller.setActive(true, true);
  for (let i = 0; i <= 120; i++) raf.tick(i * 1000 / 120);
  controller.setActive(false, true);
  raf.tick(1000 + gap);
  controller.setActive(true, true);
  for (let i = 0; i <= 12; i++) raf.tick(1000 + gap + i * 1000 / 120);
  const result = view.snapshots();
  controller.destroy();
  return result;
}

// Check the painted result: it should rise once, stay near speaking volume,
// then release into a real pause, rather than repeatedly stabbing upward.
{
  const raf = clock(), view = fixture(), controller = mountStoryMeters(view.section);
  controller.setActive(true, false);
  const paintTimes = [];
  for (let i = 0; i <= 2880; i++) {
    const before = view.count('mic');
    raf.tick(i * 1000 / 144);
    if (view.count('mic') > before) paintTimes.push(i * 1000 / 144);
  }
  const record = native.records.find(item => item.id === 'mic');
  const painted = view.meters.get('mic').writes.map(([, clip]) => {
    const top = Number(/^inset\(([\d.]+)%/.exec(clip)[1]);
    return (1 - (top * record.height / 100 - 14) / record.lanes[0].height) * 60 - 60;
  });
  const firstSpeaking = painted.findIndex(level => level > -20);
  assert.ok(paintTimes[firstSpeaking] >= 100 && paintTimes[firstSpeaking] <= 180, 'The visible meter must reach speaking volume in 100–180ms on first view');
  const firstPlateau = painted.filter((_, index) => paintTimes[index] >= 200 && paintTimes[index] <= 1500);
  assert.ok(firstPlateau.every(level => level >= -20 && level <= -12), 'The visible meter must sustain a normal speaking plateau');
  assert.ok(Math.max(...firstPlateau) - Math.min(...firstPlateau) < 6, 'The visible plateau should have only small smooth movement');
  assert.ok(firstPlateau.every((level, index) => !index || Math.abs(level - firstPlateau[index - 1]) < 2), 'The visible plateau must not contain repeated sharp attacks');
  assert.ok(Math.max(...painted) <= -12, 'The visible voice meter must avoid near-clipping spikes');
  assert.ok(painted.some(level => level < -52), 'The visible meter must fall back during phrase pauses');
  console.log(`Meter timing: first visible speaking level at ${paintTimes[firstSpeaking].toFixed(0)}ms; first plateau ${Math.min(...firstPlateau).toFixed(1)} to ${Math.max(...firstPlateau).toFixed(1)}dB; source speaking runs ${(Math.min(...speakingRuns.map(run => run.length)) / 60).toFixed(2)}–${(Math.max(...speakingRuns.map(run => run.length)) / 60).toFixed(2)}s; quiet gaps ${(Math.min(...quietRuns.slice(1)) / 60).toFixed(2)}–${(Math.max(...quietRuns.slice(1)) / 60).toFixed(2)}s.`);
  controller.destroy();
  view.assertClips();
}

// Ensure the actual 24fps painted music strip retains the regular pulse after
// smoothing, rather than merely generating sharp target values between paints.
{
  const raf = clock(), view = fixture(), controller = mountStoryMeters(view.section);
  controller.setActive(true, true);
  const paintTimes = [];
  for (let i = 0; i <= 1728; i++) {
    const before = view.count('music');
    raf.tick(i * 1000 / 144);
    if (view.count('music') > before) paintTimes.push(i * 1000 / 144);
  }
  const record = native.records.find(item => item.id === 'music');
  const painted = view.meters.get('music').writes.map(([, clip], index) => {
    const top = Number(/^inset\(([\d.]+)%/.exec(clip)[1]);
    return { time: paintTimes[index], level: (1 - (top * record.height / 100 - 14) / record.lanes[0].height) * 60 - 60 };
  });
  const peaks = [], contrasts = [];
  for (let beat = 1; beat < 24; beat++) {
    const samples = painted.filter(item => item.time >= beat * 500 && item.time < (beat + 1) * 500);
    const peak = samples.reduce((best, item) => item.level > best.level ? item : best);
    const low = Math.min(...samples.map(item => item.level));
    assert.ok(peak.time - beat * 500 <= 170, 'A painted kick must arrive promptly on each beat');
    assert.ok(peak.level > -12 && low < -25, 'Painted music must visibly pulse between kick and floor');
    assert.ok(peak.level - low > 14, 'Smoothing must preserve visible beat contrast');
    peaks.push(peak.time); contrasts.push(peak.level - low);
  }
  assert.ok(peaks.every((time, index) => !index || Math.abs(time - peaks[index - 1] - 500) <= 45), 'Painted kicks must keep a steady 120 BPM cadence within one paint interval');
  console.log(`Music timing: 120 BPM; painted kick contrast ${Math.min(...contrasts).toFixed(1)}–${Math.max(...contrasts).toFixed(1)}dB; source floor -29 to -27.8dB and kick accents -10.5 to -8.5dB.`);
  controller.destroy();
  view.assertClips();
}
assert.deepEqual(resumeAfter(200), resumeAfter(600000), 'Returning from a long pause must not consume hidden wall-clock time');

function stalledFrame(delay) {
  const raf = clock(), view = fixture(), controller = mountStoryMeters(view.section);
  controller.setActive(true, true); raf.tick(0); raf.tick(delay);
  const result = view.snapshots(); controller.destroy(); return result;
}
assert.deepEqual(stalledFrame(100), stalledFrame(5000), 'A stalled foreground frame must not jump through seconds of synthetic audio');

// Exercise the actual story controller's visibility and preference boundaries,
// with DOM measurements supplied by a small fixture rather than a browser.
{
  const raf = clock(), view = fixture();
  const media = { matches: true };
  globalThis.matchMedia = () => media;
  globalThis.window = { innerHeight: 900 };
  globalThis.innerHeight = 900;
  globalThis.document = { createElementNS: () => element() };
  globalThis.Image = class {};
  const observers = [];
  globalThis.ResizeObserver = class {
    constructor(callback) { this.callback = callback; this.target = null; this.disconnected = false; observers.push(this); }
    observe(target) { this.target = target; }
    disconnect() { this.disconnected = true; }
  };
  const world = element(), stage = element(), heading = { offsetHeight: 160 }, cables = element();
  const copies = [element(), element(), element()];
  const nodes = ['mic', 'tone', 'listen', 'share', 'music', 'duck'].map(id => element({ storyNode: id }));
  const toneImages = [element(), element()];
  nodes.find(node => node.dataset.storyNode === 'tone').querySelectorAll = () => toneImages;
  let stageBounds = { top: 160, bottom: 780 };
  stage.clientWidth = 1280;
  stage.getBoundingClientRect = () => stageBounds;
  const section = Object.assign(element(), {
    querySelector: selector => ({ '.story-world': world, '.story-stage': stage, '.story-heading': heading, '.story-cables': cables, '.story-progress-fill': element() })[selector] ?? null,
    querySelectorAll: selector => ({ '[data-story-meter]': [...view.meters.values()], '[data-story-node]': nodes, '[data-chapter]': copies })[selector] ?? [],
    getBoundingClientRect: () => ({ top: -2852, bottom: 1148, height: 4000 }),
  });
  const story = mountScrollStory(section);
  const normal = { paused: false, reduced: false, highContrast: false };
  story.update(normal);
  assert.equal(observers.length, 1, 'The story must use one heading size observer');
  assert.equal(observers[0].target, heading, 'The observer must track the intrinsic heading row');
  const originalStageHeight = parseFloat(section.style['--story-stage-height']);
  assert.ok(window.innerHeight - (originalStageHeight + heading.offsetHeight + 24 + 32 + 26) >= 72 - .01,
    'The pinned scene must leave the floating logo and heading clear at the top');
  heading.offsetHeight += 120;
  observers[0].callback();
  assert.ok(Math.abs(parseFloat(section.style['--story-stage-height']) - (originalStageHeight - 120)) < .01, 'A taller heading must shrink the scene by the same amount without waiting for a window resize');
  assert.equal(raf.pending.size, 0, 'The page lifecycle initially suspends meters');
  story.setSuspended(false);
  assert.equal(raf.pending.size, 1, 'A visible active story starts its meter clock');
  raf.tick(0); raf.tick(50);
  assert.ok(meterIds.every(id => view.count(id) > 0), 'The final scene must animate all five unmuted nodes');
  story.update({ ...normal, paused: true });
  assert.equal(raf.pending.size, 0, 'The page pause control must stop meters');
  story.update(normal);
  story.setSuspended(true);
  assert.equal(raf.pending.size, 0, 'A hidden/inactive page must stop meters immediately');
  story.setSuspended(false);
  story.update({ ...normal, reduced: true });
  assert.equal(raf.pending.size, 0, 'Reduced motion must stop meters');
  story.update(normal);
  story.update({ ...normal, highContrast: true });
  assert.equal(raf.pending.size, 0, 'High contrast fallback must stop meters');
  story.update(normal);
  media.matches = false; story.update(normal);
  assert.equal(raf.pending.size, 0, 'A small-window static fallback must stop meters');
  media.matches = true; story.update(normal);
  stageBounds = { top: 1000, bottom: 1620 }; story.update(normal);
  assert.equal(raf.pending.size, 0, 'An offscreen stage must stop meters');
  stageBounds = { top: 160, bottom: 780 }; story.update(normal);
  assert.equal(raf.pending.size, 1);
  story.destroy();
  assert.equal(raf.pending.size, 0, 'Destroying the story must cancel its meter callback');
  assert.equal(observers[0].disconnected, true, 'Destroying the story must disconnect its heading observer');
  const destroyedWrites = section.writes.length;
  heading.offsetHeight += 60; observers[0].callback();
  assert.equal(section.writes.length, destroyedWrites, 'An already queued heading callback must not write after destruction');
  view.assertClips();
}

console.log('PASS: five native strips and mute states;36,001 speech/ducking samples; approved voice goldens, fine voice/quiet flutter, steady 120 BPM music, matching Tone and processed Ducking; one capped clock; no hidden-effect/music writes; pause/offscreen/reduced-motion/high-contrast/static fallback; heading reflow; resume/stall timing; destruction.');
