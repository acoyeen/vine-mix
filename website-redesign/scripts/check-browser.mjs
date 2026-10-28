// Optional browser verification: use an existing Playwright installation.
// No browser package or executable is downloaded by this check.
import assert from 'node:assert/strict';
import { pathToFileURL } from 'node:url';
import path from 'node:path';

const { chromium } = await import(process.env.VINEMIX_PLAYWRIGHT_PATH
  ? pathToFileURL(path.resolve(process.env.VINEMIX_PLAYWRIGHT_PATH)).href : 'playwright');
const base = new URL(process.env.VINEMIX_SITE_URL || 'http://127.0.0.1:4175/');
assert(['127.0.0.1', 'localhost', '[::1]'].includes(base.hostname), 'Use a local preview');
const browser = await chromium.launch({ headless: true,
  channel: process.env.VINEMIX_BROWSER_CHANNEL || (process.platform === 'win32' ? 'msedge' : undefined) });
let scenarios = 0;

async function scenario(options, run, prepare) {
  const context = await browser.newContext({ viewport: { width: 1440, height: 900 }, ...options });
  try {
    // Deterministic offline discovery: do not depend on a live release or rate limit.
    await context.route('https://api.github.com/**', route => route.fulfill({ status: 503, body: 'Unavailable' }));
    const page = await context.newPage();
    const errors = [], failedLocalRequests = [];
    page.on('pageerror', error => errors.push(error.message));
    page.on('response', response => {
      if (response.url().startsWith(base.origin) && response.status() >= 400)
        failedLocalRequests.push(new URL(response.url()).pathname);
    });
    await prepare?.(page);
    await page.goto(base.href, { waitUntil: 'networkidle' });
    await run(page);
    assert.deepEqual(errors, [], 'No unhandled browser errors');
    assert.deepEqual(failedLocalRequests, [], 'No broken local resources');
    scenarios++;
  } finally { await context.close(); }
}

async function noOverflow(page) {
  const geometry = await page.evaluate(() => ({ width: innerWidth, content: document.documentElement.scrollWidth,
    outside: [...document.querySelectorAll('body *')].filter(node => {
      const rect = node.getBoundingClientRect();
      return rect.width > 0 && rect.right > innerWidth + 1;
    }).slice(0, 8).map(node => node.tagName + '.' + node.className) }));
  assert(geometry.content <= geometry.width, `No horizontal overflow: ${JSON.stringify(geometry)}`);
}

async function localLinks(page) {
  const ids = await page.locator('[id]').evaluateAll(nodes => nodes.map(node => node.id));
  assert.equal(new Set(ids).size, ids.length, 'Page IDs must be unique');
  const links = await page.locator('a[href]').evaluateAll(nodes => nodes.map(node => node.getAttribute('href')));
  for (const href of new Set(links)) {
    if (href.startsWith('#')) assert(ids.includes(decodeURIComponent(href.slice(1))), `Anchor target ${href} exists`);
    else {
      const url = new URL(href, page.url());
      if (url.origin === base.origin) assert.equal((await page.request.head(url.href)).status(), 200, `Local link ${href} resolves`);
    }
  }
}

