# 🏝️ OpenIsland

> **Dynamic Island для Windows + OpenCode AI Hub**: нативный плавающий островок на C# WPF с пружинными анимациями, плеером, таймером, виджетами и управлением ИИ-агентом OpenCode прямо с капсулы.

---

## 💡 О проекте

**OpenIsland** переносит концепцию Apple Dynamic Island на Windows 10/11 и превращает её в рабочий инструмент:

1. **Dynamic Island UI**
   - Плавающая капсула по центру верхней кромки экрана, поверх всех окон.
   - Нативная пружинная физика (spring physics) расширения и морфинга, стеклянные блики, тени и адаптивная геометрия.
   - Сквозные клики: прозрачные области окна не перехватывают мышь.
   - Скрытие в полноэкранных приложениях и играх.

2. **Виджеты повседневного использования**
   - Плеер: обложка, прогресс, живой спектр (реальный вывод звука через WASAPI loopback), управление медиа.
   - Таймер и секундомер.
   - Мини-эквалайзер в свёрнутом островке: реагирует на **любой** системный звук, даже когда активной медиасессии нет (например, видео в браузере).
   - **Мониторинг ПК**: CPU, память, видеокарта, сеть и температура — только пока карточка открыта, чтобы остров не нагружал систему в фоне.
   - **История буфера обмена**: последние 30 фрагментов, вставка в один клик; хранится только в памяти.
   - Индикаторы громкости, заряда, наушников, сети; настройки внешнего вида (масштаб, отступ, стекло, эффекты).

3. **OpenCode AI Hub**
   - Обнаружение локального демона OpenCode и автоматический пейринг.
   - Список сессий и моделей, переключение сессии/модели прямо из островка.
   - Отправка задачи агенту и живой стриминг ответа (SSE).
   - Звук завершения задачи — тот же `staplebops-01`, что играет сам OpenCode (MIT), плюс тост-уведомление при завершении длинной задачи.
   - Запросы разрешений агента (`allow` / `deny`) без переключения в OpenCode Desktop.
   - Глобальный хоткей **Ctrl+Alt+O** — мгновенно открыть или свернуть карточку OpenCode из любого приложения.

---

## 🛠️ Стек

| Компонент | Технология |
| :--- | :--- |
| Платформа | **C# WPF, .NET 8 (`net8.0-windows10.0.19041.0`)** |
| Рендеринг | Direct3D через WPF, Windows.UI.Composition (acrylic-стекло), кастомные `DrawingVisual`-виджеты |
| Интеграция с ИИ | REST + SSE клиент локального демона OpenCode (`OpenCodeService.cs`) |
| Хранение токена | Windows DPAPI (`ProtectedData`, `%LOCALAPPDATA%\OpenIsland\opencode-token.bin`) |
| Настройки | Реестр `HKCU\Software\OpenIsland` (старый `Software\DynamicIsland` мигрируется автоматически) |
| Доп. библиотеки | `System.Security.Cryptography.ProtectedData` (DPAPI-токен), `System.Management` (температура через WMI) |

Проверено на Windows 11 (сборка 19041+). Требуется .NET 8 Desktop Runtime; для сборки — .NET 8 SDK.

---

## 📂 Структура репозитория

```text
OpenIsland/
├── desktop/OpenIsland/         # Основное нативное приложение (C# WPF)
│   ├── MainWindow.xaml         # Разметка островка, панелей и карточек
│   ├── MainWindow.*.cs         # Логика по модулям (медиа, таймер, OpenCode, ...)
│   ├── OpenCodeService.cs      # REST/SSE клиент OpenCode, токен, статусы
│   ├── OpenCodeSelfTest.cs     # Диагностика интеграции (--selftest)
│   ├── Chime.cs                # Звук завершения задачи (staplebops-01, MIT)
│   ├── PerfService.cs          # Мониторинг ПК: CPU, RAM, сеть, GPU, температура
│   ├── ClipboardService.cs     # История буфера обмена (только в памяти)
│   ├── Settings.cs             # Настройки в реестре
│   ├── Native.cs               # Win32-вызовы (topmost, toolwindow, DWM)
│   ├── Fonts/ Gadgets/ Assets/ # Шрифты, графика виджетов, иконка и звук
│   └── OpenIsland.csproj
├── installer/OpenIsland.iss    # Скрипт Inno Setup (per-user установщик)
├── scripts/build.ps1           # Сборка, запуск, self-test, тесты и установщик
├── tests/OpenIsland.Tests/     # Юнит-тесты (xUnit): логика без UI
├── bin/OpenIsland/             # Результат сборки (OpenIsland.exe)
├── docs/                       # Архитектура, product map, roadmap, заметки по авторизации
└── Запуск_OpenIsland.bat       # Запуск в один клик (собирает при необходимости)
```

