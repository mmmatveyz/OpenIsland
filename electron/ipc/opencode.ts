import http from 'http';
import path from 'path';
import { execFile } from 'child_process';
import { BrowserWindow } from 'electron';

export interface OpenCodeServerInfo {
  version: string;
  pid: number;
  hostname: string;
  urls: string[];
}

export interface OpenCodeSession {
  id: string;
  projectID: string;
  title: string;
  agent: string;
  model: {
    id: string;
    providerID: string;
  };
  outcome?: string;
  time: {
    created: number;
    updated: number;
  };
  location: {
    directory: string;
  };
}

export interface OpenCodeModel {
  id: string;
  name: string;
  providerID: string;
  status: string;
  enabled: boolean;
}

class OpenCodeBridgeService {
  private serverUrl: string = 'http://127.0.0.1:28894';
  private mainWindow: BrowserWindow | null = null;
  private isConnected: boolean = false;
  private pollTimer: NodeJS.Timeout | null = null;
  private activeSessionId: string | null = null;

  public setWindow(win: BrowserWindow) {
    this.mainWindow = win;
  }

  public setCustomUrl(url: string) {
    this.serverUrl = url;
    this.checkHealth();
  }

  public getActiveUrl(): string {
    return this.serverUrl;
  }

  // Динамическое обнаружение порта OpenCode
  public async discoverServerUrl(): Promise<string> {
    const cliPaths = [
      path.join(process.env.LOCALAPPDATA || '', 'Programs/@opencode-aidesktop/resources/opencode-cli.exe'),
      'opencode-cli',
      'opencode'
    ];

    for (const cli of cliPaths) {
      try {
        const detected = await new Promise<string>((resolve, reject) => {
          execFile(cli, ['service', 'status'], { timeout: 2000 }, (err, stdout) => {
            if (err) return reject(err);
            const trimmed = (stdout || '').trim();
            if (trimmed.startsWith('http://') || trimmed.startsWith('https://')) {
              resolve(trimmed);
            } else {
              reject(new Error('Invalid URL'));
            }
          });
        });

        if (detected) {
          this.serverUrl = detected;
          return detected;
        }
      } catch {
        // Проверяем следующий путь
      }
    }
    return this.serverUrl;
  }

  public async start() {
    await this.discoverServerUrl();
    await this.checkHealth();

    if (this.pollTimer) clearInterval(this.pollTimer);
    this.pollTimer = setInterval(async () => {
      if (!this.isConnected) {
        await this.discoverServerUrl();
      }
      await this.checkHealth();
    }, 3000);
  }

  public stop() {
    if (this.pollTimer) {
      clearInterval(this.pollTimer);
      this.pollTimer = null;
    }
  }

  private makeRequest<T>(method: string, path: string, body?: any): Promise<T> {
    return new Promise((resolve, reject) => {
      const url = new URL(path, this.serverUrl);
      const postData = body ? JSON.stringify(body) : undefined;

      const req = http.request(
        {
          hostname: url.hostname,
          port: url.port,
          path: url.pathname + url.search,
          method,
          headers: {
            'Content-Type': 'application/json',
            ...(postData ? { 'Content-Length': Buffer.byteLength(postData) } : {})
          },
          timeout: 5000
        },
        (res) => {
          let data = '';
          res.on('data', (chunk) => (data += chunk));
          res.on('end', () => {
            try {
              if (res.statusCode && res.statusCode >= 200 && res.statusCode < 300) {
                resolve(data ? JSON.parse(data) : ({} as T));
              } else {
                reject(new Error(`OpenCode API error: ${res.statusCode} ${data}`));
              }
            } catch (e) {
              reject(e);
            }
          });
        }
      );

      req.on('error', (err) => reject(err));
      req.on('timeout', () => {
        req.destroy();
        reject(new Error('OpenCode API timeout'));
      });

      if (postData) req.write(postData);
      req.end();
    });
  }

  public async checkHealth(): Promise<boolean> {
    try {
      const info = await this.makeRequest<OpenCodeServerInfo>('GET', '/api/info');
      const wasConnected = this.isConnected;
      this.isConnected = !!info?.version;

      if (!wasConnected && this.isConnected) {
        this.emitToRenderer('opencode:connected', { ...info, url: this.serverUrl });
      }
      return this.isConnected;
    } catch {
      if (this.isConnected) {
        this.isConnected = false;
        this.emitToRenderer('opencode:disconnected', null);
      }
      return false;
    }
  }

  public async getSessions(): Promise<OpenCodeSession[]> {
    try {
      const res = await this.makeRequest<{ data: OpenCodeSession[] }>('GET', '/api/session');
      return res.data || [];
    } catch (e) {
      return [];
    }
  }

  public async getModels(): Promise<OpenCodeModel[]> {
    try {
      const res = await this.makeRequest<{ data: any[] }>('GET', '/api/model');
      return (res.data || []).map((m) => ({
        id: m.id,
        name: m.name || m.id,
        providerID: m.providerID,
        status: m.status,
        enabled: m.enabled
      }));
    } catch (e) {
      return [];
    }
  }

  public async createSession(directory: string, title?: string): Promise<OpenCodeSession> {
    const res = await this.makeRequest<OpenCodeSession>('POST', '/api/session', {
      title: title || 'OpenIsland Session',
      location: { directory }
    });
    this.activeSessionId = res.id;
    return res;
  }

  public async sendPrompt(sessionId: string, prompt: string, modelId?: string): Promise<any> {
    this.activeSessionId = sessionId;
    this.emitToRenderer('opencode:status-change', {
      status: 'busy',
      message: 'Выполняется запрос...'
    });

    try {
      const payload: any = {
        prompt: [
          {
            type: 'text',
            text: prompt
          }
        ]
      };
      if (modelId) {
        payload.model = modelId;
      }

      const res = await this.makeRequest<any>('POST', `/api/session/${sessionId}/prompt`, payload);
      this.emitToRenderer('opencode:status-change', {
        status: 'idle',
        message: 'Готов'
      });
      return res;
    } catch (e: any) {
      this.emitToRenderer('opencode:status-change', {
        status: 'error',
        message: e?.message || 'Ошибка выполнения'
      });
      throw e;
    }
  }

  private emitToRenderer(channel: string, payload: any) {
    if (this.mainWindow && !this.mainWindow.isDestroyed()) {
      this.mainWindow.webContents.send(channel, payload);
    }
  }
}

export const openCodeBridge = new OpenCodeBridgeService();
