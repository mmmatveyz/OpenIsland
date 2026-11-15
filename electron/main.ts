import { app, BrowserWindow, ipcMain, screen, Tray, Menu, nativeImage } from 'electron';
import path from 'path';
import { systemMonitor } from './ipc/system-monitor';
import { AISessionManager } from './ipc/ai-session';

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

  // На старте включаем сквозной клик (ignoreMouseEvents), 
  // чтобы пользователь мог кликать сквозь прозрачные зоны окна
  mainWindow.setIgnoreMouseEvents(true, { forward: true });

  systemMonitor.setWindow(mainWindow);
  systemMonitor.start();

  if (isDev) {
    mainWindow.loadURL('http://localhost:5173');
    // mainWindow.webContents.openDevTools({ mode: 'detach' });
  } else {
    mainWindow.loadFile(path.join(__dirname, '../dist/index.html'));
  }

  mainWindow.on('closed', () => {
    systemMonitor.stop();
    mainWindow = null;
  });
}

function createTray() {
  // Простая SVG/иконка трея
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

// Регистрация IPC обработчиков
function setupIPC() {
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

  ipcMain.handle('ai:open-session', async (_event, provider: 'claude' | 'chatgpt' | 'gemini') => {
    return await AISessionManager.openAuthWindow(provider, mainWindow || undefined);
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
