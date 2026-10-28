// Keep release selection independent of the DOM so prereleases and failures are testable.
export const REPOSITORY = 'https://github.com/acoyeen/vine-mix';
export const FEED = 'https://api.github.com/repos/acoyeen/vine-mix/releases';

function version(tag) {
  if (typeof tag !== 'string') return null;
  const match = /^v?(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?$/.exec(tag);
  if (!match) return null;
  const pre = match[4]?.split('.') || [];
  if (pre.some(part => /^\d+$/.test(part) && part.length > 1 && part[0] === '0')) return null;
  return { core: match.slice(1, 4).map(BigInt), pre };
}
function compare(a, b) {
  for (let i = 0; i < 3; i++) if (a.core[i] !== b.core[i]) return a.core[i] > b.core[i] ? 1 : -1;
  if (!a.pre.length || !b.pre.length) return Number(!a.pre.length) - Number(!b.pre.length);
  for (let i = 0; i < Math.max(a.pre.length, b.pre.length); i++) {
    const x = a.pre[i], y = b.pre[i];
    if (x === y) continue;
    if (x === undefined || y === undefined) return x === undefined ? -1 : 1;
    const xn = /^\d+$/.test(x), yn = /^\d+$/.test(y);
    if (xn && yn) return BigInt(x) > BigInt(y) ? 1 : -1;
    if (xn !== yn) return xn ? -1 : 1;
    return x > y ? 1 : -1;
  }
  return 0;
}
export function selectRelease(releases) {
  if (!Array.isArray(releases)) throw Error('Invalid release response');
  const candidates = releases.filter(r => r && !r.draft && r.published_at && version(r.tag_name));
  candidates.sort((a, b) => compare(version(b.tag_name), version(a.tag_name)));
  const release = candidates[0];
  if (!release) throw Error('No published version found');
  const tag = release.tag_name, number = tag.replace(/^v/, '');
  const base = `${REPOSITORY}/releases/download/${encodeURIComponent(tag)}/`;
  const assets = Array.isArray(release.assets) ? release.assets : [];
  const asset = name => {
    const match = assets.find(a => a && a.name === name && a.state === 'uploaded'
      && Number.isFinite(a.size) && a.size > 0 && a.browser_download_url === base + encodeURIComponent(name));
    return match?.browser_download_url || null;
  };
  const installer = asset(`VineMix-${number}-Setup-x64.exe`);
  const standalone = asset(`VineMix-${number}-Standalone-x64.exe`);
  // Never combine files from different releases or silently call an older release latest.
  if (!installer || !standalone) throw Error('Latest release packages are not ready');
  const url = `${REPOSITORY}/releases/tag/${encodeURIComponent(tag)}`;
  const label = number.replace(/-(alpha|beta|rc)(?:\.(\d+))?$/i, (_, channel, n) => ` · ${channel.toUpperCase() === 'RC' ? 'RC' : channel[0].toUpperCase() + channel.slice(1)}${n === undefined ? '' : ` ${n}`}`);
  return { tag, label, url, installer, standalone,
    checksums: asset('SHA256SUMS.txt') || url,
    docs: `${REPOSITORY}/blob/${encodeURIComponent(tag)}/docs/INSTALLATION_AND_UPDATES.md` };
}

export async function loadRelease(fetcher = fetch, signal) {
  const releases = [];
  // Read pagination rather than assuming the first release or /latest includes betas.
  for (let page = 1; page <= 10; page++) {
    const response = await fetcher(`${FEED}?per_page=100&page=${page}`, {
      signal, credentials: 'omit', headers: { Accept: 'application/vnd.github+json' }
    });
    if (!response.ok) throw Error(`GitHub returned ${response.status}`);
    const items = await response.json();
    if (!Array.isArray(items)) throw Error('Invalid release response');
    releases.push(...items);
    if (!/rel="next"/.test(response.headers.get('link') || '')) return selectRelease(releases);
  }
  throw Error('Release history exceeds discovery limit');
}
