import assert from 'node:assert/strict';
import { execFile } from 'node:child_process';
import { copyFile, mkdir, mkdtemp, readFile, realpath, rm, stat, utimes, writeFile } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { promisify } from 'node:util';

const execute = promisify(execFile);
const temporaryRoot = await realpath(os.tmpdir());
const fixture = await mkdtemp(path.join(temporaryRoot, 'vinemix-build-check-'));
const output = path.join(fixture, 'dist');
const source = path.join(fixture, 'src');
const run = () => execute(process.execPath, [path.join(fixture, 'scripts/build.mjs')], { windowsHide: true, timeout: 10000 });
const read = name => readFile(path.join(output, name), 'utf8');

try {
  await mkdir(path.join(fixture, 'scripts'));
  await mkdir(source);
  await mkdir(path.join(output, 'assets'), { recursive: true });
  await copyFile(new URL('./build.mjs', import.meta.url), path.join(fixture, 'scripts/build.mjs'));
  await writeFile(path.join(fixture, 'release.json'), JSON.stringify({
    version: '1.2.3-beta.4', displayVersion: '1.2.3 · Beta 4', repository: 'https://example.test/vine-mix',
  }));
  await writeFile(path.join(source, 'index.html'), '<title>{{displayVersion}}</title><a href="{{releaseUrl}}">Release</a>');
  await writeFile(path.join(source, 'module.js'), 'export const release = "{{version}}";');
  await writeFile(path.join(output, 'retired.js'), 'retired');
  await writeFile(path.join(output, 'notes.txt'), 'unmanaged public file');
  await writeFile(path.join(output, 'assets/license.txt'), 'retained asset notice');

  await run();
  const index = await read('index.html');
  assert.equal(index, '<title>1.2.3 · Beta 4</title><a href="https://example.test/vine-mix/releases/tag/v1.2.3-beta.4">Release</a>');
  assert.equal(await read('module.js'), 'export const release = "1.2.3-beta.4";');
  await assert.rejects(stat(path.join(output, 'retired.js')), { code: 'ENOENT' });
  assert.equal(await read('assets/license.txt'), 'retained asset notice');
  assert.equal(await read('notes.txt'), 'unmanaged public file');

  // Explicit old timestamps avoid sleeps and expose rewrites on coarse clocks.
  const old = new Date('2000-01-01T00:00:00Z');
  await utimes(path.join(output, 'index.html'), old, old);
  await utimes(path.join(output, 'module.js'), old, old);
  await run();
  assert.equal((await stat(path.join(output, 'index.html'))).mtimeMs, old.getTime());
  assert.equal((await stat(path.join(output, 'module.js'))).mtimeMs, old.getTime());

  // Resolve all templates before changing any output, including when a failing
  // template sorts after a valid changed file or names an inherited property.
  await writeFile(path.join(source, 'index.html'), '<title>Updated {{displayVersion}}</title>');
  for (const key of ['missingTemplateValue', 'constructor', '__proto__']) {
    await writeFile(path.join(source, 'z-invalid.html'), `{{${key}}}`);
    await assert.rejects(run(), error => error.stderr.includes(`Unknown template value: ${key}`));
    assert.equal(await read('index.html'), index, 'Failed template validation must preserve previous output');
    await assert.rejects(stat(path.join(output, 'z-invalid.html')), { code: 'ENOENT' });
    assert.equal(await read('assets/license.txt'), 'retained asset notice');
  }
  await rm(path.join(source, 'z-invalid.html'));
  await run();
  assert.equal(await read('index.html'), '<title>Updated 1.2.3 · Beta 4</title>');
  assert.equal((await stat(path.join(output, 'module.js'))).mtimeMs, old.getTime());
  console.log('PASS: build substitution, unchanged-file timestamps, failed-template preservation, inherited-key rejection, asset retention and retired-file cleanup.');
} finally {
  const resolvedFixture = await realpath(fixture);
  assert.equal(path.dirname(resolvedFixture), temporaryRoot, 'Cleanup must remain within the temporary directory');
  assert.ok(path.basename(resolvedFixture).startsWith('vinemix-build-check-'));
  await rm(resolvedFixture, { recursive: true, force: true });
}
