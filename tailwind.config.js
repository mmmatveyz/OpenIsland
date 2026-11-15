/** @type {import('tailwindcss').Config} */
module.exports = {
  content: [
    "./index.html",
    "./src/**/*.{js,ts,jsx,tsx}",
  ],
  theme: {
    extend: {
      colors: {
        island: {
          dark: '#0c0d0e',
          surface: '#16181a',
          border: 'rgba(255, 255, 255, 0.08)',
          hover: 'rgba(255, 255, 255, 0.05)',
          accent: '#3b82f6',
          green: '#10b981',
          orange: '#f59e0b',
          purple: '#8b5cf6'
        }
      },
      boxShadow: {
        'island': '0 20px 40px -15px rgba(0, 0, 0, 0.7), 0 0 0 1px rgba(255, 255, 255, 0.1)',
        'island-glow': '0 0 30px -5px rgba(59, 130, 246, 0.25), 0 20px 40px -15px rgba(0, 0, 0, 0.8), 0 0 0 1px rgba(255, 255, 255, 0.15)'
      }
    },
  },
  plugins: [],
};
