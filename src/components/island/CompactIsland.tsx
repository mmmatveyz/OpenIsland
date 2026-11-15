import React from 'react';
import { Cpu, MemoryStick, Terminal, Loader2 } from 'lucide-react';
import { useIslandStore } from '../../stores/useIslandStore';

interface CompactIslandProps {
  onExpand: () => void;
}

export const CompactIsland: React.FC<CompactIslandProps> = ({ onExpand }) => {
  const { stats, isOpenCodeConnected, agentStatus } = useIslandStore();

  const cpuUsage = stats?.cpu.usage ?? 0;
  const ramUsage = stats?.memory.usagePercentage ?? 0;

  const getCpuColor = (usage: number) => {
    if (usage > 80) return 'text-red-400 bg-red-400/20';
    if (usage > 50) return 'text-amber-400 bg-amber-400/20';
    return 'text-emerald-400 bg-emerald-400/20';
  };

  const isBusy = agentStatus.status === 'busy';

  return (
    <div
      onClick={onExpand}
      className="flex items-center justify-between w-full h-full px-4 text-xs font-medium text-white/90 cursor-pointer select-none"
    >
      {/* Левая секция: CPU */}
      <div className="flex items-center gap-2">
        <span className={`w-2 h-2 rounded-full ${getCpuColor(cpuUsage).split(' ')[1]}`}>
          <span className={`block w-2 h-2 rounded-full ${getCpuColor(cpuUsage).split(' ')[0]} bg-current`} />
        </span>
        <div className="flex items-center gap-1 text-white/70">
          <Cpu className="w-3.5 h-3.5 text-white/50" />
          <span>{cpuUsage}%</span>
        </div>
      </div>

      {/* Центральная секция: OpenCode статус */}
      <div className={`flex items-center gap-1.5 px-2.5 py-0.5 rounded-full border transition-all ${
        isBusy
          ? 'bg-blue-600/30 border-blue-400/50 text-blue-200'
          : isOpenCodeConnected
            ? 'bg-white/5 border-white/10 text-white/90 hover:bg-white/10'
            : 'bg-amber-500/10 border-amber-500/20 text-amber-200'
      }`}>
        {isBusy ? (
          <Loader2 className="w-3 h-3 text-blue-400 animate-spin" />
        ) : (
          <Terminal className="w-3 h-3 text-emerald-400" />
        )}
        <span className="font-semibold tracking-wide text-[11px]">
          {isBusy ? 'OpenCode: В работе...' : isOpenCodeConnected ? 'OpenCode' : 'OpenCode (Оффлайн)'}
        </span>
      </div>

      {/* Правая секция: RAM */}
      <div className="flex items-center gap-1 text-white/70">
        <MemoryStick className="w-3.5 h-3.5 text-white/50" />
        <span>{ramUsage}%</span>
      </div>
    </div>
  );
};
