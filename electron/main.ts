import { app, BrowserWindow, ipcMain, screen, Tray, Menu, nativeImage } from 'electron';
import path from 'path';
import http from 'http';
import { systemMonitor } from './ipc/system-monitor';
import { openCodeBridge } from './ipc/opencode';

let mainWindow: BrowserWindow | null = null;
let tray: Tray | null = null;

// Архитектура по референсу DynamicIslandForWindows:
// Окно имеет ТОЧНЫЙ размер самой капсулы, без гигантских прозрачных оверлеев на пол-экрана!
const COMPACT_WIDTH = 320;
const COMPACT_HEIGHT = 44;
const EXPANDED_WIDTH = 560;
const EXPANDED_HEIGHT = 380;
const TOP_OFFSET = 8;

function checkUrlLive(urlStr: string): Promise<boolean> {
  return new Promise((resolve) => {
    try {
      const u = new URL(urlStr);
      const req = http.request(
        {
          hostname: u.hostname,
          port: u.port,
          path: '/',
          method: 'GET',
          timeout: 600
        },
        () => resolve(true)
      );
      req.on('error', () => resolve(false));
      req.on('timeout', () => {
        req.destroy();
        resolve(false);
      });
      req.end();
    } catch {
      resolve(false);
    }
  });
}

async function createWindow() {
  const primaryDisplay = screen.getPrimaryDisplay();
  const { x: displayX, y: displayY, width: displayWidth } = primaryDisplay.bounds;

  const posX = displayX + Math.round((displayWidth - COMPACT_WIDTH) / 2);
  const posY = displayY + TOP_OFFSET;

  console.log(`[Desktop Engineer] Spawning Dynamic Island Window at (${posX}, ${posY}) size ${COMPACT_WIDTH}x${COMPACT_HEIGHT}`);

  mainWindow = new BrowserWindow({
    width: COMPACT_WIDTH,
    height: COMPACT_HEIGHT,
    x: posX,
    y: posY,
    transparent: true,
    backgroundColor: '#00000000',
    frame: false,
    alwaysOnTop: true,
    resizable: false,
    hasShadow: true,
    skipTaskbar: true,
    webPreferences: {
      preload: path.join(__dirname, 'preload.js'),
      contextIsolation: true,
      nodeIntegration: false
    }
  });

  mainWindow.setAlwaysOnTop(true, 'screen-saver');

  systemMonitor.setWindow(mainWindow);
  systemMonitor.start();

  openCodeBridge.setWindow(mainWindow);
  openCodeBridge.start();

  // Ресайз окна по команде из Renderer (компактный <-> развернутый)
  ipcMain.on('island:resize', (_event, mode: 'compact' | 'expanded') => {
    if (!mainWindow || mainWindow.isDestroyed()) return;

    const targetWidth = mode === 'expanded' ? EXPANDED_WIDTH : COMPACT_WIDTH;
    const targetHeight = mode === 'expanded' ? EXPANDED_HEIGHT : COMPACT_HEIGHT;
    const targetX = displayX + Math.round((displayWidth - targetWidth) / 2);

    mainWindow.setBounds({
      x: targetX,
      y: posY,
      width: targetWidth,
      height: targetHeight
    }, true);
  });

  const isViteReady = await checkUrlLive('http://localhost:5173');
  if (isViteReady) {
    console.log('[OpenIsland] Loading Vite HMR: http://localhost:5173');
    await mainWindow.loadURL('http://localhost:5173');
  } else {
    const distPath = path.join(__dirname, '../dist/index.html');
    console.log('[OpenIsland] Loading local build:', distPath);
    await mainWindow.loadFile(distPath);
  }

  mainWindow.show();
  mainWindow.focus();

  mainWindow.on('closed', () => {
    systemMonitor.stop();
    openCodeBridge.stop();
    mainWindow = null;
  });
}

function createTray() {
  const iconBase64 = 'iVBORw0KGgoAAAANSUhEUgAAABAAAAAQCAYAAAAf8/9hAAAAZklEQVR42mNkQAO/fzP8Z6ACxsDAwMDAwMhACWBmYvgPwsxQMRjAAmYmKGAg3gCm/0eB6v4Tox6rmf9R8P///5947UdW/J947ScZBjAxmKNgFBDnBhyF9e//DAwMDFRwgAErGQ4AAJsNFR+w9a0AAAAASUVORK5CYII=';
  const icon = nativeImage.createFromDataURL(`data:image/png;base64,${iconBase64}`);
  
  tray = new Tray(icon);
  const contextMenu = Menu.buildFromTemplate([
    { label: 'OpenIsland v0.1.0', enabled: false },
    { type: 'separator' },
    {
      label: 'Переключить режим',
      click: () => {
        mainWindow?.webContents.send('island:toggle-mode');
      }
    },
    {
      label: 'Выход',
      click: () => {
        app.quit();
      }
    }
  ]);

  tray.setToolTip('OpenIsland — Dynamic Island + OpenCode');
  tray.setContextMenu(contextMenu);
}

function setupIPC() {
  ipcMain.handle('system:get-stats', async () => {
    return await systemMonitor.getStats();
  });

  ipcMain.on('system:set-monitoring-active', (_event, active: boolean) => {
    systemMonitor.setHighFrequency(active);
  });

  ipcMain.handle('opencode:get-info', async () => {
    const alive = await openCodeBridge.checkHealth();
    return alive ? { connected: true } : null;
  });

  ipcMain.handle('opencode:get-sessions', async () => {
    return await openCodeBridge.getSessions();
  });

  ipcMain.handle('opencode:get-models', async () => {
    return await openCodeBridge.getModels();
  });

  ipcMain.handle('opencode:create-session', async (_event, directory: string, title?: string) => {
    return await openCodeBridge.createSession(directory, title);
  });

  ipcMain.handle('opencode:send-prompt', async (_event, sessionId: string, prompt: string, modelId?: string) => {
    return await openCodeBridge.sendPrompt(sessionId, prompt, modelId);
  });

  ipcMain.on('window:minimize', () => {
    mainWindow?.minimize();
  });

  ipcMain.on('window:close', () => {
    app.quit();
  });
}

const gotTheLock = app.requestSingleInstanceLock();

if (!gotTheLock) {
  app.quit();
} else {
  app.on('second-instance', () => {
    if (mainWindow) {
      if (mainWindow.isMinimized()) mainWindow.restore();
      mainWindow.show();
      mainWindow.focus();
    }
  });

  app.whenReady().then(async () => {
    setupIPC();
    await createWindow();
    createTray();

    app.on('activate', () => {
      if (BrowserWindow.getAllWindows().length === 0) {
        createWindow();
      }
    });
  });

  app.on('window-all-closed', () => {
    if (process.platform !== 'darwin') {
      app.quit();
    }
  });
}
