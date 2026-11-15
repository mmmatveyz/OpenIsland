import React, { useState } from 'react';
import { 
  Terminal, 
  Activity, 
  Cpu, 
  MemoryStick, 
  Wifi, 
  Battery, 
  Send, 
  Pin, 
  Minimize2, 
  Loader2, 
  FolderGit2, 
  Play, 
  Sparkles, 
  Layers
} from 'lucide-react';
import { useIslandStore } from '../../stores/useIslandStore';

interface ExpandedIslandProps {
  onCollapse: () => void;
}

export const ExpandedIsland: React.FC<ExpandedIslandProps> = ({ onCollapse }) => {
  const { 
    activeTab, 
    setActiveTab, 
    stats, 
    isOpenCodeConnected,
    openCodeSessions,
    openCodeModels,
    selectedSessionId,
    setSelectedSessionId,
    selectedModelId,
    setSelectedModelId,
    agentStatus,
    setAgentStatus,
    isPinned,
    togglePin
  } = useIslandStore();

  const [promptText, setPromptText] = useState('');
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [lastResponse, setLastResponse] = useState<string | null>(null);

  const formatNetSpeed = (bytesPerSec?: number) => {
    if (!bytesPerSec || bytesPerSec === 0) return '0 KB/s';
    if (bytesPerSec > 1024 * 1024) {
      return `${(bytesPerSec / (1024 * 1024)).toFixed(1)} MB/s`;
    }
    return `${Math.round(bytesPerSec / 1024)} KB/s`;
  };

  const handleSendPrompt = async () => {
    if (!promptText.trim() || !window.electronAPI) return;

    const sessionId = selectedSessionId || openCodeSessions[0]?.id;
    if (!sessionId) {
      alert('Нет активной сессии OpenCode. Создайте или выберите сессию.');
      return;
    }

    setIsSubmitting(true);
    setAgentStatus({ status: 'busy', message: 'OpenCode обрабатывает задачу...' });

    try {
      await window.electronAPI.sendOpenCodePrompt(sessionId, promptText, selectedModelId || undefined);
      setLastResponse(`Команда успешно отправлена агенту OpenCode в сессию: ${sessionId.slice(0, 12)}...`);
      setPromptText('');
      setAgentStatus({ status: 'idle', message: 'Задача выполнена' });
    } catch (e: any) {
      setLastResponse(`Ошибка: ${e?.message || 'Не удалось отправить промпт'}`);
      setAgentStatus({ status: 'error', message: 'Ошибка запроса' });
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <div className="flex flex-col w-full h-full p-4 text-white select-none">
      {/* Верхняя панель: Переключение табов и кнопки управления */}
      <div className="flex items-center justify-between pb-3 border-b border-white/10">
        <div className="flex items-center gap-1.5 p-1 rounded-xl bg-white/5 border border-white/5">
          <button
            onClick={() => setActiveTab('opencode')}
            className={`flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-xs font-medium transition-all ${
              activeTab === 'opencode'
                ? 'bg-blue-600/90 text-white shadow-sm'
                : 'text-white/60 hover:text-white hover:bg-white/5'
            }`}
          >
            <Terminal className="w-3.5 h-3.5 text-emerald-300" />
            <span>OpenCode Пульт</span>
          </button>

          <button
            onClick={() => setActiveTab('monitor')}
            className={`flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-xs font-medium transition-all ${
              activeTab === 'monitor'
                ? 'bg-blue-600/90 text-white shadow-sm'
                : 'text-white/60 hover:text-white hover:bg-white/5'
            }`}
          >
            <Activity className="w-3.5 h-3.5" />
            <span>Монитор ПК</span>
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
        {/* ТАБ 1: OPENCODE ПУЛЬТ (ОСНОВНОЙ) */}
        {activeTab === 'opencode' && (
          <div className="flex flex-col gap-3">
            {/* Статус OpenCode и сессия */}
            <div className="flex items-center justify-between p-2.5 rounded-2xl bg-white/[0.03] border border-white/5">
              <div className="flex items-center gap-2">
                <span className={`w-2 h-2 rounded-full ${isOpenCodeConnected ? 'bg-emerald-400 animate-pulse' : 'bg-red-400'}`} />
                <div>
                  <div className="text-xs font-semibold text-white">
                    {isOpenCodeConnected ? 'Сервер OpenCode активен (127.0.0.1:28894)' : 'Сервер OpenCode не обнаружен'}
                  </div>
                  <div className="text-[11px] text-white/50">
                    Статус агента: {agentStatus.message}
                  </div>
                </div>
              </div>

              {/* Селектор модели */}
              {openCodeModels.length > 0 && (
                <div className="flex items-center gap-1.5">
                  <Sparkles className="w-3.5 h-3.5 text-blue-400" />
                  <select
                    value={selectedModelId || openCodeModels[0]?.id}
                    onChange={(e) => setSelectedModelId(e.target.value)}
                    className="bg-white/10 border border-white/10 text-white text-[11px] rounded-lg px-2 py-1 outline-none cursor-pointer"
                  >
                    {openCodeModels.map((m) => (
                      <option key={m.id} value={m.id} className="bg-neutral-900 text-white">
                        {m.name || m.id}
                      </option>
                    ))}
                  </select>
                </div>
              )}
            </div>

            {/* Выбор активной сессии проекта */}
            {openCodeSessions.length > 0 && (
              <div className="flex flex-col gap-1.5">
                <div className="flex items-center justify-between text-[11px] text-white/60 px-1">
                  <div className="flex items-center gap-1.5">
                    <Layers className="w-3 h-3 text-blue-400" />
                    <span>Активная сессия проекта:</span>
                  </div>
                  <span className="font-mono text-white/40">{openCodeSessions.length} сессий</span>
                </div>
                <div className="flex gap-2 overflow-x-auto pb-1">
                  {openCodeSessions.slice(0, 4).map((ses) => {
                    const isSelected = (selectedSessionId || openCodeSessions[0]?.id) === ses.id;
                    return (
                      <div
                        key={ses.id}
                        onClick={() => setSelectedSessionId(ses.id)}
                        className={`flex flex-col px-3 py-2 rounded-xl border text-xs cursor-pointer whitespace-nowrap transition-all ${
                          isSelected
                            ? 'bg-blue-600/20 border-blue-500/50 text-white'
                            : 'bg-white/[0.02] border-white/5 hover:bg-white/[0.05] text-white/70'
                        }`}
                      >
                        <div className="font-semibold truncate max-w-[170px]">{ses.title || 'Сессия'}</div>
                        <div className="text-[10px] text-white/40 truncate max-w-[170px]">
                          {ses.location?.directory?.split('\\').pop() || 'Workspace'}
                        </div>
                      </div>
                    );
                  })}
                </div>
              </div>
            )}

            {/* Поле ввода задачи / промпта для OpenCode */}
            <div className="flex flex-col gap-2">
              <div className="relative">
                <input
                  type="text"
                  value={promptText}
                  onChange={(e) => setPromptText(e.target.value)}
                  onKeyDown={(e) => e.key === 'Enter' && handleSendPrompt()}
                  placeholder="Поставить задачу OpenCode: например, запусти тесты или проверь git..."
                  className="w-full px-3.5 py-2.5 pr-10 text-xs rounded-xl bg-white/5 border border-white/10 text-white placeholder-white/40 focus:outline-none focus:border-blue-500/50 transition-colors"
                />
                <button
                  onClick={handleSendPrompt}
                  disabled={isSubmitting}
                  className="absolute right-2 top-2 p-1 rounded-lg text-emerald-400 hover:text-emerald-300 hover:bg-emerald-500/10 transition-colors disabled:opacity-50"
                >
                  {isSubmitting ? (
                    <Loader2 className="w-4 h-4 animate-spin text-blue-400" />
                  ) : (
                    <Send className="w-4 h-4" />
                  )}
                </button>
              </div>

              {lastResponse && (
                <div className="p-3 rounded-xl bg-emerald-950/30 border border-emerald-500/20 text-xs text-emerald-200 font-mono">
                  {lastResponse}
                </div>
              )}
            </div>

            {/* Быстрые действия для разработчика */}
            <div className="grid grid-cols-2 gap-2">
              <button
                onClick={() => setPromptText('Проверь текущий git статус и опиши измененные файлы')}
                className="flex items-center gap-2 p-2.5 rounded-xl bg-white/[0.02] hover:bg-white/[0.05] border border-white/5 text-xs text-white/80 transition-all text-left"
              >
                <FolderGit2 className="w-4 h-4 text-blue-400 shrink-0" />
                <span className="truncate">Git статус и изменения</span>
              </button>

              <button
                onClick={() => setPromptText('Запусти сборку и проверь наличие ошибок компиляции')}
                className="flex items-center gap-2 p-2.5 rounded-xl bg-white/[0.02] hover:bg-white/[0.05] border border-white/5 text-xs text-white/80 transition-all text-left"
              >
                <Play className="w-4 h-4 text-emerald-400 shrink-0" />
                <span className="truncate">Собрать и протестировать</span>
              </button>
            </div>
          </div>
        )}

        {/* ТАБ 2: МОНИТОР ПК */}
        {activeTab === 'monitor' && (
          <div className="grid grid-cols-2 gap-3">
            {/* CPU */}
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

            {/* RAM */}
            <div className="p-3 rounded-2xl bg-white/[0.03] border border-white/5 flex flex-col justify-between">
              <div className="flex items-center justify-between text-xs text-white/70">
                <div className="flex items-center gap-2">
                  <MemoryStick className="w-4 h-4 text-emerald-400" />
                  <span className="font-semibold text-white">Память</span>
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

            {/* Network */}
            <div className="p-3 rounded-2xl bg-white/[0.03] border border-white/5 flex items-center justify-between">
              <div className="flex items-center gap-2.5">
                <div className="p-2 rounded-xl bg-purple-500/10 text-purple-400">
                  <Wifi className="w-4 h-4" />
                </div>
                <div>
                  <div className="text-xs font-medium text-white/80">Трафик</div>
                  <div className="text-[11px] text-white/50">Сеть</div>
                </div>
              </div>
              <div className="text-right font-mono text-xs">
                <div className="text-emerald-400 font-semibold">↓ {formatNetSpeed(stats?.network?.rx_sec)}</div>
                <div className="text-blue-400">↑ {formatNetSpeed(stats?.network?.tx_sec)}</div>
              </div>
            </div>

            {/* Battery */}
            <div className="p-3 rounded-2xl bg-white/[0.03] border border-white/5 flex items-center justify-between">
              <div className="flex items-center gap-2.5">
                <div className="p-2 rounded-xl bg-amber-500/10 text-amber-400">
                  <Battery className="w-4 h-4" />
                </div>
                <div>
                  <div className="text-xs font-medium text-white/80">Питание</div>
                  <div className="text-[11px] text-white/50">
                    {stats?.battery?.hasBattery
                      ? stats.battery.isCharging ? 'Зарядка' : 'Батарея'
                      : 'AC Сеть'}
                  </div>
                </div>
              </div>
              <div className="text-right font-mono text-xs">
                {stats?.battery?.hasBattery ? (
                  <span className="text-base font-bold text-amber-300">{stats.battery.percent}%</span>
                ) : (
                  <span className="text-white/60">100%</span>
                )}
              </div>
            </div>
          </div>
        )}
      </div>
    </div>
  );
};
