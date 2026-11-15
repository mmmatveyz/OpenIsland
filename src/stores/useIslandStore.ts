import { create } from 'zustand';
import { SystemStats } from '../../electron/preload';

export type IslandMode = 'compact' | 'expanded';
export type IslandTab = 'monitor' | 'ai' | 'opencode';
export type AIProvider = 'claude' | 'chatgpt' | 'gemini';

interface IslandStore {
  mode: IslandMode;
  activeTab: IslandTab;
  stats: SystemStats | null;
  selectedProvider: AIProvider;
  isSessionActive: Record<AIProvider, boolean>;
  isPinned: boolean;
  isHovered: boolean;

  setMode: (mode: IslandMode) => void;
  setActiveTab: (tab: IslandTab) => void;
  setStats: (stats: SystemStats) => void;
  setSelectedProvider: (provider: AIProvider) => void;
  setSessionActive: (provider: AIProvider, active: boolean) => void;
  togglePin: () => void;
  setIsHovered: (hovered: boolean) => void;
}

export const useIslandStore = create<IslandStore>((set) => ({
  mode: 'compact',
  activeTab: 'monitor',
  stats: null,
  selectedProvider: 'claude',
  isSessionActive: {
    claude: false,
    chatgpt: false,
    gemini: false
  },
  isPinned: false,
  isHovered: false,

  setMode: (mode) => set({ mode }),
  setActiveTab: (activeTab) => set({ activeTab }),
  setStats: (stats) => set({ stats }),
  setSelectedProvider: (selectedProvider) => set({ selectedProvider }),
  setSessionActive: (provider, active) =>
    set((state) => ({
      isSessionActive: { ...state.isSessionActive, [provider]: active }
    })),
  togglePin: () => set((state) => ({ isPinned: !state.isPinned })),
  setIsHovered: (isHovered) => set({ isHovered })
}));
