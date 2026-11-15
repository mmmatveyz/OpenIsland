import { contextBridge, ipcRenderer } from 'electron';

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

  // Запрос разовых метрик системы
  getSystemStats: (): Promise<SystemStats> => {
    return ipcRenderer.invoke('system:get-stats');
  },

  // Подписка на обновление метрик в реальном времени
  onSystemStats: (callback: (stats: SystemStats) => void) => {
    const handler = (_event: any, stats: SystemStats) => callback(stats);
    ipcRenderer.on('system:stats-updated', handler);
    return () => {
      ipcRenderer.removeListener('system:stats-updated', handler);
    };
  },

  // Управление режимом опроса (ускорение при развернутом острове)
  setMonitoringActive: (active: boolean) => {
    ipcRenderer.send('system:set-monitoring-active', active);
  },

  // Открытие окна входа в аккаунт с подпиской
  openAISession: (provider: 'claude' | 'chatgpt' | 'gemini') => {
    return ipcRenderer.invoke('ai:open-session', provider);
  },

  // Свернуть / Закрыть приложение
  minimize: () => ipcRenderer.send('window:minimize'),
  close: () => ipcRenderer.send('window:close')
};

contextBridge.exposeInMainWorld('electronAPI', electronAPI);

export type ElectronAPI = typeof electronAPI;
