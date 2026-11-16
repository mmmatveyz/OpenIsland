# AGENTS.md — Инструкция и контекст для ИИ-агентов проекта OpenIsland

Добро пожаловать в проект **OpenIsland**! Этот файл является главным справочником и набором правил для любых ИИ-агентов (Antigravity, Claude, ChatGPT, Cursor, Copilot и др.), работающих с этим репозиторием.

---

## 1. Миссия проекта

**OpenIsland** — это нативное/десктопное приложение для Windows 10/11, объединяющее:
1. **Dynamic Island UI**: плавающий, ненавязчивый адаптивный «островок» вверху экрана с плавной физикой анимаций (Apple-like).
2. **Пульт управления и виджеты**: медиаплеер с живым спектром, таймер, мини-эквалайзер на любой системный звук, звук и тост завершения задачи агента, история буфера обмена, мониторинг ПК (CPU/RAM/GPU/сеть/температура), индикаторы системы (громкость, заряд, наушники, сеть).
3. **AI Hub (OpenCode)**: быстрый доступ к локальному агенту OpenCode прямо из островка — сессии, модели, отправка задач, живой стриминг ответа и подтверждение разрешений.

---

## 2. Архитектура и стек технологий

| Компонент | Технология | Обоснование |
| :--- | :--- | :--- |
| **Десктопное ядро и UI** | **C# WPF (.NET 8, `net8.0-windows10.0.19041.0`)** | Нативное оверлейное окно поверх всех окон, пружинная физика, ~150 МБ приватной памяти вместо Chromium-процессов и аппаратное ускорение Direct3D. |
| **Отрисовка островка** | WPF + `DrawingVisual`, Windows.UI.Composition | Стекло, блики, тени, спектр и кастомные виджеты без тяжёлых сторонних UI-библиотек. |
| **Интеграция с ИИ** | **OpenCodeService (REST + SSE)** | Сессии, модели, промпты, статусы, события `session.execution.*`, `session.text.*`, `permission.*` в реальном времени. |
| **Авторизация в OpenCode** | **Автоматический пейринг (`opencode pair`)** + DPAPI | Токен получается без ручного ввода и хранится зашифрованным для текущего пользователя. |
| **Настройки** | Реестр `HKCU\Software\OpenIsland` (со старым ключом `Software\DynamicIsland` — авто-миграция при первом чтении) | Простое персистентное хранение без внешних файлов; автозапуск — `HKCU\...\CurrentVersion\Run` (значение `OpenIsland`). |
| **Обновления** | GitHub Releases (`mmmatveyz/OpenIsland`) | Проверка `releases/latest`, скачивание ассета `OpenIsland.exe` и сверка `sha256`-дайджеста. |

---

## 3. Структура проекта

```text
OpenIsland/
├── AGENTS.md                      # Данный файл: правила и гайды для ИИ-агентов
├── README.md                      # Главный обзор проекта для разработчиков
├── OpenIsland.sln                 # Решение .NET (desktop/OpenIsland)
├── docs/                          # Детальная архитектурная документация
│   ├── ARCHITECTURE.md            # Устройство окна, рендеринга, сервисов и OpenCode
│   ├── ROADMAP.md                 # План развития и текущий статус фаз
│   ├── PRODUCT.md                 # Карта функций: что откуда, идентичность, план
│   └── AI_SESSION_AUTH.md         # Отложенный дизайн авторизации по подпискам
├── desktop/OpenIsland/            # ОСНОВНОЕ приложение (C# WPF)
│   ├── MainWindow.xaml            # Разметка островка, панелей и карточек
│   ├── MainWindow.xaml.cs         # Окно, View/Panel-роутинг, форма капсулы
│   ├── MainWindow.Shape.cs        # Пружинная геометрия, стекло, морфинг
│   ├── MainWindow.Pointer.cs      # Мышь, drag, hover, контекстное меню
│   ├── MainWindow.Media.cs        # Плеер, эквалайзер, обложки
│   ├── MainWindow.Timer.cs        # Таймер
│   ├── MainWindow.Menu.cs         # Меню и настройки
│   ├── MainWindow.Status.cs       # Громкость, заряд, наушники, сеть
│   ├── MainWindow.OpenCode.cs     # UI OpenCode AI Hub, звук и тост завершения задачи
│   ├── MainWindow.Perf.cs         # Карточка мониторинга ПК
│   ├── MainWindow.Clipboard.cs    # Карточка истории буфера обмена
│   ├── OpenCodeService.cs         # REST/SSE клиент OpenCode, пейринг, DPAPI-токен
│   ├── OpenCodeSelfTest.cs        # Диагностический прогон (--selftest)
│   ├── PerfService.cs             # CPU/RAM/сеть/GPU/температура (опрос по требованию)
│   ├── ClipboardService.cs        # История буфера обмена (только в памяти)
│   ├── Chime.cs                   # Звук завершения задачи (staplebops-01, MIT)
│   ├── Settings.cs                # Настройки в реестре
│   ├── Native.cs                  # Win32: topmost, toolwindow, DWM, клики
│   ├── MediaService.cs            # Интеграция с системным медиаплеером (GSMTC)
│   ├── AudioService.cs            # Громкость и пик вывода (IAudioMeterInformation)
│   ├── SpectrumService.cs         # Захват системного звука (WASAPI loopback)
│   ├── Updater.cs                 # Обновления через GitHub Releases (mmmatveyz/OpenIsland)
│   ├── Autostart.cs               # Автозапуск с Windows
│   ├── Fonts/                     # Inter + Phosphor Fill (встроенные ресурсы)
│   ├── Gadgets/                   # PNG-графика виджетов
│   ├── Assets/                    # Иконка приложения и звук завершения задачи
│   └── OpenIsland.csproj
├── installer/OpenIsland.iss       # Скрипт Inno Setup (per-user установщик)
├── scripts/build.ps1              # Сборка, запуск, self-test и установщик
├── tests/OpenIsland.Tests/        # Юнит-тесты (xUnit): логика без UI
├── bin/OpenIsland/                # Результат сборки (OpenIsland.exe)
├── Запуск_OpenIsland.bat          # Запуск в один клик (собирает при необходимости)
└── .dotnet/                       # Локально установленный SDK (в git не попадает)
```

