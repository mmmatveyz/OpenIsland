import { app, BrowserWindow, ipcMain, screen, Tray, Menu, nativeImage } from 'electron';
import path from 'path';
import { systemMonitor } from './ipc/system-monitor';
import { openCodeBridge } from './ipc/opencode';

let mainWindow: BrowserWindow | null = null;
let tray: Tray | null = null;

const isDev = !app.isPackaged || process.env.NODE_ENV === 'development';

function createWindow() {
  const primaryDisplay = screen.getPrimaryDisplay();
  const { width: screenWidth } = primaryDisplay.workAreaSize;

  const winWidth = 720;
  const winHeight = 500;
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

  if (isDev) {
    mainWindow.loadURL('http://localhost:5173').catch(() => {
      mainWindow?.loadFile(path.join(__dirname, '../dist/index.html'));
    });
  } else {
    mainWindow.loadFile(path.join(__dirname, '../dist/index.html'));
  }

  mainWindow.on('closed', () => {
    systemMonitor.stop();
    openCodeBridge.stop();
    mainWindow = null;
  });
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
