import React, { useEffect } from 'react';
import { IslandContainer } from './components/island/IslandContainer';
import { useIslandStore } from './stores/useIslandStore';

export const App: React.FC = () => {
  const { setStats } = useIslandStore();

  useEffect(() => {
    if (!window.electronAPI) return;

    // Разовый запрос при старте
    window.electronAPI.getSystemStats().then((initialStats) => {
      setStats(initialStats);
    }).catch(console.error);

    // Подписка на регулярные обновления
    const unsubscribe = window.electronAPI.onSystemStats((updatedStats) => {
      setStats(updatedStats);
    });

    return () => {
      if (unsubscribe) unsubscribe();
    };
  }, [setStats]);

  return (
    <main className="w-screen h-screen flex flex-col items-center justify-start bg-transparent">
      <IslandContainer />
    </main>
  );
};

export default App;
