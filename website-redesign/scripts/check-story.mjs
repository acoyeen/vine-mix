import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { geometry, nodeWidth, sceneSize, storyFrame } from '../src/story-layout.js';

// Native render metadata is independent of the animation layout. Check the
// measured image/socket geometry, including its transparent 36px border, rather
// than treating a screenshot's padding as visible control surface.
const nativeNodes = JSON.parse(readFileSync(new URL('../reference-assets/nodes/nodes.json', import.meta.url), 'utf8'));
const toneFrames = JSON.parse(readFileSync(new URL('../reference-assets/nodes/tone-geometry.json', import.meta.url), 'utf8')).records;
const files = { mic: 'physical-input.png', listen: 'listening-output.png', share: 'virtual-output.png', music: 'running-app.png', duck: 'ducking.png' };
const native = Object.fromEntries(Object.entries(files).map(([id, file]) => [id, nativeNodes.find(node => node.file === file)]));
native.tone = { ...toneFrames[0], ports: toneFrames[0].sockets, padding: 36 };
const dimensions = {};
for (const [id, node] of Object.entries(native)) {
  const scale = nodeWidth / node.width;
  dimensions[id] = [nodeWidth, node.height * scale];
  assert.equal(geometry[id].width, node.width, `Native width changed: ${id}`);
  assert.equal(geometry[id].height, node.height, `Native height changed: ${id}`);
  for (const port of node.ports.filter(port => port.kind !== 'control')) {
    const kind = port.output ? 'out' : port.kind === 'sidechain' ? 'trigger' : 'in';
    assert.deepEqual(geometry[id][kind], [port.x, port.y], `Socket must match native artwork: ${id}/${kind}`);
  }
}
for (const frame of toneFrames) {
  assert.equal(frame.width, native.tone.width, 'Tone animation frames must keep their width');
  assert.equal(frame.height, native.tone.height, 'Tone animation frames must keep their height');
  assert.deepEqual(frame.sockets, native.tone.ports, 'Tone animation must not move its sockets');
  const image = readFileSync(new URL(`../dist/assets/nodes/${frame.file}`, import.meta.url));
  assert.equal(image.readUInt32BE(16), frame.width, 'Tone PNG width must match metadata');
  assert.equal(image.readUInt32BE(20), frame.height, 'Tone PNG height must match metadata');
}
const connections = {
  'basic-listen': ['mic', 'listen', 'in'],
  'basic-share': ['mic', 'share', 'in'],
  'effect-in': ['mic', 'tone', 'in'],
  'effect-listen': ['tone', 'listen', 'in'],
  'effect-share': ['tone', 'share', 'in'],
  'tone-listen': ['tone', 'listen', 'in'],
  'music-in': ['music', 'duck', 'in'],
  trigger: ['tone', 'duck', 'trigger'],
  'voice-share': ['tone', 'share', 'in'],
  'duck-share': ['duck', 'share', 'in'],
};
const visibleThreshold = .05;
const issues = new Map();
let sampledSegments = 0;

function issue(kind, detail, progress) {
  const key = `${kind}: ${detail}`;
  const entry = issues.get(key) || { first: progress, last: progress, count: 0 };
  entry.last = progress;
  entry.count++;
  issues.set(key, entry);
}

function samplePath(d) {
  // The renderer emits move + cubic segments. Reject unfamiliar commands rather
  // than silently testing an incomplete path if its representation changes.
  const tokens = d.match(/[A-Za-z]|[-+]?(?:\d*\.)?\d+(?:[eE][-+]?\d+)?/g) || [];
  const points = [];
  let current;
  for (let index = 0; index < tokens.length;) {
    const command = tokens[index++];
    if (command === 'M') {
      current = [Number(tokens[index++]), Number(tokens[index++])];
      points.push(current);
    } else if (command === 'C') {
      const controls = [current];
      for (let k = 0; k < 3; k++) controls.push([Number(tokens[index++]), Number(tokens[index++])]);
      for (let step = 1; step <= 64; step++) {
        const t = step / 64, u = 1 - t;
        points.push([0, 1].map(axis => u ** 3 * controls[0][axis]
          + 3 * u * u * t * controls[1][axis]
          + 3 * u * t * t * controls[2][axis] + t ** 3 * controls[3][axis]));
      }
      current = controls[3];
    } else {
      throw new Error(`Unsupported path command: ${command}`);
    }
  }
  assert.ok(points.length > 1 && points.flat().every(Number.isFinite), 'Wire coordinates must be finite');
  sampledSegments += points.length - 1;
  return points;
}

