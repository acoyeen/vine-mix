import { readFile, writeFile, readdir, mkdir, rename, unlink } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import path from 'node:path';
const root = fileURLToPath(new URL('../', import.meta.url));
const release = JSON.parse(await readFile(path.join(root, 'release.json'), 'utf8'));
const values = {
  ...release,
  releaseUrl: `${release.repository}/releases/tag/v${release.version}`,
  docsBase: `${release.repository}/blob/v${release.version}/docs`,
  sourceUrl: `${release.repository}/tree/v${release.version}`
};
const source = path.join(root, 'src');
const output = path.join(root, 'dist');
// Render the same pipeline shell the controller adopts, including its CSS, so
// neither custom-element registration nor stylesheet loading changes its size.
if ((await readFile(path.join(source, 'index.html'), 'utf8')).includes('{{pipelineShell}}')) {
  const { pipelineShell } = await import(new URL('../src/pipeline-shell.js', import.meta.url));
  const styles = await readFile(path.join(source, 'pipeline.css'), 'utf8');
  values.pipelineShell = pipelineShell(`<style>${styles}</style>`);
}
const names = new Set((await readdir(source, { withFileTypes: true }))
  .filter(entry => entry.isFile()).map(entry => entry.name));
if (!names.has('index.html')) throw new Error('The site source must contain index.html.');

// Resolve every template before changing public output, so a bad release value
// cannot leave a partially rendered page. Assets are maintained separately.
const files = await Promise.all([...names].map(async name => {
  const sourceText = await readFile(path.join(source, name), 'utf8');
  const content = sourceText.replace(/\{\{(\w+)\}\}/g, (_, key) => {
    if (!Object.hasOwn(values, key)) throw new Error(`Unknown template value: ${key}`);
    return values[key];
  });
  return { name, content };
}));
await mkdir(output, { recursive: true });
for (const { name, content } of files) {
  const target = path.join(output, name);
  const previous = await readFile(target, 'utf8').catch(error => {
    if (error.code !== 'ENOENT') throw error;
    return null;
  });
  if (previous === content) continue;

  // Keep the preview from reading a partially written file, and leave unchanged
  // outputs untouched so repeated builds do not create needless disk activity.
  const temporary = `${target}.${process.pid}.tmp`;
  try {
    await writeFile(temporary, content);
    await rename(temporary, target);
  } finally {
    await unlink(temporary).catch(error => {
      if (error.code !== 'ENOENT') throw error;
    });
  }
}
// Retired source modules must not remain in the public package. Only generated
// top-level HTML/CSS/JS is managed here; the assets directory is never removed.
const generatedExtensions = new Set(['.html', '.css', '.js']);
for (const entry of await readdir(output, { withFileTypes: true })) {
  if (entry.isFile() && generatedExtensions.has(path.extname(entry.name)) && !names.has(entry.name)) {
    await unlink(path.join(output, entry.name));
  }
}
console.log(`Built VineMix ${release.version} website.`);
