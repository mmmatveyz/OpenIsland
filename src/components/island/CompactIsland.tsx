import React from 'react';
import { Cpu, MemoryStick, Sparkles } from 'lucide-react';
import { useIslandStore } from '../../stores/useIslandStore';

interface CompactIslandProps {
  onExpand: () => void;
}

export const CompactIsland: React.FC<CompactIslandProps> = ({ onExpand }) => {
  const { stats, selectedProvider } = useIslandStore();

  const cpuUsage = stats?.cpu.usage ?? 0;
  const ramUsage = stats?.memory.usagePercentage ?? 0;

  // Цвет индикатора CPU
  const getCpuColor = (usage: number) => {
    if (usage > 80) return 'text-red-400 bg-red-400/20';
    if (usage > 50) return 'text-amber-400 bg-amber-400/20';
    return 'text-emerald-400 bg-emerald-400/20';
  };

  const providerNames: Record<string, string> = {
    claude: 'Claude Pro',
    chatgpt: 'ChatGPT Plus',
    gemini: 'Gemini Adv'
  };

  return (
    <div
      onClick={onExpand}
      className="flex items-center justify-between w-full h-full px-4 text-xs font-medium text-white/90 cursor-pointer select-none"
    >
      {/* Левая секция: CPU */}
      <div className="flex items-center gap-2">
        <span className={`w-2 h-2 rounded-full animate-pulse ${getCpuColor(cpuUsage).split(' ')[1]}`}>
          <span className={`block w-2 h-2 rounded-full ${getCpuColor(cpuUsage).split(' ')[0]} bg-current`} />
        </span>
        <div className="flex items-center gap-1 text-white/70">
          <Cpu className="w-3.5 h-3.5 text-white/50" />
          <span>{cpuUsage}%</span>
        </div>
      </div>

      {/* Центральная секция: ИИ статус */}
      <div className="flex items-center gap-1.5 px-2.5 py-0.5 rounded-full bg-white/5 border border-white/10 text-white/80 hover:bg-white/10 transition-colors">
        <Sparkles className="w-3 h-3 text-blue-400" />
        <span className="font-semibold tracking-wide text-[11px] text-blue-200">
          {providerNames[selectedProvider] || 'OpenIsland'}
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