function cross(a, b, c) {
  return (b[0] - a[0]) * (c[1] - a[1]) - (b[1] - a[1]) * (c[0] - a[0]);
}

function pathsCross(a, b) {
  for (let i = 1; i < a.length; i++) {
    for (let j = 1; j < b.length; j++) {
      // Intended shared source/input endpoints do not count as crossings.
      if (cross(a[i - 1], a[i], b[j - 1]) * cross(a[i - 1], a[i], b[j]) < -1e-7
        && cross(b[j - 1], b[j], a[i - 1]) * cross(b[j - 1], b[j], a[i]) < -1e-7) return true;
    }
  }
  return false;
}

function within(point, box, margin = .5) {
  return point[0] > box.x + margin && point[0] < box.x + box.width - margin
    && point[1] > box.y + margin && point[1] < box.y + box.height - margin;
}

function distance(a, b) {
  return Math.hypot(a[0] - b[0], a[1] - b[1]);
}

assert.deepEqual(storyFrame(-1), storyFrame(0), 'Progress before entry must clamp');
assert.deepEqual(storyFrame(2), storyFrame(1), 'Progress after exit must clamp');
const opening = storyFrame(0), connected = storyFrame(.16);
const openingSpan = frame => Math.max(...['mic', 'listen', 'share'].map(id => frame.poses[id][0]))
  - Math.min(...['mic', 'listen', 'share'].map(id => frame.poses[id][0]));
