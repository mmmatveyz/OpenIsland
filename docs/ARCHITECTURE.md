# Архитектура OpenIsland

Документ описывает техническое устройство системы, взаимодействие процессов Electron, оконный менеджмент и протоколы передачи данных.

---

## 1. Общая схема системы

```
+-------------------------------------------------------------------------+
|                              WINDOWS OS                                 |
+-------------------------------------------------------------------------+
       │                                                      │
       ▼ (Hardware stats, processes)                          ▼ (Window events)
+───────────────────────────────────+         +───────────────────────────+
|      SystemInformation / Win32    |         |    DWM / Windows API      |
+───────────────────────────────────+         +───────────────────────────+
       │                                                      │
       └─────────────────────────┬────────────────────────────┘
                                 ▼
+─────────────────────────────────────────────────────────────────────────+
|                       ELECTRON MAIN PROCESS                             |
|                                                                         |
|  - WindowManager: прозрачное безрамочное TopMost окно                   |
|  - HitTest Manager: dynamic ignoreMouseEvents (сквозные клики)          |
|  - SystemMonitorService: опрос CPU/RAM/GPU/сети в фоне                  |
|  - AISessionManager: персистентные Chromium сессии для Claude/GPT/Gemini|
|  - SafeStorage: шифрование токенов и cookies                            |
+─────────────────────────────────────────────────────────────────────────+
                                 │
                   (IPC: contextBridge / preload)
                                 ▼
+─────────────────────────────────────────────────────────────────────────+
|                    RENDERER PROCESS (React + Vite)                      |
|                                                                         |
|  - Framer Motion: пружинные анимации капсулы (compact -> expanded)     |
|  - IslandContainer: ядро острова (позиция, размеры, morphing)           |
|  - Модули виджетов:                                                     |
|      • PC Stats Widget (CPU, RAM, GPU, Net)                             |
|      • AI Assistant Widget (Prompt, Chat, Sessions)                     |
|      • Media / Dev Process Tracker                                      |
|  - Zustand Stores: локальное реактивное состояние UI                    |
+─────────────────────────────────────────────────────────────────────────+
```

---

## 2. Реализация плавающего окна (Window Manager)

Для создания эффекта «Dynamic Island» поверх любого открытого полноэкранного окна или игры применяются следующие параметры Electron `BrowserWindow`:

```typescript
const islandWindow = new BrowserWindow({
  width: screenWidth,       // Позволяет острову расширяться без обрезки
  height: 400,
  x: 0,
  y: 0,
  transparent: true,        // Прозрачный фон
  frame: false,             // Без рамки Windows
  alwaysOnTop: true,        // Поверх всех окон
  skipTaskbar: true,        // Скрыто из панели задач
  focusable: true,          // Позволяет вводить промпты
  hasShadow: false,         // Отключаем нативную тень Windows, рисуем свою
  webPreferences: {
    preload: path.join(__dirname, 'preload.js'),
    contextIsolation: true,
    nodeIntegration: false
  }
});
```

### 2.1. Решение проблемы сквозных кликов (Click-through)
Так как окно покрывает верхнюю часть экрана, клики по свободным прозрачным участкам вокруг острова должны проходить в окна под ним.
- Когда курсор **вне** островка: `islandWindow.setIgnoreMouseEvents(true, { forward: true })`.
- Когда курсор **наводится** на активную зону островка: `islandWindow.setIgnoreMouseEvents(false)`.
- Реализуется через отправку IPC-событий `island:mouse-enter` и `island:mouse-leave` из Renderer.

---

## 3. Мониторинг ПК (System Monitor Service)

Служба мониторинга работает в Main процессе и опрашивает параметры системы с адаптивным интервалом:

1. **CPU**: общая загрузка, температура, загрузка отдельных ядер (`si.currentLoad()`, `si.cpuTemperature()`).
2. **RAM**: занятая/свободная оперативная память (`si.mem()`).
3. **GPU**: загрузка графического процессора, видеопамять (`si.graphics()`).
4. **Сеть**: входящая/исходящая скорость передачи (`si.networkStats()`).
5. **Батарея / Питание**: процент заряда, статус зарядки (`si.battery()`).
6. **Dev-процессы**: мониторинг открытых локальных портов (Node, Python, Docker, Go).

---

## 4. Архитектура взаимодействия с ИИ

Подробно описана в документе [`docs/AI_SESSION_AUTH.md`](./AI_SESSION_AUTH.md).
Кратко:
- Поддерживается два режима:
  1. **Session Auth (Браузерная сессия подписки)**: использование аккаунтов Claude Pro, ChatGPT Plus, Gemini Advanced через авторизованный WebView-контекст.
  2. **Direct API**: указание персональных API-ключей (OpenAI, Anthropic, Google AI Studio, OpenRouter, локальная Ollama).
