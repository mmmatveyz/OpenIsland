import { create } from 'zustand';
import { SystemStats } from '../../electron/preload';
import { OpenCodeSession, OpenCodeModel } from '../../electron/ipc/opencode';

export type IslandMode = 'compact' | 'expanded';
export type IslandTab = 'opencode' | 'monitor' | 'dev';

interface IslandStore {
  mode: IslandMode;
  activeTab: IslandTab;
  stats: SystemStats | null;
  isPinned: boolean;
  isHovered: boolean;

  // OpenCode State
  isOpenCodeConnected: boolean;
  openCodeSessions: OpenCodeSession[];
  openCodeModels: OpenCodeModel[];
  selectedSessionId: string | null;
  selectedModelId: string | null;
  agentStatus: {
    status: 'idle' | 'busy' | 'error';
    message: string;
  };

  setMode: (mode: IslandMode) => void;
  setActiveTab: (tab: IslandTab) => void;
  setStats: (stats: SystemStats) => void;
  togglePin: () => void;
  setIsHovered: (hovered: boolean) => void;

  setOpenCodeConnected: (connected: boolean) => void;
  setOpenCodeSessions: (sessions: OpenCodeSession[]) => void;
  setOpenCodeModels: (models: OpenCodeModel[]) => void;
  setSelectedSessionId: (id: string | null) => void;
  setSelectedModelId: (id: string | null) => void;
  setAgentStatus: (status: { status: 'idle' | 'busy' | 'error'; message: string }) => void;
}

export const useIslandStore = create<IslandStore>((set) => ({
  mode: 'compact',
  activeTab: 'opencode', // OpenCode — главный экран!
  stats: null,
  isPinned: false,
  isHovered: false,

  isOpenCodeConnected: false,
  openCodeSessions: [],
  openCodeModels: [],
  selectedSessionId: null,
  selectedModelId: null,
  agentStatus: {
    status: 'idle',
    message: 'Готов к работе'
  },

  setMode: (mode) => set({ mode }),
  setActiveTab: (activeTab) => set({ activeTab }),
  setStats: (stats) => set({ stats }),
  togglePin: () => set((state) => ({ isPinned: !state.isPinned })),
  setIsHovered: (isHovered) => set({ isHovered }),

  setOpenCodeConnected: (isOpenCodeConnected) => set({ isOpenCodeConnected }),
  setOpenCodeSessions: (openCodeSessions) => set({ openCodeSessions }),
  setOpenCodeModels: (openCodeModels) => set({ openCodeModels }),
  setSelectedSessionId: (selectedSessionId) => set({ selectedSessionId }),
  setSelectedModelId: (selectedModelId) => set({ selectedModelId }),
  setAgentStatus: (agentStatus) => set({ agentStatus })
}));
