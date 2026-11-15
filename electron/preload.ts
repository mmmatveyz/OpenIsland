import { contextBridge, ipcRenderer } from 'electron';
import { OpenCodeServerInfo, OpenCodeSession, OpenCodeModel } from './ipc/opencode';

export interface SystemStats {
  cpu: {
    usage: number; // 0 - 100%
    temperature?: number;
    cores: number;
    brand: string;
  };
  memory: {
    total: number; // GB
    used: number; // GB
    free: number; // GB
    usagePercentage: number; // 0 - 100%
  };
  gpu?: {
    model: string;
    usage?: number;
    memoryUsed?: number;
  };
  battery?: {
    hasBattery: boolean;
    isCharging: boolean;
    percent: number;
  };
  network?: {
    rx_sec: number; // bytes/sec download
    tx_sec: number; // bytes/sec upload
  };
}

const electronAPI = {
  // Управление прозрачностью для сквозных кликов (click-through)
  setIgnoreMouseEvents: (ignore: boolean) => {
    ipcRenderer.send('window:set-ignore-mouse-events', ignore);
  },

  updateIslandBounds: (bounds: { width: number; height: number; y?: number }) => {
    ipcRenderer.send('window:update-island-bounds', bounds);
  },

  onHoverChange: (callback: (isHovered: boolean) => void) => {
    const handler = (_event: any, hovered: boolean) => callback(hovered);
    ipcRenderer.on('window:hover-changed', handler);
    return () => {
      ipcRenderer.removeListener('window:hover-changed', handler);
    };
  },

  // Метрики системы
  getSystemStats: (): Promise<SystemStats> => {
    return ipcRenderer.invoke('system:get-stats');
  },

  onSystemStats: (callback: (stats: SystemStats) => void) => {
    const handler = (_event: any, stats: SystemStats) => callback(stats);
    ipcRenderer.on('system:stats-updated', handler);
    return () => {
      ipcRenderer.removeListener('system:stats-updated', handler);
    };
  },

  setMonitoringActive: (active: boolean) => {
    ipcRenderer.send('system:set-monitoring-active', active);
  },

  // OpenCode API интеграция
  getOpenCodeInfo: (): Promise<OpenCodeServerInfo | null> => {
    return ipcRenderer.invoke('opencode:get-info');
  },

  getOpenCodeSessions: (): Promise<OpenCodeSession[]> => {
    return ipcRenderer.invoke('opencode:get-sessions');
  },

  getOpenCodeModels: (): Promise<OpenCodeModel[]> => {
    return ipcRenderer.invoke('opencode:get-models');
  },

  createOpenCodeSession: (directory: string, title?: string): Promise<OpenCodeSession> => {
    return ipcRenderer.invoke('opencode:create-session', directory, title);
  },

  sendOpenCodePrompt: (sessionId: string, prompt: string, modelId?: string): Promise<any> => {
    return ipcRenderer.invoke('opencode:send-prompt', sessionId, prompt, modelId);
  },

  onOpenCodeStatus: (callback: (status: { status: string; message: string }) => void) => {
    const handler = (_event: any, status: any) => callback(status);
    ipcRenderer.on('opencode:status-change', handler);
    return () => {
      ipcRenderer.removeListener('opencode:status-change', handler);
    };
  },

  // Свернуть / Закрыть приложение
  minimize: () => ipcRenderer.send('window:minimize'),
  close: () => ipcRenderer.send('window:close')
};

contextBridge.exposeInMainWorld('electronAPI', electronAPI);

export type ElectronAPI = typeof electronAPI;
