import si from 'systeminformation';
import { BrowserWindow } from 'electron';
import { SystemStats } from '../preload';

class SystemMonitorService {
  private timer: NodeJS.Timeout | null = null;
  private intervalMs: number = 2000;
  private isHighFrequency: boolean = false;
  private mainWindow: BrowserWindow | null = null;
  private cpuBrand: string = 'CPU';
  private cpuCores: number = 4;

  constructor() {
    this.initCpuStaticInfo();
  }

  private async initCpuStaticInfo() {
    try {
      const cpu = await si.cpu();
      this.cpuBrand = `${cpu.manufacturer} ${cpu.brand}`.trim();
      this.cpuCores = cpu.cores;
    } catch {
      // Игнорируем ошибки при получении статики
    }
  }

  public setWindow(win: BrowserWindow) {
    this.mainWindow = win;
  }

  public start() {
    if (this.timer) clearInterval(this.timer);
    this.timer = setInterval(() => this.collectAndEmitStats(), this.intervalMs);
  }

  public stop() {
    if (this.timer) {
      clearInterval(this.timer);
      this.timer = null;
    }
  }

  public setHighFrequency(active: boolean) {
    this.isHighFrequency = active;
    this.intervalMs = active ? 1000 : 2500;
    this.start();
  }

  public async getStats(): Promise<SystemStats> {
    try {
      const [load, mem, battery, network] = await Promise.all([
        si.currentLoad(),
        si.mem(),
        si.battery().catch(() => ({ hasBattery: false, isCharging: false, percent: 0 })),
        si.networkStats().catch(() => [])
      ]);

      const net0 = network && network.length > 0 ? network[0] : null;

      const stats: SystemStats = {
        cpu: {
          usage: Math.round(load.currentLoad || 0),
          cores: this.cpuCores,
          brand: this.cpuBrand
        },
        memory: {
          total: Number((mem.total / (1024 ** 3)).toFixed(1)),
          used: Number((mem.used / (1024 ** 3)).toFixed(1)),
          free: Number((mem.free / (1024 ** 3)).toFixed(1)),
          usagePercentage: Math.round((mem.used / mem.total) * 100)
        },
        battery: {
          hasBattery: battery.hasBattery,
          isCharging: battery.isCharging,
          percent: battery.percent
        },
        network: net0 ? {
          rx_sec: Math.max(0, net0.rx_sec || 0),
          tx_sec: Math.max(0, net0.tx_sec || 0)
        } : undefined
      };

      return stats;
    } catch (err) {
      console.error('Ошибка сбора метрик системы:', err);
      return {
        cpu: { usage: 0, cores: 4, brand: 'CPU' },
        memory: { total: 16, used: 8, free: 8, usagePercentage: 50 }
      };
    }
  }

  private async collectAndEmitStats() {
    if (!this.mainWindow || this.mainWindow.isDestroyed()) return;
    const stats = await this.getStats();
    if (!this.mainWindow.isDestroyed()) {
      this.mainWindow.webContents.send('system:stats-updated', stats);
    }
  }
}

export const systemMonitor = new SystemMonitorService();
