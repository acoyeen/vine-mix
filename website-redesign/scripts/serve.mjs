import http from 'node:http';
import { readFile, realpath, stat } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import path from 'node:path';
const root = await realpath(fileURLToPath(new URL('../dist/', import.meta.url)));
const mime = {
  '.html': 'text/html; charset=utf-8', '.css': 'text/css; charset=utf-8',
  '.js': 'text/javascript; charset=utf-8', '.json': 'application/json; charset=utf-8',
  '.svg': 'image/svg+xml', '.png': 'image/png', '.jpg': 'image/jpeg',
  '.ttf': 'font/ttf', '.woff2': 'font/woff2', '.txt': 'text/plain; charset=utf-8',
};
let port = Number(process.env.VINEMIX_SITE_PORT || 4175);
if (!Number.isInteger(port) || port < 1 || port > 65535) {
  throw new Error('VINEMIX_SITE_PORT must be an integer between 1 and 65535.');
}
const lastPort = Math.min(port + 100, 65535);
const isInsideRoot = target => {
  const relative = path.relative(root, target);
  return relative !== '' && relative !== '..' && !relative.startsWith(`..${path.sep}`) && !path.isAbsolute(relative);
};
const server = http.createServer(async (req, res) => {
  if (req.method !== 'GET' && req.method !== 'HEAD') {
    res.writeHead(405, { Allow: 'GET, HEAD', 'Content-Type':'text/plain; charset=utf-8' });
    res.end('Method not allowed');
    return;
  }
  try {
    const pathname = decodeURIComponent(new URL(req.url, 'http://127.0.0.1').pathname);
    const relative = pathname === '/' ? 'index.html' : pathname.slice(1);
    const target = path.resolve(root, relative);
    if (!isInsideRoot(target) || relative.includes('\\')) throw new Error('Not found');
    const physicalTarget = await realpath(target);
    const info = await stat(physicalTarget);
    if (!isInsideRoot(physicalTarget) || !info.isFile()) throw new Error('Not found');
    const content = req.method === 'HEAD' ? null : await readFile(physicalTarget);
    res.writeHead(200, { 'Content-Type': mime[path.extname(target).toLowerCase()] || 'application/octet-stream', 'Content-Length': content?.length ?? info.size, 'Cache-Control':'no-cache', 'X-Content-Type-Options':'nosniff', 'Referrer-Policy':'strict-origin-when-cross-origin' });
    res.end(content);
  } catch {
    res.writeHead(404, { 'Content-Type':'text/plain; charset=utf-8' });
    res.end(req.method === 'HEAD' ? undefined : 'Not found');
  }
});
server.on('error', error => {
  if (error.code === 'EADDRINUSE' && port < lastPort) { port++; server.listen(port, '127.0.0.1'); }
  else { console.error(error.message); process.exitCode = 1; }
});
server.listen(port, '127.0.0.1', () => console.log(`VineMix preview: http://127.0.0.1:${port}/`));
