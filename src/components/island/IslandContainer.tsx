import React, { useEffect } from 'react';
import { motion, AnimatePresence } from 'framer-motion';
import { useIslandStore } from '../../stores/useIslandStore';
import { CompactIsland } from './CompactIsland';
import { ExpandedIsland } from './ExpandedIsland';

export const IslandContainer: React.FC = () => {
  const { mode, setMode, setIsHovered } = useIslandStore();

  // Синхронизация реальных размеров островка с Main-процессом Electron
  useEffect(() => {
    const width = mode === 'compact' ? 300 : 560;
    const height = mode === 'compact' ? 42 : 360;
    window.electronAPI?.updateIslandBounds({ width, height, y: 8 });
  }, [mode]);

  // Подписка на нативный Win32 трекинг наведения мыши
  useEffect(() => {
    const unsub = window.electronAPI?.onHoverChange((hovered) => {
      setIsHovered(hovered);
    });
    return () => {
      if (unsub) unsub();
    };
  }, [setIsHovered]);

  return (
    <div className="w-full flex justify-center pt-2 select-none">
      <motion.div
        layout
        initial={false}
        animate={{
          width: mode === 'compact' ? 300 : 560,
          height: mode === 'compact' ? 42 : 360,
          borderRadius: mode === 'compact' ? 9999 : 24,
        }}
        transition={{
          type: 'spring',
          stiffness: 450,
          damping: 32,
          mass: 0.8
        }}
        className="relative overflow-hidden pointer-events-auto island-glass shadow-island cursor-pointer flex flex-col items-center justify-center border border-white/20"
        style={{
          backgroundColor: '#0d0f12',
          boxShadow: mode === 'compact'
            ? '0 12px 30px -5px rgba(0, 0, 0, 0.9), 0 0 0 1px rgba(255, 255, 255, 0.2)'
            : '0 25px 50px -15px rgba(0, 0, 0, 0.95), 0 0 30px -5px rgba(59, 130, 246, 0.2), 0 0 0 1px rgba(255, 255, 255, 0.2)'
        }}
      >
        <AnimatePresence mode="wait">
          {mode === 'compact' ? (
            <motion.div
              key="compact"
              initial={{ opacity: 0, scale: 0.95 }}
              animate={{ opacity: 1, scale: 1 }}
              exit={{ opacity: 0, scale: 0.95 }}
              transition={{ duration: 0.15 }}
              className="w-full h-full"
            >
              <CompactIsland onExpand={() => setMode('expanded')} />
            </motion.div>
          ) : (
            <motion.div
              key="expanded"
              initial={{ opacity: 0, scale: 0.98 }}
              animate={{ opacity: 1, scale: 1 }}
              exit={{ opacity: 0, scale: 0.98 }}
              transition={{ duration: 0.2 }}
              className="w-full h-full"
            >
              <ExpandedIsland onCollapse={() => setMode('compact')} />
            </motion.div>
          )}
        </AnimatePresence>
      </motion.div>
    </div>
  );
};