assert.ok(openingSpan(opening) < openingSpan(connected) * .7, 'The opening nodes must start noticeably closer together');
for (const id of ['basic-listen', 'basic-share']) {
  assert.equal(opening.wires.find(wire => wire.id === id).draw, 0, 'Opening cables must begin undrawn');
  assert.equal(connected.wires.find(wire => wire.id === id).draw, 1, 'Opening cables must finish before the tone insertion');
  const halfway = storyFrame(.08).wires.find(wire => wire.id === id).draw;
  assert.ok(halfway > .1 && halfway < .9, 'Opening cables must draw progressively through the scroll');
}
const drawingStages = [
  { ids: ['effect-in', 'effect-listen', 'effect-share'], before: .35, during: .405, after: .48 },
  { ids: ['tone-listen', 'trigger', 'voice-share', 'music-in', 'duck-share'], before: .85, during: .915, after: .97 },
];
for (const { ids, before, during, after } of drawingStages) {
  for (const id of ids) {
    const at = progress => storyFrame(progress).wires.find(wire => wire.id === id).draw;
    assert.equal(at(before), 0, `New route must start undrawn: ${id}`);
    assert.ok(at(during) > .05 && at(during) < .95, `New route must visibly draw along its path: ${id}`);
    assert.equal(at(after), 1, `Route must finish before its settled chapter stop: ${id}`);
  }
}
const wireAt = (id, progress) => storyFrame(progress).wires.find(wire => wire.id === id);
const maximumFadeStep = (id, start, end) => {
  let previous = wireAt(id, start).opacity, maximum = 0;
  for (let progress = start + .002; progress < end + .001; progress += .002) {
    const opacity = wireAt(id, progress).opacity;
    assert.ok(opacity <= previous, `Cable exit must fade monotonically: ${id}`);
    maximum = Math.max(maximum, previous - opacity);
    previous = opacity;
  }
  return maximum;
};
const firstExitStep = maximumFadeStep('basic-listen', .20, .24);
for (const id of ['effect-listen', 'effect-share']) {
  assert.equal(wireAt(id, .56).opacity, 1, `Output cable must begin its exit fully visible: ${id}`);
  assert.equal(wireAt(id, .62).opacity, 0, `Output cable must finish fading before relocation: ${id}`);
  const values = [.57, .58, .59, .60, .61].map(progress => wireAt(id, progress).opacity);
  assert.ok(values.every(opacity => opacity > .01 && opacity < .99), `Output exit must have multiple visible intermediate fade levels: ${id}`);
  assert.ok(maximumFadeStep(id, .56, .62) <= firstExitStep, `Second-stage cable exit must be at least as gentle as the first: ${id}`);
  for (const progress of [.56, .58, .60, .62]) {
    assert.equal(wireAt(id, progress).draw, 1, 'Exiting cables must fade without shrinking their reveal mask');
    assert.equal(wireAt('effect-in', progress).opacity, 1, 'The microphone-to-tone cable must remain visible');
  }
}
// Both output nodes follow a single straight diagonal. This catches a return
// to the old separate horizontal and vertical legs, even if their endpoints
// and collision checks still pass.
const moveStart = storyFrame(.62), moveEnd = storyFrame(.80);
for (const progress of [.64, .68, .71, .75, .78]) {
  const frame = storyFrame(progress), fractions = [];
  for (const id of ['listen', 'share']) {
    const start = moveStart.poses[id], end = moveEnd.poses[id], position = frame.poses[id];
    assert.ok(Math.abs(cross(start, end, position)) < 1e-6, `Output must keep a diagonal trajectory: ${id}`);
    fractions.push((position[0] - start[0]) / (end[0] - start[0]));
  }
  assert.ok(Math.abs(fractions[0] - fractions[1]) < 1e-10, 'Outputs must share the same easing and movement duration');
}
const movement = (from, to) => distance(storyFrame(from).poses.listen, storyFrame(to).poses.listen);
assert.ok(movement(.70, .71) > movement(.62, .63) * 4, 'Output motion must accelerate gently from its start');
assert.ok(movement(.70, .71) > movement(.79, .80) * 4, 'Output motion must settle gently at its destination');
let previousChapter = -1;
const previousOpacity = Object.fromEntries(Object.keys(native).map(id => [id, 0]));
const previousDraw = Object.fromEntries(Object.keys(connections).map(id => [id, 0]));
const samples = [];
for (const [progress, count] of [[0, 3], [.55, 4], [1, 6]]) {
  assert.equal(Object.values(storyFrame(progress).poses).filter(pose => pose[2] > visibleThreshold).length, count, 'The route must grow from three to four to six retained nodes');
}
for (let step = 0; step <= 1000; step++) {
  const progress = step / 1000;
  const frame = storyFrame(progress);
  samples.push(JSON.stringify(frame));
  assert.deepEqual(Object.keys(frame.poses).sort(), Object.keys(native).sort(), 'The scene must retain the same native node identities');
  assert.ok(frame.chapter >= previousChapter && frame.chapter >= 0 && frame.chapter <= 2, 'Chapter order must be monotonic');
  previousChapter = frame.chapter;
  for (const gain of Object.values(frame.tones)) assert.ok(Number.isFinite(gain) && gain >= -12 && gain <= 12, 'Tone gains must stay in the real control range');
  const boxes = [];
  for (const [id, [x, y, opacity]] of Object.entries(frame.poses)) {
    assert.ok([x, y, opacity].every(Number.isFinite), `Finite pose required: ${id}`);
    assert.ok(opacity >= 0 && opacity <= 1, `Opacity out of range: ${id}`);
    assert.ok(dimensions[id], `Missing card dimension contract: ${id}`);
    if (['mic', 'listen', 'share'].includes(id)) assert.equal(opacity, 1, `The original node must remain visible: ${id}`);
    assert.ok(opacity >= previousOpacity[id], `Added nodes must remain instead of fading away: ${id}`);
    previousOpacity[id] = opacity;
    if (opacity <= visibleThreshold) continue;
    const [width, height] = dimensions[id];
    const padding = native[id].padding * nodeWidth / native[id].width;
    const box = { id, x: x + padding, y: y + padding, width: width - padding * 2, height: height - padding * 2 };
    boxes.push(box);
    if (x < 0 || y < 0 || x + width > sceneSize.width || y + height > sceneSize.height) issue('Node outside scene', id, progress);
  }
  for (let i = 0; i < boxes.length; i++) for (let j = i + 1; j < boxes.length; j++) {
    const a = boxes[i], b = boxes[j];
    if (Math.min(a.x + a.width, b.x + b.width) - Math.max(a.x, b.x) > .2
      && Math.min(a.y + a.height, b.y + b.height) - Math.max(a.y, b.y) > .2) issue('Visible node overlap', `${a.id}/${b.id}`, progress);
  }
  const visibleWires = [];
  for (const wire of frame.wires) {
    assert.ok(Number.isFinite(wire.opacity) && wire.opacity >= 0 && wire.opacity <= 1, `Wire opacity out of range: ${wire.id}`);
    assert.equal(wire.kind, wire.id === 'trigger' ? 'trigger' : 'audio', 'Only the detector cable is a trigger');
    assert.ok(Number.isFinite(wire.draw) && wire.draw >= previousDraw[wire.id] && wire.draw <= 1, `Every cable must draw continuously from source to destination: ${wire.id}`);
    previousDraw[wire.id] = wire.draw;
    if (wire.opacity <= visibleThreshold) continue;
    const points = samplePath(wire.d);
    visibleWires.push({ id: wire.id, points });
    assert.ok(connections[wire.id], `Unreviewed route semantics: ${wire.id}`);
    const [from, to, inputKind] = connections[wire.id];
    const source = frame.poses[from], target = frame.poses[to];
    if (source[2] <= visibleThreshold || target[2] <= visibleThreshold) issue('Cable joins hidden card', wire.id, progress);
    const sourcePort = native[from].ports.find(port => port.output && port.kind === 'audio');
    const targetPort = native[to].ports.find(port => !port.output && port.kind === (inputKind === 'trigger' ? 'sidechain' : 'audio'));
    const sourcePoint = [source[0] + sourcePort.x * nodeWidth / native[from].width, source[1] + sourcePort.y * nodeWidth / native[from].width];
    const targetPoint = [target[0] + targetPort.x * nodeWidth / native[to].width, target[1] + targetPort.y * nodeWidth / native[to].width];
    if (distance(points[0], sourcePoint) > .1 || distance(points.at(-1), targetPoint) > .1) issue('Detached cable endpoint', wire.id, progress);
    for (const box of boxes) {
      if (points.some(point => within(point, box))) issue('Cable enters card', `${wire.id}/${box.id}`, progress);
    }
    if (points.some(([x, y]) => x < 0 || x > sceneSize.width || y < 0 || y > sceneSize.height)) issue('Cable outside scene', wire.id, progress);
  }
  for (let i = 0; i < visibleWires.length; i++) for (let j = i + 1; j < visibleWires.length; j++) {
    if (pathsCross(visibleWires[i].points, visibleWires[j].points)) issue('Visible cable crossing', `${visibleWires[i].id}/${visibleWires[j].id}`, progress);
  }
}
assert.ok(Object.values(previousDraw).every(draw => draw === 1), 'Every cable must complete its drawing by the final scene');

// Revisit frames in reverse and a different order. The scene must derive solely
// from position, without accumulating animation state as visitors scroll back.
for (let step = 1000; step >= 0; step--) assert.equal(JSON.stringify(storyFrame(step / 1000)), samples[step], 'Reverse scrolling must reproduce the same scene');
for (let index = 0; index <= 1000; index++) {
  const step = (index * 503) % 1001;
  assert.equal(JSON.stringify(storyFrame(step / 1000)), samples[step], 'Rapid scroll jumps must reproduce the same scene');
}

if (issues.size) {
  for (const [description, { first, last, count }] of issues) console.error(`${description}: progress ${first.toFixed(3)}–${last.toFixed(3)} (${count} sampled frames)`);
  throw new Error(`${issues.size} visual geometry invariant(s) failed.`);
}
console.log(`PASS: 1,001 scene positions; ${sampledSegments.toLocaleString('en-US')} sampled cable segments; compact entry/all ten cable draws, synchronized eased diagonals, native geometry/PNG frames, cumulative nodes, card clearance, cable collisions/crossings, sockets, bounds, gains and reverse/rapid scrolling verified.`);
