import { BrowserWindow, session } from 'electron';

export interface AISessionStatus {
  provider: 'claude' | 'chatgpt' | 'gemini';
  isAuthenticated: boolean;
  userEmail?: string;
}

export class AISessionManager {
  private static authUrls = {
    claude: 'https://claude.ai/login',
    chatgpt: 'https://chatgpt.com/auth/login',
    gemini: 'https://gemini.google.com/'
  };

  public static async openAuthWindow(provider: 'claude' | 'chatgpt' | 'gemini', parentWindow?: BrowserWindow): Promise<AISessionStatus> {
    const partition = `persist:ai_${provider}`;
    const sess = session.fromPartition(partition);

    const authWin = new BrowserWindow({
      width: 900,
      height: 700,
      title: `OpenIsland — Авторизация ${provider.toUpperCase()}`,
      autoHideMenuBar: true,
      parent: parentWindow || undefined,
      modal: false,
      webPreferences: {
        partition,
        nodeIntegration: false,
        contextIsolation: true
      }
    });

    const targetUrl = this.authUrls[provider];
    await authWin.loadURL(targetUrl);

    return new Promise((resolve) => {
      authWin.on('closed', async () => {
        // После закрытия окна проверяем наличие кук авторизации
        const cookies = await sess.cookies.get({});
        let isAuthenticated = false;

        if (provider === 'claude') {
          isAuthenticated = cookies.some(c => c.name.includes('sessionKey') || c.name.includes('claude'));
        } else if (provider === 'chatgpt') {
          isAuthenticated = cookies.some(c => c.name.includes('session-token') || c.name.includes('oai-'));
        } else if (provider === 'gemini') {
          isAuthenticated = cookies.some(c => c.name === 'SID' || c.name === '__Secure-1PSID');
        }

        resolve({
          provider,
          isAuthenticated
        });
      });
    });
  }
}
