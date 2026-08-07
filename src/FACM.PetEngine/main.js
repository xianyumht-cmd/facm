const { app, BrowserWindow, ipcMain, screen } = require('electron');
const http = require('http');
const fs = require('fs');
const path = require('path');

const HOST = '127.0.0.1';
const PORT = 3100;
const assets = new Map();
const personas = new Map();
const eventClients = new Map();
let petWindow = null;
let activePersonaId = null;
let rendererReady = false;
let modelReadyResolver = null;
let modelReadyRejecter = null;
let dragStart = null;

app.commandLine.appendSwitch('disable-background-timer-throttling');
app.commandLine.appendSwitch('disable-renderer-backgrounding');
app.commandLine.appendSwitch('enable-gpu-rasterization');

function json(res, status, body) {
  const payload = Buffer.from(JSON.stringify(body == null ? {} : body), 'utf8');
  res.writeHead(status, {
    'Content-Type': 'application/json; charset=utf-8',
    'Content-Length': payload.length,
    'Cache-Control': 'no-store'
  });
  res.end(payload);
}

function readJson(req) {
  return new Promise((resolve, reject) => {
    const chunks = [];
    let total = 0;
    req.on('data', chunk => {
      total += chunk.length;
      if (total > 2 * 1024 * 1024) {
        reject(new Error('request too large'));
        req.destroy();
        return;
      }
      chunks.push(chunk);
    });
    req.on('end', () => {
      if (!chunks.length) return resolve({});
      try { resolve(JSON.parse(Buffer.concat(chunks).toString('utf8'))); }
      catch (error) { reject(error); }
    });
    req.on('error', reject);
  });
}

function personaFromUrl(url) {
  const match = /^\/personas\/([^/]+)(?:\/(vrm|spawn|despawn|events))?$/.exec(url.pathname);
  if (!match) return null;
  return { id: decodeURIComponent(match[1]), action: match[2] || '' };
}

function emitPersonaEvent(personaId, eventName) {
  const clients = eventClients.get(personaId);
  if (!clients) return;
  for (const res of [...clients]) {
    try { res.write(`event: ${eventName}\n\ndata: {}\n\n`); }
    catch (_) { clients.delete(res); }
  }
}

function ensurePetWindow() {
  if (petWindow && !petWindow.isDestroyed()) return petWindow;
  const work = screen.getPrimaryDisplay().workArea;
  const width = 420;
  const height = Math.min(720, Math.max(560, work.height - 80));
  petWindow = new BrowserWindow({
    width,
    height,
    x: work.x + work.width - width - 28,
    y: work.y + Math.max(20, Math.floor((work.height - height) / 2)),
    show: false,
    transparent: true,
    frame: false,
    alwaysOnTop: true,
    skipTaskbar: true,
    hasShadow: false,
    resizable: false,
    maximizable: false,
    minimizable: false,
    fullscreenable: false,
    backgroundColor: '#00000000',
    webPreferences: {
      preload: path.join(__dirname, 'preload.js'),
      contextIsolation: true,
      nodeIntegration: false,
      sandbox: false,
      webSecurity: true
    }
  });
  petWindow.setAlwaysOnTop(true, 'screen-saver');
  petWindow.loadFile(path.join(__dirname, 'renderer.html'));
  petWindow.on('closed', () => {
    petWindow = null;
    rendererReady = false;
    activePersonaId = null;
    dragStart = null;
  });
  return petWindow;
}

async function loadPersonaModel(personaId) {
  const persona = personas.get(personaId);
  if (!persona || !persona.assetId) throw new Error('persona has no VRM asset');
  const sourcePath = assets.get(persona.assetId);
  if (!sourcePath || !fs.existsSync(sourcePath)) throw new Error('VRM model file is missing');

  const win = ensurePetWindow();
  if (!rendererReady) {
    await new Promise((resolve, reject) => {
      const timeout = setTimeout(() => reject(new Error('renderer ready timeout')), 12000);
      const poll = setInterval(() => {
        if (rendererReady) {
          clearInterval(poll);
          clearTimeout(timeout);
          resolve();
        }
      }, 50);
      win.once('closed', () => {
        clearInterval(poll);
        clearTimeout(timeout);
        reject(new Error('pet window closed'));
      });
    });
  }

  await new Promise((resolve, reject) => {
    const timeout = setTimeout(() => {
      modelReadyResolver = null;
      modelReadyRejecter = null;
      reject(new Error('VRM model load timeout'));
    }, 15000);
    modelReadyResolver = () => {
      clearTimeout(timeout);
      modelReadyResolver = null;
      modelReadyRejecter = null;
      resolve();
    };
    modelReadyRejecter = error => {
      clearTimeout(timeout);
      modelReadyResolver = null;
      modelReadyRejecter = null;
      reject(error instanceof Error ? error : new Error(String(error || 'VRM load failed')));
    };
    win.webContents.send('facm-load-model', { sourcePath, personaId });
  });
}

