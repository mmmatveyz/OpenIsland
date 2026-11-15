import React, { useEffect } from 'react';
import { IslandContainer } from './components/island/IslandContainer';
import { useIslandStore } from './stores/useIslandStore';

export const App: React.FC = () => {
  const { 
    setStats, 
    setOpenCodeConnected, 
    setOpenCodeSessions, 
    setOpenCodeModels,
    setAgentStatus 
  } = useIslandStore();

  useEffect(() => {
    if (!window.electronAPI) return;

    // Разовый опрос железа ПК
    window.electronAPI.getSystemStats().then((initialStats) => {
      setStats(initialStats);
    }).catch(console.error);

    // Подписка на обновление метрик
    const unsubStats = window.electronAPI.onSystemStats((updatedStats) => {
      setStats(updatedStats);
    });

    // Опрос OpenCode сервиса
    const initOpenCode = async () => {
      try {
        const info = await window.electronAPI.getOpenCodeInfo();
        setOpenCodeConnected(!!info);

        if (info) {
          const [sessions, models] = await Promise.all([
            window.electronAPI.getOpenCodeSessions(),
            window.electronAPI.getOpenCodeModels()
          ]);
          setOpenCodeSessions(sessions);
          setOpenCodeModels(models);
        }
      } catch (e) {
        console.error('Failed to init OpenCode:', e);
      }
    };

    initOpenCode();
    const interval = setInterval(initOpenCode, 5000);

    const unsubStatus = window.electronAPI.onOpenCodeStatus((status: any) => {
      setAgentStatus(status);
    });

    return () => {
      if (unsubStats) unsubStats();
      if (unsubStatus) unsubStatus();
      clearInterval(interval);
    };
  }, [setStats, setOpenCodeConnected, setOpenCodeSessions, setOpenCodeModels, setAgentStatus]);

  return (
    <main className="w-screen h-screen flex flex-col items-center justify-start bg-transparent">
      <IslandContainer />
    </main>
  );
};

export default App;