---

## 4. Правила и соглашения для ИИ-агентов (Guidelines)

### 4.0. Дисциплина разработки
- **Надёжный Win32-трекинг окна и мыши**: островок должен корректно переживать смену DPI, полноэкранные приложения и перезапуск Explorer; дисциплина по потреблению RAM и размеру сборки, отсутствие блокировок UI-потока.
- **Плавная пружинная физика**: никаких дёрганий макета при трансформации капсулы, аккуратная типографика и отступы.
- **Проверка nullable-контрактов** и обработка edge cases (демон OpenCode недоступен, порт сменился, сессия удалена, токен истёк).

### 4.1. Безопасность и хранение данных
- **Никаких открытых токенов и паролей в кодовой базе и логах.** Токен OpenCode хранится только через Windows DPAPI: `%LOCALAPPDATA%\OpenIsland\opencode-token.bin` (`ProtectedData`, `CurrentUser`).
- Старый plaintext-файл `opencode-token.txt` мигрируется и удаляется автоматически; новый plaintext-формат не вводить.
- В лог (`%LOCALAPPDATA%\OpenIsland\logs\app.log`) нельзя писать токены, cookies, содержимое приватных сообщений и полные ответы API с секретами.
- Внешние ссылки и команды не выполняются по данным из ответов API без явной проверки.

### 4.2. Производительность (Zero UI Lag)
- Островок находится на экране постоянно — фоновое потребление CPU должно стремиться к 0–1%.
- Сетевые опросы и интеграции не блокируют UI-поток: только `async`/`Task`, обновление UI через `Dispatcher`.
- OpenCode: события приходят через SSE (`/api/event`), поллинг `/api/session` используется как fallback с интервалом ~2 с. Частые обновления UI схлопываются (throttle).
- Тяжёлые визуальные эффекты (спектр, частицы, стекло) не должны тикать, когда островок скрыт, сложен или не виден.
- Анимации трансформации размера выполняются через пружины и GPU-трансформации, без пересоздания визуальных деревьев на каждом кадре.

### 4.3. Оформление кода
- C# с включённым `Nullable`; избегать `null!`, подавления предупреждений без причины и пустых `catch` (логировать через `App.Log` / `App.LogDebug`).
- Модульность через `partial class MainWindow` и файлы `MainWindow.<Модуль>.cs`: новый виджет = новый `View` + `Panel` (при необходимости) + XAML-карточка + обработчики в отдельном файле.
- Не ломать существующие формы `PillShapes` и роутинг `TargetView()`; новые размеры добавлять точечно.
- Соблюдать существующий стиль XAML: `StaticResource`-стили (`ChipButton`, `RowButton`, `BackButton`, `Caption`), единые отступы и скругления.
- Не коммитить сборки и артефакты: `bin/`, `obj/`, `.dotnet/`, `*.exe` вне исходников.

---

## 5. Сборка и проверка

```powershell
# Сборка Release в bin\OpenIsland
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\build.ps1

# Сборка + запуск
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\build.ps1 -Run

# Диагностика OpenCode (создаёт временную сессию, шлёт промпт, ждёт ответ)
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\build.ps1 -SelfTest

# Сборка установщика Inno Setup в bin\installer-out (нужен Inno Setup 6)
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\build.ps1 -Installer

# Юнит-тесты (xUnit): разбор ответов OpenCode, версии/changelog апдейтера,
# Countdown, история буфера обмена, инварианты настроек
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\build.ps1 -Test
```

- Сборка должна проходить с **0 ошибок и 0 предупреждений**.
- Юнит-тесты должны проходить; в CI они гоняются с `-warnaserror` на каждый push/PR.
- Перед пересборкой скрипт останавливает запущенный `OpenIsland.exe` (флаг `-NoStop` отключает это).
- Если .NET SDK отсутствует, скрипт ставит его локально в `.dotnet/`.
- Установщик собирается per-user (`%LOCALAPPDATA%\Programs\OpenIsland`), чтобы встроенный апдейтер мог подменять exe без UAC; `.iss` валидируется в CI на каждый push.
- После изменений, затрагивающих OpenCode, полезно прогнать `--selftest` и посмотреть `%LOCALAPPDATA%\OpenIsland\logs\app.log`.

---

## 6. Документирование изменений
- При добавлении новых фич, изменении архитектуры или интеграций обязательно обновлять соответствующие файлы в `docs/`.
- Поддерживать `README.md`, `AGENTS.md` и `docs/ROADMAP.md` в актуальном состоянии.
- Явно помечать отложенные/нереализованные возможности как «план» или «легаси», чтобы не выдавать их за готовые.
