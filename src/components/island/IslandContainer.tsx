import React, { useEffect, useRef } from 'react';
import { motion, AnimatePresence } from 'framer-motion';
import { useIslandStore } from '../../stores/useIslandStore';
import { CompactIsland } from './CompactIsland';
import { ExpandedIsland } from './ExpandedIsland';

export const IslandContainer: React.FC = () => {
  const { mode, setMode, isPinned, setIsHovered } = useIslandStore();
  const hoverTimeoutRef = useRef<NodeJS.Timeout | null>(null);

  // Обработка наведения мыши для сквозных кликов (Click-Through)
  const handleMouseEnter = () => {
    if (hoverTimeoutRef.current) {
      clearTimeout(hoverTimeoutRef.current);
      hoverTimeoutRef.current = null;
    }
    setIsHovered(true);
    // Отключаем сквозной клик: окно становится интерактивным
    window.electronAPI?.setIgnoreMouseEvents(false);
  };

  const handleMouseLeave = () => {
    setIsHovered(false);

    hoverTimeoutRef.current = setTimeout(() => {
      // Если окно не закреплено и свернуто, включаем сквозной клик
      if (mode === 'compact') {
        window.electronAPI?.setIgnoreMouseEvents(true);
      } else if (!isPinned) {
        // Если открыто, но не закреплено, сворачиваем при уходе курсора
        setMode('compact');
        window.electronAPI?.setIgnoreMouseEvents(true);
      }
    }, 250);
  };

  // Слушаем изменения режима
  useEffect(() => {
    if (mode === 'expanded') {
      window.electronAPI?.setMonitoringActive(true);
      window.electronAPI?.setIgnoreMouseEvents(false);
    } else {
      window.electronAPI?.setMonitoringActive(false);
    }
  }, [mode]);

  return (
    <div className="w-full flex justify-center pt-2 select-none">
      <motion.div
        layout
        onMouseEnter={handleMouseEnter}
        onMouseLeave={handleMouseLeave}
        initial={false}
        animate={{
          width: mode === 'compact' ? 290 : 560,
          height: mode === 'compact' ? 40 : 360,
          borderRadius: mode === 'compact' ? 9999 : 24,
        }}
        transition={{
          type: 'spring',
          stiffness: 420,
          damping: 30,
          mass: 0.8
        }}
        className="relative overflow-hidden island-glass shadow-island cursor-default flex flex-col items-center justify-center border border-white/10"
        style={{
          boxShadow: mode === 'compact'
            ? '0 10px 30px -10px rgba(0, 0, 0, 0.8), 0 0 0 1px rgba(255, 255, 255, 0.1)'
            : '0 25px 50px -15px rgba(0, 0, 0, 0.9), 0 0 30px -5px rgba(59, 130, 246, 0.15), 0 0 0 1px rgba(255, 255, 255, 0.15)'
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
