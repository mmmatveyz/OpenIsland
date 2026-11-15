import { app, BrowserWindow, ipcMain, screen, Tray, Menu, nativeImage } from 'electron';
import path from 'path';
import http from 'http';
import { systemMonitor } from './ipc/system-monitor';
import { openCodeBridge } from './ipc/opencode';

let mainWindow: BrowserWindow | null = null;
let tray: Tray | null = null;
let mousePollTimer: NodeJS.Timeout | null = null;

let currentIslandBounds = {
  width: 300,
  height: 44,
  y: 8
};
let isHoveredState = false;

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
          timeout: 800
        },
        (res) => {
          resolve(true);
        }
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

  const winWidth = 720;
  const winHeight = 480;
  const posX = displayX + Math.round((displayWidth - winWidth) / 2);
  const posY = displayY;

  console.log(`[OpenIsland] Screen: ${displayWidth}x${primaryDisplay.bounds.height}, Window pos: (${posX}, ${posY})`);

  mainWindow = new BrowserWindow({
    width: winWidth,
    height: winHeight,
    x: posX,
    y: posY,
    transparent: true,
    backgroundColor: '#00000000',
    frame: false,
    alwaysOnTop: true,
    resizable: false,
    hasShadow: false,
    skipTaskbar: false,
    show: false, // Покажем сразу после загрузки контента
    webPreferences: {
      preload: path.join(__dirname, 'preload.js'),
      contextIsolation: true,
      nodeIntegration: false
    }
  });

  // Поверх всех окон на Windows
  mainWindow.setAlwaysOnTop(true, 'screen-saver');

  // Сквозной клик
  mainWindow.setIgnoreMouseEvents(true, { forward: true });

  systemMonitor.setWindow(mainWindow);
  systemMonitor.start();

  openCodeBridge.setWindow(mainWindow);
  openCodeBridge.start();

  startMouseTracking(posX, posY, winWidth);

  // Проверяем: запущен ли Vite сервер (http://localhost:5173)
  const isViteReady = await checkUrlLive('http://localhost:5173');
  if (isViteReady) {
    console.log('[OpenIsland] Загрузка через Vite HMR: http://localhost:5173');
    await mainWindow.loadURL('http://localhost:5173');
  } else {
    const distPath = path.join(__dirname, '../dist/index.html');
    console.log('[OpenIsland] Vite не запущен, загрузка продакшн сборки:', distPath);
    await mainWindow.loadFile(distPath);
  }

  mainWindow.show();
  mainWindow.focus();

  mainWindow.on('closed', () => {
    stopMouseTracking();
    systemMonitor.stop();
    openCodeBridge.stop();
    mainWindow = null;
  });
}

function startMouseTracking(winX: number, winY: number, winWidth: number) {
  if (mousePollTimer) clearInterval(mousePollTimer);

  mousePollTimer = setInterval(() => {
    if (!mainWindow || mainWindow.isDestroyed()) return;

    const cursor = screen.getCursorScreenPoint();

    const islandLeft = winX + (winWidth - currentIslandBounds.width) / 2;
    const islandRight = islandLeft + currentIslandBounds.width;
    const islandTop = winY + currentIslandBounds.y;
    const islandBottom = islandTop + currentIslandBounds.height;

    const inside =
      cursor.x >= islandLeft &&
      cursor.x <= islandRight &&
      cursor.y >= islandTop &&
      cursor.y <= islandBottom;

    if (inside !== isHoveredState) {
      isHoveredState = inside;
      mainWindow.setIgnoreMouseEvents(!inside, { forward: true });
      mainWindow.webContents.send('window:hover-changed', inside);
    }
  }, 50);
}

function stopMouseTracking() {
  if (mousePollTimer) {
    clearInterval(mousePollTimer);
    mousePollTimer = null;
  }
}

function createTray() {
  const icon = nativeImage.createEmpty();
  tray = new Tray(icon);
  const contextMenu = Menu.buildFromTemplate([
    { label: 'OpenIsland v0.1.0', enabled: false },
    { type: 'separator' },
    {
      label: 'Показать/Скрыть',
      click: () => {
        if (!mainWindow) {
          createWindow();
        } else if (mainWindow.isVisible()) {
          mainWindow.hide();
        } else {
          mainWindow.show();
        }
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
  ipcMain.on('window:update-island-bounds', (_event, bounds: { width: number; height: number; y?: number }) => {
    currentIslandBounds = {
      width: bounds.width,
      height: bounds.height,
      y: bounds.y ?? 8
    };
  });

  ipcMain.on('window:set-ignore-mouse-events', (event, ignore: boolean) => {
    const win = BrowserWindow.fromWebContents(event.sender);
    if (win && !win.isDestroyed()) {
      win.setIgnoreMouseEvents(ignore, { forward: true });
    }
  });

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
