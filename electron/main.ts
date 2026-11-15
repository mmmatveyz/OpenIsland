import { app, BrowserWindow, ipcMain, screen, Tray, Menu, nativeImage } from 'electron';
import path from 'path';
import { systemMonitor } from './ipc/system-monitor';
import { openCodeBridge } from './ipc/opencode';

let mainWindow: BrowserWindow | null = null;
let tray: Tray | null = null;
let mousePollTimer: NodeJS.Timeout | null = null;

// Текущие границы активного островка (для идеального сквозного клика)
let currentIslandBounds = {
  width: 300,
  height: 44,
  y: 8
};
let isHoveredState = false;

const isDev = !app.isPackaged || process.env.NODE_ENV === 'development';

function createWindow() {
  const primaryDisplay = screen.getPrimaryDisplay();
  const { width: screenWidth } = primaryDisplay.workAreaSize;

  const winWidth = 720;
  const winHeight = 480;
  const posX = Math.round((screenWidth - winWidth) / 2);
  const posY = 0;

  mainWindow = new BrowserWindow({
    width: winWidth,
    height: winHeight,
    x: posX,
    y: posY,
    transparent: true,
    frame: false,
    alwaysOnTop: true,
    resizable: false,
    hasShadow: false,
    skipTaskbar: false,
    webPreferences: {
      preload: path.join(__dirname, 'preload.js'),
      contextIsolation: true,
      nodeIntegration: false
    }
  });

  // На старте включаем сквозной клик
  mainWindow.setIgnoreMouseEvents(true, { forward: true });

  systemMonitor.setWindow(mainWindow);
  systemMonitor.start();

  openCodeBridge.setWindow(mainWindow);
  openCodeBridge.start();

  // Запуск аппаратного трекинга мыши для идеального Click-Through на Windows
  startMouseTracking(posX, posY, winWidth);

  if (isDev) {
    mainWindow.loadURL('http://localhost:5173').catch(() => {
      mainWindow?.loadFile(path.join(__dirname, '../dist/index.html'));
    });
  } else {
    mainWindow.loadFile(path.join(__dirname, '../dist/index.html'));
  }

  mainWindow.on('closed', () => {
    stopMouseTracking();
    systemMonitor.stop();
    openCodeBridge.stop();
    mainWindow = null;
  });
}

// Надежный Win32-трекинг мыши: опрашиваем положение курсора каждые 50мс
function startMouseTracking(winX: number, winY: number, winWidth: number) {
  if (mousePollTimer) clearInterval(mousePollTimer);

  mousePollTimer = setInterval(() => {
    if (!mainWindow || mainWindow.isDestroyed()) return;

    const cursor = screen.getCursorScreenPoint();

    // Границы активного островка относительно экрана
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
  // Renderer сообщает текущие размеры островка (в свернутом или развернутом виде)
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

  // Системные метрики
  ipcMain.handle('system:get-stats', async () => {
    return await systemMonitor.getStats();
  });

  ipcMain.on('system:set-monitoring-active', (_event, active: boolean) => {
    systemMonitor.setHighFrequency(active);
  });

  // OpenCode API
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
      mainWindow.focus();
    }
  });

  app.whenReady().then(() => {
    setupIPC();
    createWindow();
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
