const fs = require('node:fs');
const path = require('node:path');
const https = require('node:https');

function option(name, required = true) {
  const index = process.argv.indexOf(name);
  if (index < 0) {
    if (required) throw new Error(`missing ${name}`);
    return '';
  }
  if (index + 1 >= process.argv.length) throw new Error(`missing value for ${name}`);
  return process.argv[index + 1];
}

const root = path.resolve(option('--root'));
const port = Number(option('--port'));
const keyPath = path.resolve(option('--key'));
const certPath = path.resolve(option('--cert'));
const readyPath = path.resolve(option('--ready'));
const requestLogPath = path.resolve(option('--request-log', false) || path.join(path.dirname(readyPath), 'requests.jsonl'));
const mode = option('--mode', false) || 'normal';
const delayMs = Number(option('--delay-ms', false) || 0);
const redirectLocation = option('--redirect-location', false) || 'http://127.0.0.1/blocked';

if (!Number.isInteger(port) || port < 1 || port > 65535) throw new Error('invalid port');
if (!fs.statSync(root).isDirectory()) throw new Error('origin root is not a directory');

function logRequest(record) {
  fs.appendFileSync(requestLogPath, `${JSON.stringify(record)}\n`, 'utf8');
}

function safeFile(pathname) {
  const relative = pathname.replace(/^\/+/, '');
  const candidate = path.resolve(root, ...relative.split('/'));
  if (candidate !== root && !candidate.startsWith(`${root}${path.sep}`)) return null;
  return candidate;
}

function sendFile(req, res) {
  const started = Date.now();
  let pathname = '';
  try {
    pathname = decodeURIComponent(new URL(req.url, 'https://localhost').pathname);
  } catch {
    res.writeHead(400); res.end(); return;
  }
  const file = safeFile(pathname);
  const relative = file ? path.relative(root, file).replaceAll(path.sep, '/') : '';
  const baseRecord = { method: req.method, path: `/${relative}`, mode, range: req.headers.range || null };
  if (mode === 'unavailable') {
    res.writeHead(503, { 'Content-Type': 'text/plain', 'Content-Length': '0' });
    res.end(); logRequest({ ...baseRecord, status: 503, bytes: 0, durationMs: Date.now() - started }); return;
  }
  if (mode === 'redirect') {
    res.writeHead(302, { Location: redirectLocation, 'Content-Length': '0' });
    res.end(); logRequest({ ...baseRecord, status: 302, bytes: 0, durationMs: Date.now() - started }); return;
  }
  if (!file || !fs.existsSync(file) || !fs.statSync(file).isFile()) {
    res.writeHead(404, { 'Content-Length': '0' });
    res.end(); logRequest({ ...baseRecord, status: 404, bytes: 0, durationMs: Date.now() - started }); return;
  }
  let bytes = fs.readFileSync(file);
  if (mode === 'corrupt-package' && pathname.toLowerCase().endsWith('.cab')) {
    bytes = Buffer.from(bytes); if (bytes.length) bytes[0] ^= 0xff;
  }
  if (mode === 'truncate-package' && pathname.toLowerCase().endsWith('.cab')) {
    bytes = bytes.subarray(0, Math.max(1, Math.floor(bytes.length / 2)));
  }
  let start = 0;
  let end = bytes.length - 1;
  let status = 200;
  const range = req.headers.range;
  if (range) {
    const match = /^bytes=(\d+)-$/.exec(range);
    if (match) {
      start = Number(match[1]);
      if (start >= bytes.length) {
        res.writeHead(416, { 'Content-Length': '0' }); res.end();
        logRequest({ ...baseRecord, status: 416, bytes: 0, durationMs: Date.now() - started }); return;
      }
      status = 206;
    }
  }
  const payload = bytes.subarray(start, end + 1);
  const headers = {
    'Content-Type': pathname.endsWith('.json') || pathname.endsWith('.sig') ? 'application/octet-stream' : 'application/octet-stream',
    'Content-Length': payload.length,
    'Accept-Ranges': 'bytes',
    'Cache-Control': 'no-store'
  };
  if (status === 206) headers['Content-Range'] = `bytes ${start}-${end}/${bytes.length}`;
  const finish = () => {
    if (delayMs > 0) setTimeout(() => res.end(payload), delayMs);
    else res.end(payload);
    logRequest({ ...baseRecord, status, bytes: payload.length, durationMs: Date.now() - started });
  };
  res.writeHead(status, headers);
  if (req.method === 'HEAD') { res.end(); return; }
  finish();
}

const server = https.createServer({ key: fs.readFileSync(keyPath), cert: fs.readFileSync(certPath) }, sendFile);
server.on('error', (error) => { console.error(error.message); process.exitCode = 1; });
server.listen(port, '127.0.0.1', () => {
  fs.writeFileSync(readyPath, JSON.stringify({ protocol: 'https', port, pid: process.pid, mode }) + '\n', 'utf8');
});

function stop() { server.close(() => process.exit(0)); }
process.on('SIGTERM', stop);
process.on('SIGINT', stop);
