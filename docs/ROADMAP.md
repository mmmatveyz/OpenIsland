# Roadmap проекта OpenIsland

План разработки разбит на 5 ключевых фаз от фундамента до полноценного релиза.

---

## 📌 Фаза 1: Фундамент и Островной UI (Foundation & Dynamic Island Core)
- [x] Инициализация проекта: Electron + Vite + React + TypeScript + TailwindCSS.
- [x] Настройка прозрачного безрамочного окна `BrowserWindow` поверх всех окон (`alwaysOnTop`).
- [x] Реализация механизма сквозных кликов (`ignoreMouseEvents` при выходе курсора из активной области островка).
- [x] Базовые состояния островка с пружинными анимациями (Framer Motion):
  - [x] **Свернутое (Compact/Capsule)**: маленькая аккуратная черная «таблетка» по центру экрана.
  - [x] **Компактно-активное (Active/Pill)**: появление мини-иконок и индикаторов CPU/RAM/ИИ.
  - [x] **Развернутое (Expanded Panel)**: всплывающая карточка управления при ховере/клике.

---

## 📌 Фаза 2: Интеграция с OpenCode (OpenCode Engine & Agent Bridge)
- [x] Автоматическое динамическое обнаружение порта демона OpenCode (`opencode service status`).
- [x] Интеграция REST API OpenCode в Electron Main процесс (`electron/ipc/opencode.ts`).
- [x] Получение списка активных сессий проекта (`/api/session`).
- [x] Получение списка доступных моделей (`/api/model` — DeepSeek V4.1 Flash, GLM 5.3 Flash и др.).
- [x] Отправка промптов и задач агенту OpenCode прямо из развернутого островка.
- [x] Отображение статуса активности агента (Думает / Готов) в свернутой капсуле Dynamic Island.

## 📌 Фаза 3: Каталог ИИ-экспертов (Agency Agents Integration)
- [x] Подключение репозитория `agency-agents` (`tools/agency-agents`) для разработки проекта (Desktop App Engineer, Frontend Dev, Code Reviewer).
- [ ] Встраивание каталога персон в UI развернутого островка:
  - Выбор специализации перед отправкой промпта (`Frontend Wizard`, `Code Reviewer`, `Security Auditor`, `DevOps`).
  - Автоматическая передача системного промпта эксперта в задачу OpenCode.

## 📌 Фаза 4: Пульт мониторинга ПК (System Hardware Monitor)
- [x] Интеграция библиотеки `systeminformation` в Main процесс.
- [x] IPC-канал передачи метрик в реальном времени (CPU %, RAM %, сеть, питание).
- [x] Нативный Win32-трекинг курсора (`screen.getCursorScreenPoint`) для идеального сквозного клика (Click-Through).

## 📌 Фаза 5: Расширенные возможности OpenCode & Автоматизация
- [ ] Подключение стриминга событий OpenCode через SSE (`/api/event`) для живого отображения логов в островке.
- [ ] Одобрение прав и действий агента (`/api/session/{id}/permission/reply`) прямо с капсулы островка.
- [ ] Просмотр диффов изменений (`/api/session/{id}/diff`).
- [ ] Глобальный хоткей (`Alt + Space` или `Win + I`) для мгновенного фокуса на острове.

## 📌 Фаза 6 (Отложено / Future): Авторизация по персональным подпискам
- [ ] Отдельный изолированный модуль `AISessionManager` (Claude Pro, ChatGPT Plus, Gemini Advanced).
- [ ] Проксирование локальных инструментов в веб-сессию для запуска агентского цикла без официальных API-ключей.

## 📌 Фаза 7: Кастомизация и релиз
- [ ] Настройки внешнего вида и тем оформления (Fluent / Mica / Acrylic).
- [ ] Автозапуск с Windows (Windows Startup Service).
- [ ] Сборка NSIS инсталлятора через `electron-builder`.
