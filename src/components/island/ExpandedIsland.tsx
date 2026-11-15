import React, { useState } from 'react';
import { 
  Activity, 
  Sparkles, 
  Code2, 
  Pin, 
  Minimize2, 
  Cpu, 
  MemoryStick, 
  Wifi, 
  Battery, 
  LogIn, 
  CheckCircle2, 
  Send,
  Terminal,
  ExternalLink
} from 'lucide-react';
import { useIslandStore, IslandTab, AIProvider } from '../../stores/useIslandStore';

interface ExpandedIslandProps {
  onCollapse: () => void;
}

export const ExpandedIsland: React.FC<ExpandedIslandProps> = ({ onCollapse }) => {
  const { 
    activeTab, 
    setActiveTab, 
    stats, 
    selectedProvider, 
    setSelectedProvider,
    isSessionActive,
    setSessionActive,
    isPinned,
    togglePin
  } = useIslandStore();

  const [promptText, setPromptText] = useState('');
  const [isLoggingIn, setIsLoggingIn] = useState(false);
  const [aiResponse, setAiResponse] = useState<string | null>(null);

  const formatNetSpeed = (bytesPerSec?: number) => {
    if (!bytesPerSec || bytesPerSec === 0) return '0 KB/s';
    if (bytesPerSec > 1024 * 1024) {
      return `${(bytesPerSec / (1024 * 1024)).toFixed(1)} MB/s`;
    }
    return `${Math.round(bytesPerSec / 1024)} KB/s`;
  };

  const handleAuth = async (provider: AIProvider) => {
    if (!window.electronAPI) return;
    setIsLoggingIn(true);
    try {
      const res = await window.electronAPI.openAISession(provider);
      setSessionActive(provider, res.isAuthenticated);
    } catch (e) {
      console.error(e);
    } finally {
      setIsLoggingIn(false);
    }
  };

  const handleSendPrompt = () => {
    if (!promptText.trim()) return;
    setAiResponse(`Ответ от ${selectedProvider.toUpperCase()}: Готов помочь с кодом! Введите команду или вопрос для генерации.`);
  };

  return (
    <div className="flex flex-col w-full h-full p-4 text-white select-none">
      {/* Верхняя панель: Табы и контролы */}
      <div className="flex items-center justify-between pb-3 border-b border-white/10">
        <div className="flex items-center gap-1.5 p-1 rounded-xl bg-white/5 border border-white/5">
          <button
            onClick={() => setActiveTab('monitor')}
            className={`flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-xs font-medium transition-all ${
              activeTab === 'monitor'
                ? 'bg-blue-600/80 text-white shadow-sm'
                : 'text-white/60 hover:text-white hover:bg-white/5'
            }`}
          >
            <Activity className="w-3.5 h-3.5" />
            <span>Монитор ПК</span>
          </button>

          <button
            onClick={() => setActiveTab('ai')}
            className={`flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-xs font-medium transition-all ${
              activeTab === 'ai'
                ? 'bg-blue-600/80 text-white shadow-sm'
                : 'text-white/60 hover:text-white hover:bg-white/5'
            }`}
          >
            <Sparkles className="w-3.5 h-3.5" />
            <span>ИИ Пульт</span>
          </button>

          <button
            onClick={() => setActiveTab('opencode')}
            className={`flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-xs font-medium transition-all ${
              activeTab === 'opencode'
                ? 'bg-blue-600/80 text-white shadow-sm'
                : 'text-white/60 hover:text-white hover:bg-white/5'
            }`}
          >
            <Code2 className="w-3.5 h-3.5" />
            <span>OpenCode</span>
          </button>
        </div>

        <div className="flex items-center gap-1">
          <button
            onClick={togglePin}
            title={isPinned ? 'Открепить окно' : 'Закрепить открытым'}
            className={`p-1.5 rounded-lg transition-colors ${
              isPinned ? 'text-blue-400 bg-blue-500/20' : 'text-white/50 hover:text-white hover:bg-white/5'
            }`}
          >
            <Pin className="w-4 h-4" />
          </button>
          <button
            onClick={onCollapse}
            title="Свернуть остров"
            className="p-1.5 rounded-lg text-white/50 hover:text-white hover:bg-white/5 transition-colors"
          >
            <Minimize2 className="w-4 h-4" />
          </button>
        </div>
      </div>

      {/* Контент табов */}
      <div className="flex-1 pt-3 overflow-y-auto">
        {/* ТАБ 1: МОНИТОР ПК */}
        {activeTab === 'monitor' && (
          <div className="grid grid-cols-2 gap-3">
            {/* CPU Карточка */}
            <div className="p-3 rounded-2xl bg-white/[0.03] border border-white/5 flex flex-col justify-between">
              <div className="flex items-center justify-between text-xs text-white/70">
                <div className="flex items-center gap-2">
                  <Cpu className="w-4 h-4 text-blue-400" />
                  <span className="font-semibold text-white">Процессор</span>
                </div>
                <span className="text-[11px] font-mono text-white/50">{stats?.cpu.cores ?? 4} ядер</span>
              </div>
              <div className="my-2">
                <div className="flex items-baseline justify-between mb-1">
                  <span className="text-2xl font-bold font-mono tracking-tight text-white">
                    {stats?.cpu.usage ?? 0}%
                  </span>
                  <span className="text-[10px] text-white/40 truncate max-w-[130px]">
                    {stats?.cpu.brand || 'CPU'}
                  </span>
                </div>
                <div className="w-full h-1.5 rounded-full bg-white/10 overflow-hidden">
                  <div
                    className="h-full bg-gradient-to-r from-blue-500 to-indigo-500 transition-all duration-500"
                    style={{ width: `${Math.min(100, stats?.cpu.usage ?? 0)}%` }}
                  />
                </div>
              </div>
            </div>

            {/* RAM Карточка */}
            <div className="p-3 rounded-2xl bg-white/[0.03] border border-white/5 flex flex-col justify-between">
              <div className="flex items-center justify-between text-xs text-white/70">
                <div className="flex items-center gap-2">
                  <MemoryStick className="w-4 h-4 text-emerald-400" />
                  <span className="font-semibold text-white">Оперативная память</span>
                </div>
                <span className="text-[11px] font-mono text-white/50">
                  {stats?.memory.used ?? 0} / {stats?.memory.total ?? 16} ГБ
                </span>
              </div>
              <div className="my-2">
                <div className="flex items-baseline justify-between mb-1">
                  <span className="text-2xl font-bold font-mono tracking-tight text-white">
                    {stats?.memory.usagePercentage ?? 0}%
                  </span>
                  <span className="text-[10px] text-white/40">
                    Свободно: {stats?.memory.free ?? 0} ГБ
                  </span>
                </div>
                <div className="w-full h-1.5 rounded-full bg-white/10 overflow-hidden">
                  <div
                    className="h-full bg-gradient-to-r from-emerald-500 to-teal-400 transition-all duration-500"
                    style={{ width: `${Math.min(100, stats?.memory.usagePercentage ?? 0)}%` }}
                  />
                </div>
              </div>
            </div>

            {/* Сеть Карточка */}
            <div className="p-3 rounded-2xl bg-white/[0.03] border border-white/5 flex items-center justify-between">
              <div className="flex items-center gap-2.5">
                <div className="p-2 rounded-xl bg-purple-500/10 text-purple-400">
                  <Wifi className="w-4 h-4" />
                </div>
                <div>
                  <div className="text-xs font-medium text-white/80">Сетевой трафик</div>
                  <div className="text-[11px] text-white/50">Загрузка / Отдача</div>
                </div>
              </div>
              <div className="text-right font-mono text-xs">
                <div className="text-emerald-400 font-semibold">↓ {formatNetSpeed(stats?.network?.rx_sec)}</div>
                <div className="text-blue-400">↑ {formatNetSpeed(stats?.network?.tx_sec)}</div>
              </div>
            </div>

            {/* Батарея Карточка */}
            <div className="p-3 rounded-2xl bg-white/[0.03] border border-white/5 flex items-center justify-between">
              <div className="flex items-center gap-2.5">
                <div className="p-2 rounded-xl bg-amber-500/10 text-amber-400">
                  <Battery className="w-4 h-4" />
                </div>
                <div>
                  <div className="text-xs font-medium text-white/80">Питание</div>
                  <div className="text-[11px] text-white/50">
                    {stats?.battery?.hasBattery
                      ? stats.battery.isCharging ? 'Заряжается' : 'От батареи'
                      : 'Стационарный ПК (Сеть)'}
                  </div>
                </div>
              </div>
              <div className="text-right font-mono text-xs">
                {stats?.battery?.hasBattery ? (
                  <span className="text-base font-bold text-amber-300">{stats.battery.percent}%</span>
                ) : (
                  <span className="text-white/60">100% (AC)</span>
                )}
              </div>
            </div>
          </div>
        )}

        {/* ТАБ 2: ИИ ПУЛЬТ (AI HUB) */}
        {activeTab === 'ai' && (
          <div className="flex flex-col gap-3">
            {/* Выбор провайдера с подпиской */}
            <div className="grid grid-cols-3 gap-2">
              {(['claude', 'chatgpt', 'gemini'] as AIProvider[]).map((p) => {
                const labels: Record<AIProvider, { name: string; sub: string }> = {
                  claude: { name: 'Claude', sub: 'Pro / Team' },
                  chatgpt: { name: 'ChatGPT', sub: 'Plus / 4o' },
                  gemini: { name: 'Gemini', sub: 'Advanced' }
                };
                const active = selectedProvider === p;
                const authenticated = isSessionActive[p];

                return (
                  <div
                    key={p}
                    onClick={() => setSelectedProvider(p)}
                    className={`p-2.5 rounded-2xl border transition-all cursor-pointer ${
                      active
                        ? 'bg-blue-600/15 border-blue-500/40 shadow-sm'
                        : 'bg-white/[0.02] border-white/5 hover:bg-white/[0.05]'
                    }`}
                  >
                    <div className="flex items-center justify-between mb-1">
                      <span className="text-xs font-bold text-white">{labels[p].name}</span>
                      {authenticated ? (
                        <CheckCircle2 className="w-3.5 h-3.5 text-emerald-400" />
                      ) : (
                        <span className="text-[9px] text-amber-400/80 px-1 rounded bg-amber-400/10">Вход</span>
                      )}
                    </div>
                    <div className="text-[10px] text-white/50">{labels[p].sub}</div>
                  </div>
                );
              })}
            </div>

            {/* Карточка авторизации подписки */}
            <div className="p-3 rounded-2xl bg-white/[0.03] border border-white/5 flex items-center justify-between">
              <div>
                <div className="text-xs font-semibold text-white">
                  Сессия {selectedProvider.toUpperCase()}
                </div>
                <div className="text-[11px] text-white/60">
                  {isSessionActive[selectedProvider]
                    ? 'Авторизован в браузере. Действует ваша активная подписка.'
                    : 'Войдите в свой аккаунт, чтобы использовать подписку без API-токенов.'}
                </div>
              </div>

              <button
                onClick={() => handleAuth(selectedProvider)}
                disabled={isLoggingIn}
                className="flex items-center gap-1.5 px-3 py-1.5 rounded-xl bg-white/10 hover:bg-white/20 border border-white/10 text-xs font-medium text-white transition-all disabled:opacity-50"
              >
                <LogIn className="w-3.5 h-3.5" />
                <span>{isLoggingIn ? 'Открытие...' : isSessionActive[selectedProvider] ? 'Сменить' : 'Войти'}</span>
              </button>
            </div>

            {/* Поле быстрого промпта */}
            <div className="flex flex-col gap-2">
              <div className="relative">
                <input
                  type="text"
                  value={promptText}
                  onChange={(e) => setPromptText(e.target.value)}
                  onKeyDown={(e) => e.key === 'Enter' && handleSendPrompt()}
                  placeholder={`Спросить ${selectedProvider.toUpperCase()} о коде или задаче...`}
                  className="w-full px-3.5 py-2.5 pr-10 text-xs rounded-xl bg-white/5 border border-white/10 text-white placeholder-white/40 focus:outline-none focus:border-blue-500/50 transition-colors"
                />
                <button
                  onClick={handleSendPrompt}
                  className="absolute right-2 top-2 p-1 rounded-lg text-blue-400 hover:text-blue-300 hover:bg-blue-500/10 transition-colors"
                >
                  <Send className="w-4 h-4" />
                </button>
              </div>

              {aiResponse && (
                <div className="p-3 rounded-xl bg-blue-950/30 border border-blue-500/20 text-xs text-blue-100 font-mono">
                  {aiResponse}
                </div>
              )}
            </div>
          </div>
        )}

        {/* ТАБ 3: OPENCODE & DEV */}
        {activeTab === 'opencode' && (
          <div className="flex flex-col gap-3">
            <div className="grid grid-cols-2 gap-3">
              <div className="p-3 rounded-2xl bg-white/[0.03] border border-white/5">
                <div className="flex items-center gap-2 mb-2 text-xs font-semibold text-white">
                  <Terminal className="w-4 h-4 text-blue-400" />
                  <span>Локальные Dev-порты</span>
                </div>
                <div className="flex flex-col gap-1.5 text-xs">
                  <div className="flex items-center justify-between text-white/70">
                    <span className="font-mono text-emerald-400">:5173</span>
                    <span className="text-[10px] text-white/40">OpenIsland (Vite HMR)</span>
                  </div>
                  <div className="flex items-center justify-between text-white/70">
                    <span className="font-mono text-white/40">:3000</span>
                    <span className="text-[10px] text-white/40">Свободен</span>
                  </div>
                </div>
              </div>

              <div className="p-3 rounded-2xl bg-white/[0.03] border border-white/5">
                <div className="flex items-center gap-2 mb-2 text-xs font-semibold text-white">
                  <Code2 className="w-4 h-4 text-emerald-400" />
                  <span>Git Репозиторий</span>
                </div>
                <div className="text-xs text-white/80 font-mono">
                  main <span className="text-emerald-400 font-semibold">• up to date</span>
                </div>
                <div className="text-[10px] text-white/40 mt-1">
                  Рабочая директория чиста
                </div>
              </div>
            </div>

            <div className="p-3 rounded-2xl bg-white/[0.03] border border-white/5">
              <div className="text-xs font-semibold text-white mb-1.5">Быстрые действия разработчика</div>
              <div className="flex gap-2">
                <button
                  onClick={() => {
                    setActiveTab('ai');
                    setPromptText('Сгенерируй семантическое git commit сообщение для последних изменений');
                  }}
                  className="flex items-center gap-1.5 px-3 py-1.5 rounded-xl bg-white/5 hover:bg-white/10 border border-white/5 text-xs text-white/80 transition-all"
                >
                  <ExternalLink className="w-3 h-3 text-blue-400" />
                  <span>Git Commit Msg</span>
                </button>
                <button
                  onClick={() => {
                    setActiveTab('ai');
                    setPromptText('Объясни и оптимизируй следующую команду powershell: ');
                  }}
                  className="flex items-center gap-1.5 px-3 py-1.5 rounded-xl bg-white/5 hover:bg-white/10 border border-white/5 text-xs text-white/80 transition-all"
                >
                  <Terminal className="w-3 h-3 text-emerald-400" />
                  <span>Команда PowerShell</span>
                </button>
              </div>
            </div>
          </div>
        )}
      </div>
    </div>
  );
};
