import React, { useEffect } from 'react';
import { motion, AnimatePresence } from 'framer-motion';
import { useIslandStore } from '../../stores/useIslandStore';
import { CompactIsland } from './CompactIsland';
import { ExpandedIsland } from './ExpandedIsland';

export const IslandContainer: React.FC = () => {
  const { mode, setMode } = useIslandStore();

  // Ресайз нативного окна Electron по референсу DynamicIslandForWindows
  useEffect(() => {
    window.electronAPI?.resizeIsland(mode);
  }, [mode]);

  useEffect(() => {
    const unsub = window.electronAPI?.onToggleMode?.(() => {
      setMode(mode === 'compact' ? 'expanded' : 'compact');
    });
    return () => {
      if (unsub) unsub();
    };
  }, [mode, setMode]);

  return (
    <div className="w-full h-full flex items-center justify-center select-none overflow-hidden p-0.5">
      <motion.div
        layout
        initial={false}
        animate={{
          width: '100%',
          height: '100%',
          borderRadius: mode === 'compact' ? 9999 : 24,
        }}
        transition={{
          type: 'spring',
          stiffness: 450,
          damping: 32,
          mass: 0.8
        }}
        className="w-full h-full relative overflow-hidden island-glass shadow-island cursor-pointer flex flex-col items-center justify-center border border-white/20"
        style={{
          backgroundColor: '#0d0f12',
          boxShadow: mode === 'compact'
            ? '0 10px 25px -5px rgba(0, 0, 0, 0.9), 0 0 0 1px rgba(255, 255, 255, 0.2)'
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
              className="w-full h-full flex items-center"
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