async function spawnPersona(personaId) {
  await loadPersonaModel(personaId);
  activePersonaId = personaId;
  const win = ensurePetWindow();
  if (!win.isVisible()) win.showInactive();
  win.setAlwaysOnTop(true, 'screen-saver');
}

function despawnPersona(personaId) {
  if (activePersonaId !== personaId) return;
  activePersonaId = null;
  if (petWindow && !petWindow.isDestroyed()) petWindow.hide();
}

function handleSse(req, res, personaId) {
  res.writeHead(200, {
    'Content-Type': 'text/event-stream; charset=utf-8',
    'Cache-Control': 'no-cache',
    'Connection': 'keep-alive',
    'Access-Control-Allow-Origin': 'http://127.0.0.1'
  });
  res.write(': FACM Pet Engine\n\n');
  let clients = eventClients.get(personaId);
  if (!clients) {
    clients = new Set();
    eventClients.set(personaId, clients);
  }
  clients.add(res);
  const remove = () => clients.delete(res);
  req.on('close', remove);
  req.on('error', remove);
}

const server = http.createServer(async (req, res) => {
  try {
    const url = new URL(req.url, `http://${HOST}:${PORT}`);
    if (req.method === 'GET' && url.pathname === '/personas') {
      return json(res, 200, [...personas.values()]);
    }
    if (req.method === 'POST' && url.pathname === '/assets/import') {
      const body = await readJson(req);
      if (!body.assetId || !body.sourcePath || !fs.existsSync(body.sourcePath))
        return json(res, 400, { error: 'invalid VRM asset' });
      assets.set(String(body.assetId), String(body.sourcePath));
      return json(res, 200, { assetId: String(body.assetId) });
    }
    if (req.method === 'POST' && url.pathname === '/personas') {
      const body = await readJson(req);
      if (!body.id) return json(res, 400, { error: 'persona id is required' });
      const id = String(body.id);
      if (personas.has(id)) return json(res, 409, personas.get(id));
      const persona = { id, name: String(body.name || id), assetId: String(body.vrmAssetId || '') };
      personas.set(id, persona);
      return json(res, 201, persona);
    }

    const route = personaFromUrl(url);
    if (!route) return json(res, 404, { error: 'not found' });
    if (req.method === 'GET' && route.action === 'events') return handleSse(req, res, route.id);
    if (req.method !== 'POST') return json(res, 405, { error: 'method not allowed' });

    if (route.action === 'despawn') {
      despawnPersona(route.id);
      return json(res, 200, { ok: true });
    }
    if (route.action === 'vrm') {
      const body = await readJson(req);
      const persona = personas.get(route.id);
      if (!persona) return json(res, 404, { error: 'persona not found' });
      persona.assetId = String(body.assetId || '');
      personas.set(route.id, persona);
      return json(res, 200, persona);
    }
    if (route.action === 'spawn') {
      if (!personas.has(route.id)) return json(res, 404, { error: 'persona not found' });
      await spawnPersona(route.id);
      return json(res, 200, { ok: true });
    }
    return json(res, 404, { error: 'not found' });
  } catch (error) {
    console.error('[FACM Pet Engine API]', error);
    if (!res.headersSent) json(res, 500, { error: String(error && error.message ? error.message : error) });
    else res.end();
  }
});

ipcMain.handle('facm-read-model', async (_, sourcePath) => {
  const resolved = path.resolve(String(sourcePath || ''));
  const data = await fs.promises.readFile(resolved);
  return data;
});

ipcMain.on('facm-renderer-ready', () => { rendererReady = true; });
ipcMain.on('facm-model-ready', () => {
  if (modelReadyResolver) modelReadyResolver();
});
ipcMain.on('facm-model-error', (_, message) => {
  if (modelReadyRejecter) modelReadyRejecter(new Error(String(message || 'VRM load failed')));
});
ipcMain.on('facm-pet-click', () => {
  if (activePersonaId) emitPersonaEvent(activePersonaId, 'pointer-click');
});
ipcMain.on('facm-drag-start', () => {
  if (!petWindow || petWindow.isDestroyed()) return;
  const cursor = screen.getCursorScreenPoint();
  const bounds = petWindow.getBounds();
  dragStart = { cursor, x: bounds.x, y: bounds.y };
});
ipcMain.on('facm-drag-move', (_, delta) => {
  if (!petWindow || petWindow.isDestroyed() || !dragStart) return;
  const dx = Number(delta && delta.dx || 0);
  const dy = Number(delta && delta.dy || 0);
  petWindow.setPosition(Math.round(dragStart.x + dx), Math.round(dragStart.y + dy), false);
});
ipcMain.on('facm-drag-end', () => { dragStart = null; });

app.whenReady().then(() => {
  server.listen(PORT, HOST, () => console.log(`[FACM Pet Engine] API ready at http://${HOST}:${PORT}/`));
});

app.on('window-all-closed', event => {
  // Keep the local engine alive while FACM is using the API.
});

app.on('before-quit', () => {
  try { server.close(); } catch (_) {}
});