---

## 🚀 Быстрый старт

### Сборка и запуск

```powershell
# Сборка Release в bin\OpenIsland
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\build.ps1

# Сборка и немедленный запуск
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\build.ps1 -Run

# Диагностика интеграции с OpenCode (без UI)
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\build.ps1 -SelfTest

# Юнит-тесты (xUnit): разбор OpenCode, апдейтер, Countdown, настройки
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\build.ps1 -Test

# Сборка установщика (нужен Inno Setup 6: winget install JRSoftware.InnoSetup)
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\build.ps1 -Installer
```

Либо двойной клик по `Запуск_OpenIsland.bat` — скрипт соберёт приложение, если `bin\OpenIsland\OpenIsland.exe` ещё нет.

Если .NET SDK не установлен в системе, `scripts\build.ps1` автоматически скачает SDK 8.0 в локальную папку `.dotnet/` (она исключена из git).

### Установка

Для обычных пользователей публикуется установщик `OpenIslandSetup-<версия>.exe` (Inno Setup) рядом с portable-сборкой `OpenIsland.exe` в [GitHub Releases](https://github.com/mmmatveyz/OpenIsland/releases).

- Ставится **для текущего пользователя** в `%LOCALAPPDATA%\Programs\OpenIsland` — права администратора не нужны, а встроенный апдейтер может подменять exe без UAC.
- Ярлык в «Пуск», запись в «Программы и компоненты» и корректное удаление; ярлык на рабочем столе и автозапуск — по желанию.
- Сборка установщика: `scripts\build.ps1 -Installer` (требует установленный Inno Setup 6). Скрипт установщика — [`installer/OpenIsland.iss`](./installer/OpenIsland.iss).

### Требования

- Windows 10 19041+ / Windows 11.
- .NET 8 SDK для сборки (ставится скриптом) и .NET 8 Desktop Runtime для запуска.
- Запущенный OpenCode Desktop (или сервис `opencode`) для AI Hub. Остальные виджеты работают автономно.

### Полезные аргументы запуска

| Аргумент | Назначение |
| :--- | :--- |
| `--view OpenCodeBig` | Открыть сразу карточку OpenCode AI Hub |
| `--selftest` | Прогнать headless-проверку OpenCode (сессии, модели, промпт, ответ) |

Логи приложения: `%LOCALAPPDATA%\OpenIsland\logs\app.log` (ротация при 1 МБ).

---

## 🔐 Безопасность

- Токен OpenCode хранится только в зашифрованном виде через Windows DPAPI для текущего пользователя; старый plaintext-файл автоматически мигрируется и удаляется.
- Пароли и токены не попадают в репозиторий и логи.
- Все запросы идут на локальный демон `http://127.0.0.1:<port>`, порт определяется через `opencode service status`.
- Единственный внешний запрос — проверка обновлений в GitHub Releases; ассет сверяется по `sha256`-дайджесту.

Нашли уязвимость — не создавайте публичный issue, см. [SECURITY.md](./SECURITY.md).

---

## 📚 Документация

- [**docs/ARCHITECTURE.md**](./docs/ARCHITECTURE.md) — устройство окна, слоёв рендеринга, сервисов и интеграции с OpenCode.
- [**docs/ROADMAP.md**](./docs/ROADMAP.md) — состояние фаз и дальнейшие планы.
- [**docs/PRODUCT.md**](./docs/PRODUCT.md) — карта функций: что пришло из апстрима, что наше, что осталось «чужим» в идентичности и план дальнейших работ.
- [**docs/AI_SESSION_AUTH.md**](./docs/AI_SESSION_AUTH.md) — отложенный дизайн авторизации по подпискам (Claude Pro / ChatGPT Plus / Gemini Advanced).
- [**AGENTS.md**](./AGENTS.md) — правила и контекст для ИИ-агентов, работающих с репозиторием.

---

## 🙏 Благодарности

- **Апстрим `mihailkotovski/DynamicIsland`** — ядро островка (окно-оверлей, пружинная физика, стекло, медиа, оформление), из которого вырос OpenIsland.
- **[OpenCode](https://github.com/sst/opencode)** (sst) — звук завершения задачи `staplebops-01`, распространяется по лицензии MIT.

---

## 📄 Лицензия

MIT License — см. [LICENSE](./LICENSE). Copyright (c) 2026 OpenIsland.