try {
  await scenario({}, async page => {
    await page.waitForFunction(() => document.querySelector('#sound').classList.contains('is-enhanced'));
    await page.waitForFunction(() => document.querySelector('vine-mix-pipeline').shadowRoot.querySelectorAll('.node').length === 7);
    await localLinks(page);
    assert.equal(await page.locator('.vm-faq summary').count(), 8);
    assert.equal(await page.locator('[data-release-state]').textContent(), '(Saved release)');
    for (const kind of ['installer', 'standalone'])
      assert.match(await page.locator(`[data-download="${kind}"]`).getAttribute('href'), /^https:\/\/github\.com\/acoyeen\/vine-mix\/releases\/download\//);

    // The first Tab exposes the skip link; activation moves focus into the product.
    await page.keyboard.press('Tab');
    assert.equal(await page.evaluate(() => document.activeElement.className), 'skip-link');
    await page.keyboard.press('Enter');
    await page.waitForFunction(() => document.activeElement.id === 'sound');
    await page.locator('.story-skip').click();
    await page.waitForFunction(() => document.activeElement.id === 'audio-pipeline');
    await page.locator('.site-brand').click();
    await page.waitForFunction(() => document.activeElement.id === 'top' && scrollY < 1);
    await page.locator('.arrival-actions a').click();
    await page.waitForFunction(() => document.activeElement.id === 'download');
    assert.equal(page.url(), base.href, 'Section shortcuts must keep the original URL');
    await page.locator('.site-brand').click();
    await page.waitForFunction(() => document.activeElement.id === 'top' && scrollY < 1);
    assert.equal(page.url(), base.href, 'Return-to-top must keep the original URL');

    const questions = page.locator('.vm-faq summary');
    await questions.nth(0).focus();
    await page.keyboard.press('Enter');
    assert.equal(await page.locator('.vm-faq details[open]').count(), 1);
    await questions.nth(2).click();
    assert.equal(await page.locator('.vm-faq details[open]').count(), 1);
    assert.equal(await page.locator('.vm-faq details').nth(0).getAttribute('open'), null);
    await questions.nth(2).press('Space');
    assert.equal(await page.locator('.vm-faq details[open]').count(), 0);

    for (const [width, height] of [[320, 844], [390, 844], [768, 1024], [1280, 900], [1440, 900], [1440, 500]]) {
      await page.setViewportSize({ width, height });
      await page.waitForTimeout(150);
      await noOverflow(page);
      await questions.nth(2).click();
      await page.waitForTimeout(300);
      await noOverflow(page);
      await questions.nth(2).click();
    }
    assert.deepEqual(await page.evaluate(() => [...document.images]
      .filter(image => image.complete && !image.naturalWidth).map(image => image.getAttribute('src'))), []);
    await page.goto(new URL('credits.html', base).href, { waitUntil: 'networkidle' });
    assert.equal(await page.locator('h1').textContent(), 'Credits and licenses.');
    await localLinks(page);
    await noOverflow(page);
    await page.getByRole('link', { name: 'Back to VineMix' }).click();
    await page.waitForSelector('#download');
  });

  for (const options of [{ reducedMotion: 'reduce' }, { forcedColors: 'active' }]) {
    await scenario(options, async page => {
      assert.equal(await page.locator('#sound').evaluate(node => node.classList.contains('is-enhanced')), false);
      assert.equal(await page.locator('#background-canvas').getAttribute('data-motion'), 'paused');
      for (const route of await page.locator('.story-fallback').all()) assert(await route.isVisible());
      await page.locator('.arrival-actions a').click();
      await page.waitForFunction(() => document.activeElement.id === 'download');
      await noOverflow(page);
    });
  }

  await scenario({ javaScriptEnabled: false, viewport: { width: 320, height: 844 } }, async page => {
    assert(await page.locator('.pipeline-fallback').isVisible());
    for (const route of await page.locator('.story-fallback').all()) assert(await route.isVisible());
    await page.locator('.arrival-actions a').click();
    assert.equal(new URL(page.url()).hash, '#download');
    await page.locator('.vm-faq summary').nth(0).click();
    assert.equal(await page.locator('.vm-faq details[open]').count(), 1);
    await noOverflow(page);
  });

  await scenario({}, async page => {
    assert.equal(await page.locator('#background-canvas').getAttribute('data-motion'), 'unavailable');
    assert(await page.locator('.atmosphere').evaluate(node => node.classList.contains('shader-failed')));
    await page.locator('.arrival-actions a').click();
    await page.waitForFunction(() => document.activeElement.id === 'download');
  }, page => page.addInitScript(() => {
    const original = HTMLCanvasElement.prototype.getContext;
    HTMLCanvasElement.prototype.getContext = function (kind, ...args) {
      return /webgl/.test(kind) ? null : original.call(this, kind, ...args);
    };
  }));

  await scenario({}, async page => {
    await page.waitForFunction(() => document.querySelector('vine-mix-pipeline').shadowRoot.textContent.includes('Node artwork could not load'));
    assert(await page.locator('vine-mix-pipeline').getByText('Physical input → High-pass filter → Denoiser → Expander → Compressor → Equaliser → Virtual output').count());
    await page.locator('.arrival-actions a').click();
    await page.waitForFunction(() => document.activeElement.id === 'download');
  }, page => page.route('**/assets/pipeline/nodes/nodes.json', route => route.fulfill({ status: 200, contentType: 'application/json', body: 'invalid JSON' })));

  let discoveryFails = false;
  const repository = 'https://github.com/acoyeen/vine-mix';
  const version = '1.2.3-beta.4';
  await scenario({}, async page => {
    assert.equal(await page.locator('[data-release-state]').textContent(), '(Latest Release)');
    assert.equal(await page.locator('[data-release-version]').textContent(), '1.2.3 · Beta 4');
    for (const selector of ['[data-release-link]', '[data-download="installer"]', '[data-download="standalone"]', '[data-release-notes]', '[data-checksums]', '[data-installation]'])
      assert((await page.locator(selector).getAttribute('href')).includes(`/v${version}/`) || (await page.locator(selector).getAttribute('href')).endsWith(`/v${version}`));
    const before = await page.locator('.vm-downloads a').evaluateAll(nodes => nodes.map(node => node.href));
    discoveryFails = true;
    await page.evaluate(() => window.dispatchEvent(new Event('online')));
    await page.waitForFunction(() => document.querySelector('[data-release-state]').textContent === '(Last checked release)');
    assert.deepEqual(await page.locator('.vm-downloads a').evaluateAll(nodes => nodes.map(node => node.href)), before);
  }, page => page.route('https://api.github.com/**', route => route.fulfill(discoveryFails
    ? { status: 503, body: 'Unavailable' }
    : { status: 200, contentType: 'application/json', body: JSON.stringify([{ tag_name: `v${version}`, draft: false,
      published_at: '2026-10-27T00:00:00Z', assets: [`VineMix-${version}-Setup-x64.exe`, `VineMix-${version}-Standalone-x64.exe`, 'SHA256SUMS.txt']
        .map(name => ({ name, size: 100, state: 'uploaded', browser_download_url: `${repository}/releases/download/v${version}/${name}` })) }]) })));

  console.log(`Browser checks passed: ${scenarios} scenarios; navigation/focus/history, FAQ mouse/keyboard, six viewport sizes, local links/credits, successful and offline downloads, reduced motion, high contrast, no JavaScript, unavailable WebGL and failed artwork.`);
} finally { await browser.close(); }
