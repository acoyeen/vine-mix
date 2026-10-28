import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { once } from 'node:events';
import { copyFile, mkdir, mkdtemp, realpath, rm, symlink, writeFile } from 'node:fs/promises';
import http from 'node:http';
import os from 'node:os';
import path from 'node:path';

// Exercise the real server against isolated public/private files. No listener or
// public file from the maintainer's preview is changed by this check.
const temporaryRoot = await realpath(os.tmpdir());
const fixture = await mkdtemp(path.join(temporaryRoot, 'vinemix-server-check-'));
let child;
let blocker;

function request(port, pathname, method = 'GET') {
  return new Promise((resolve, reject) => {
    const req = http.request({ hostname: '127.0.0.1', port, path: pathname, method }, res => {
      const chunks = [];
      res.on('data', chunk => chunks.push(chunk));
      res.on('end', () => resolve({ status: res.statusCode, headers: res.headers, body: Buffer.concat(chunks) }));
      res.on('error', reject);
    });
    req.setTimeout(5000, () => req.destroy(new Error('Preview request timed out')));
    req.on('error', reject);
    req.end();
  });
}

async function start(port) {
  child = spawn(process.execPath, [path.join(fixture, 'scripts/serve.mjs')], {
    env: { ...process.env, VINEMIX_SITE_PORT: String(port) }, windowsHide: true,
    stdio: ['ignore', 'pipe', 'pipe'],
  });
  return new Promise((resolve, reject) => {
    let output = '';
    const timeout = setTimeout(() => reject(new Error('Preview did not start')), 10000);
    const finish = (error, actualPort) => {
      clearTimeout(timeout);
      if (error) reject(error); else resolve(actualPort);
    };
    child.on('error', error => finish(error));
    child.on('exit', code => finish(new Error(`Preview exited before readiness (${code})`)));
    child.stdout.on('data', chunk => {
      output += chunk;
      const match = /VineMix preview: http:\/\/127\.0\.0\.1:(\d+)\//.exec(output);
      if (match) finish(null, Number(match[1]));
    });
  });
}

try {
  await mkdir(path.join(fixture, 'scripts'));
  await mkdir(path.join(fixture, 'dist/assets'), { recursive: true });
  await mkdir(path.join(fixture, 'private'));
  await copyFile(new URL('./serve.mjs', import.meta.url), path.join(fixture, 'scripts/serve.mjs'));
  await writeFile(path.join(fixture, 'dist/index.html'), '<!doctype html><title>VineMix test</title>');
  await writeFile(path.join(fixture, 'dist/assets/font.woff2'), Buffer.from([0x77, 0x4f, 0x46, 0x32]));
  await writeFile(path.join(fixture, 'dist/assets/nodes.json'), '{"example":true}');
  await writeFile(path.join(fixture, 'private/evidence.txt'), 'private fixture must never be served');
  await symlink(path.join(fixture, 'private'), path.join(fixture, 'dist/escape'), process.platform === 'win32' ? 'junction' : 'dir');

  blocker = http.createServer((req, res) => res.end('existing listener'));
  blocker.listen(0, '127.0.0.1');
  await once(blocker, 'listening');
  const occupiedPort = blocker.address().port;
  const port = await start(occupiedPort);
  assert.ok(port > occupiedPort && port <= Math.min(65535, occupiedPort + 100), 'An occupied port must select the next available port');
  assert.equal((await request(occupiedPort, '/')).body.toString(), 'existing listener', 'Port fallback must preserve the existing listener');

  const page = await request(port, '/');
  assert.equal(page.status, 200);
  assert.match(page.headers['content-type'], /^text\/html;/);
  assert.equal(page.headers['x-content-type-options'], 'nosniff');
  assert.equal(page.headers['cache-control'], 'no-cache');
  assert.equal(page.headers['referrer-policy'], 'strict-origin-when-cross-origin');
  assert.equal(Number(page.headers['content-length']), page.body.length);
  const head = await request(port, '/', 'HEAD');
  assert.equal(head.status, 200);
  assert.equal(head.body.length, 0);
  assert.equal(head.headers['content-length'], page.headers['content-length']);
  assert.equal((await request(port, '/assets/font.woff2')).headers['content-type'], 'font/woff2');
  const json = await request(port, '/assets/nodes.json?cache=example');
  assert.equal(json.headers['content-type'], 'application/json; charset=utf-8');
  assert.deepEqual(JSON.parse(json.body), { example: true });

  for (const pathname of ['/missing', '/assets/', '/%E0%A4%A', '/%2e%2e%2fprivate/evidence.txt',
    '/%2e%2e%5cprivate%5cevidence.txt', '/escape/evidence.txt']) {
    const missing = await request(port, pathname);
    assert.equal(missing.status, 404, `${pathname} must not expose a file`);
    assert.equal(missing.body.toString(), 'Not found');
    const missingHead = await request(port, pathname, 'HEAD');
    assert.equal(missingHead.status, 404);
    assert.equal(missingHead.body.length, 0);
  }
  for (const method of ['POST', 'PUT', 'DELETE']) {
    const result = await request(port, '/', method);
    assert.equal(result.status, 405);
    assert.equal(result.headers.allow, 'GET, HEAD');
  }
  console.log('PASS: preview GET/HEAD, MIME types, errors, malformed URLs, traversal, physical-path containment, methods and non-destructive port fallback.');
} finally {
  if (child && child.exitCode === null) {
    const stopped = once(child, 'exit');
    child.kill();
    await stopped;
  }
  if (blocker?.listening) await new Promise((resolve, reject) => blocker.close(error => error ? reject(error) : resolve()));
  const resolvedFixture = await realpath(fixture);
  assert.equal(path.dirname(resolvedFixture), temporaryRoot, 'Cleanup must remain within the temporary directory');
  assert.ok(path.basename(resolvedFixture).startsWith('vinemix-server-check-'));
  await rm(resolvedFixture, { recursive: true, force: true });
}
