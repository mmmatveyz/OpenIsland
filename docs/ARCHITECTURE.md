# Архитектура OpenIsland

Документ описывает устройство нативного приложения `desktop/OpenIsland` (C# WPF, .NET 8), оконный менеджмент, рендеринг островка, сервисы виджетов и интеграцию с OpenCode.

---

## 1. Общая схема системы

```text
+--------------------------------------------------------------------------+
|                               WINDOWS 10/11                              |
+--------------------------------------------------------------------------+
      │ Win32 / DWM / GSMTC        │ Media, power, network      │ OpenCode daemon
      ▼                            ▼                            ▼
+---------------------+   +----------------------+   +-------------------------+
| Native.cs           |   | MediaService /       |   | OpenCodeService.cs      |
| topmost, toolwindow |   | AudioService /       |   | REST + SSE + pairing    |
| fullscreen checks   |   | NetworkService / ... |   | DPAPI token storage     |
+---------------------+   +----------------------+   +-------------------------+
      │                            │                            │
      └──────────────┬─────────────┴──────────────┬─────────────┘
                     ▼                            ▼
+--------------------------------------------------------------------------+
|                    MainWindow (WPF, UI thread)                           |
|                                                                          |
|  - View/Panel routing: TargetView(), ShowPanel(), WorkArea()             |
|  - Spring physics: Spring.cs + MainWindow.Shape.cs                       |
|  - Custom visuals: DrawingVisual widgets (spectrum, equ, goo, glass)     |
|  - Modules: MainWindow.Media.cs, .Timer.cs, .Status.cs, .Menu.cs, ...    |
|  - OpenCode UI: MainWindow.OpenCode.cs                                   |
+--------------------------------------------------------------------------+
                     │
                     ▼
+--------------------------------------------------------------------------+
|       Transparent always-on-top overlay window (HostWidth x HostHeight)  |
|       Rounded "island" pill, morphs between compact and expanded states  |
+--------------------------------------------------------------------------+
```

Приложение однопроцессное. Сетевые и фоновые операции выполняются асинхронно, UI обновляется в потоке диспетчера WPF.

---

## 2. Окно и оконный менеджмент

### 2.1. Параметры окна

`MainWindow.xaml`:

```xml
<Window WindowStyle="None" AllowsTransparency="True" Background="Transparent"
        ShowInTaskbar="False" ShowActivated="False" Topmost="True"
        ResizeMode="NoResize" Width="620" Height="592">
```

- Окно-хост имеет фиксированный размер `620 × 592` (`HostWidth`/`HostHeight` в `MainWindow.xaml.cs`), позиционируется по центру верхней кромки рабочего стола.
- `AllowsTransparency` + прозрачный фон: клики получают только реальные элементы островка; прозрачные области не перехватывают мышь.
- `Native.cs` дополнительно помечает окно как `WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`, чтобы оно не появлялось в Alt+Tab и не забирало фокус у активного приложения. Для стеклянных поверхностей используется расширенный стиль `WS_EX_NOREDIRECTIONBITMAP | WS_EX_LAYERED`.
- `StayOnTop()` периодически возвращает окно наверх, не активируя его (`SWP_NOACTIVATE`).

### 2.2. Позиционирование и полноэкранный режим

- Островок выравнивается по центру рабочей области; отступ сверху задаётся настройкой `Gap`, масштаб — `Scale`.
- При активном полноэкранном приложении или игре (проверка через Win32 foreground window и его стилей) островок скрывается, если включена настройка `HideFullscreen`.
- Окно остаётся поверх всех окон (`Topmost`), но не мешает играм в полноэкранном режиме.

### 2.3. Мышь и интерактивность

- `MainWindow.Pointer.cs`: наведение, клик, drag, правый клик (контекстное меню), прокрутка.
- Пружины (`Spring.cs`) обеспечивают плавное «дыхание» и морфинг без резких скачков.
- Наведение на капсулу включает компактное превью (обложка, прогресс, эквалайзер) и увеличивает кликабельную область точечно, не раздувая окно.

### 2.4. Глобальный хоткей

`RegisterHotKey` (Win32) регистрирует **Ctrl+Alt+O** при инициализации окна. Сообщение `WM_HOTKEY` перехватывается хуком `HwndSource` в `MainWindow`:

- если карточка OpenCode уже открыта — она сворачивается;
- иначе открывается `Panel.OpenCode`, окно поднимается наверх, фокус переводится в поле задачи.

Хоткей освобождается в `OnClosed`. Если комбинация занята другой программой, приложение продолжает работать, а в лог пишется предупреждение. Ctrl+Alt+Space не используется, так как часто занят системными утилитами.

### 2.5. Активация окна и авто-сворачивание

- По умолчанию окно неактивируемо (`WS_EX_NOACTIVATE`), чтобы не отбирать фокус. Перед вводом текста (поле задачи OpenCode) `EnterTextEntry()` запоминает текущее активное окно, включает активацию (`MakeActivatable(true)`), поднимает и активирует островок; `LeaveTextEntry()` снимает фокус, возвращает передний план прежнему окну и снова запрещает активацию.
- Раскрытая панель сворачивается сама по таймеру `_collapseTimeout`: `CollapseDelay` (≈0.55 с) для обычных панелей и `LongCollapseDelay` (2.5 с) для `Panel.OpenCode`, чтобы успеть прочитать текст или начать ввод. Наведение мыши отменяет таймер, уход курсора — перезапускает, набор в поле задачи (`OpenCodePromptBox_TextChanged`) продлевает его.
- `Collapse()` не сворачивает панель, пока курсор на островке. `CollapseNow()` сворачивает немедленно: по повторному нажатию хоткея и при потере активации (`OnDeactivated`, например клик по другому окну).

---

## 3. Формы островка и рендеринг

### 3.1. View и Panel

Состояния UI описаны двумя перечислениями в `MainWindow.xaml.cs`:

- `View` — конкретный визуальный экран (`Idle`, `Sound`, `Media`, `Volume`, `TimerBig`, `Menu`, `Settings`, `OpenCodeBig`, `Perf`, `Clipboard`, ...).
- `Panel` — логическая раскрытая панель (`Player`, `Timer`, `Menu`, `Settings`, `OpenCode`, `Perf`, `Clipboard`, ...).

`TargetView()` выбирает экран по приоритету: принудительный view → активная панель → активный агент OpenCode → медиа → звук (`_sounding`) → таймер → idle. `ShowPanel()`/`SelectPanel()` переключают панель и анимируют переход.

### 3.2. Геометрия капсулы

`PillShapes` задаёт базовые `Width`, `Height`, `Radius` для каждого `View`. Динамические размеры (например, `MediaBig` с крупной обложкой или `Update`) вычисляются в `ShapeOf()`.

`MainWindow.Shape.cs`:

- четыре пружины `_width`, `_height`, `_radius`, `_userScale` плавно интерполируют форму;
- тень, блики и «жидкое» стекло (`Liquid.cs`, `Goo.cs`, `Glass.cs`) рисуются вручную;
- пузырьки уведомлений (таймер, громкость, наушники) анимируются отдельными пружинами;
- `DrawingSurfaces.cs` и `Visuals.cs` инкапсулируют недорогие `DrawingVisual`-поверхности.

### 3.3. Модули виджетов

| Модуль | Файлы | Возможности |
| :--- | :--- | :--- |
| Плеер | `MainWindow.Media.cs`, `MediaService.cs`, `Cover.cs`, `Scrubber.cs`, `PlayerBar.cs` | Управление системным плеером через GSMTC, обложка, прогресс, перемотка, источник приложения |
| Эквалайзер | `MainWindow.Media.cs`, `SpectrumService.cs`, `SpectrumAnalyzer.cs`, `BandMeter.cs`, `Equalizer.cs`, `AudioService.cs` | Живой спектр из реального вывода звука (WASAPI loopback); капсула `View.Sound` при любом системном звуке без активного трека (дешёвый master peak через `IAudioMeterInformation`) |
| Таймер | `MainWindow.Timer.cs`, `Countdown.cs`, `Digits.cs`, `Ring.cs` | Таймер, секундомер, будильник |
| Статус | `MainWindow.Status.cs`, `CoreAudio.cs`, `Headset.cs`, `NetworkService.cs` | Громкость, заряд, наушники, сеть |
| Внешний вид | `MainWindow.Menu.cs`, `Toggle.cs`, `Aura.cs`, `StarField.cs`, `BackdropPreview.cs` | Масштаб, отступ, стекло, фон, акцент, вид эквалайзера, анимации |
| OpenCode | `MainWindow.OpenCode.cs`, `OpenCodeService.cs`, `OpenCodeSelfTest.cs`, `Chime.cs` | AI Hub: сессии, модели, промпты, разрешения, стриминг, звук завершения задачи и тост о её окончании (иконка трея) |
| Мониторинг ПК | `MainWindow.Perf.cs`, `PerfService.cs` | CPU (`GetSystemTimes`), RAM (`GlobalMemoryStatusEx`), сеть (`NetworkInterface`), GPU (PDH), температура (WMI). Опрос — только пока карточка открыта |
| Буфер обмена | `MainWindow.Clipboard.cs`, `ClipboardService.cs` | История последних 30 фрагментов (в памяти), захват по `WM_CLIPBOARDUPDATE`, вставка в один клик |
| Обновления | `Updater.cs`, XAML `UpdatePage` | Проверка релизов [`mmmatveyz/OpenIsland`](https://github.com/mmmatveyz/OpenIsland), скачивание ассета `OpenIsland.exe`, сверка `sha256`-дайджеста, подмена файла с автоперезапуском |

> Апстрим-модули «Тексты песен», «Запись OBS», «Полка файлов», «Погода» и «QR-код» вырезаны из проекта (см. `docs/PRODUCT.md` §2.5).

Настройки хранятся в реестре `HKCU\Software\OpenIsland` (`Settings.cs`); при первом чтении значения автоматически переносятся из старого ключа `HKCU\Software\DynamicIsland`. Автозапуск — в `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` под именем `OpenIsland` (`Autostart.cs`, старое значение `DynamicIsland` переносится). Значок приложения живёт в системном трее (`MainWindow.Tray.cs`, `Native.cs`).

---

## 4. Интеграция с OpenCode

### 4.1. Обнаружение и авторизация

`OpenCodeService.cs`:

1. Находит CLI `opencode-cli.exe` в `%APPDATA%\ai.opencode.desktop\cli\*`, `%LOCALAPPDATA%\Programs\@opencode-aidesktop\resources` или `PATH`.
2. Выполняет `opencode service status`, извлекает URL демона (обычно `http://127.0.0.1:28894`) и проверяет доступность `/api/info`. Если порт сменился — сервис находится заново.
3. При отсутствии токена запускает `opencode pair`, забирает одноразовую ссылку, получает токен и сохраняет его через DPAPI (`%LOCALAPPDATA%\OpenIsland\opencode-token.bin`). При `401` пейринг повторяется автоматически.
4. Токен передаётся в заголовке `Authorization: Basic base64("opencode:<token>")`.

### 4.2. REST API

| Назначение | Метод и путь |
| :--- | :--- |
| Проверка сервиса | `GET /api/info` |
| Список сессий | `GET /api/session` |
| Активные сессии | `GET /api/session/active` |
| Список моделей | `GET /api/model` |
| Создание сессии | `POST /api/session` (`title`, `location.directory`, `model`) |
| Удаление сессии | `DELETE /api/session/{id}` |
| Отправка задачи | `POST /api/session/{id}/prompt` (`{"text": "..."}`) |
| Смена модели | `POST /api/session/{id}/model` (`{"model": {"id", "providerID"}}`) |
| Остановка агента | `POST /api/session/{id}/interrupt` |
| Сообщения сессии | `GET /api/session/{id}/message?limit=8&order=desc` |
| Разрешения | `GET /api/session/{id}/permission`, `POST /api/session/{id}/permission/{requestID}/reply` (`decision`: `once` / `always` / `reject`) |
| Поток событий | `GET /api/event` (SSE) |

### 4.3. SSE и состояние

Поток `/api/event` читается построчно; кадры `data: {json}` разбираются в `HandleEvent()`. Ключевые события:

- `session.status` — `idle` / `running` / `retry` (индикатор и текст статуса);
- `session.execution.started/succeeded/failed` — старт и завершение задачи, ошибки;
- `session.text.started/delta/ended` — живой текст ответа (с ограничением буфера);
- `permission.asked/replied` — запросы доступа агента и их закрытие;
- `session.model.selected` — смена модели;
- `session.created/deleted/renamed` — обновление списка сессий.

Параллельно раз в 2 секунды выполняется `RefreshAsync()`: обновляет сессии, модели, активную сессию и разрешения. Это страховка на случай разрыва SSE (поток переподключается через 3 секунды). Уведомления UI троттлятся (~120 мс) и схлопываются, чтобы не перерисовывать островок на каждый токен.

### 4.4. UI OpenCode AI Hub

`MainWindow.OpenCode.cs` + блок `OpenCodeBigView` в XAML (панель шириной 380):

- счётчик и переключение сессий (`‹ 1 / 6 ›`), создание новой сессии;
- выбор модели (`‹ МОДЕЛЬ ›`);
- поле ввода задачи (`Enter` — отправить);
- блок ответа агента (живой текст и финальный ответ из истории сообщений);
- панель разрешений «Разрешить / Отклонить»;
- кнопка «Стоп» для прерывания выполнения;
- звук завершения: при переходе агента из «работает» в «готово» играет `Chime` (звук OpenCode, MIT). MP3 встроен в exe (`Resource`) и один раз выгружается в `%LOCALAPPDATA%\OpenIsland\staplebops-01.mp3`, потому что `MediaPlayer` умеет открывать только файл на диске;
- тост о завершении: если задача выполнялась дольше `TaskToastAfter` (15 с), вместе со звуком показывается уведомление Windows через иконку трея (`Shell_NotifyIcon` + `NIF_INFO`, `Native.ShowBalloon`); выключается настройкой `Settings.TaskToast`.

Компактная капсула `View.OpenCode` показывает статус агента; при работе агента островок автоматически переходит в это состояние.

Офлайн-состояние: без соединения заголовок карточки — «OpenCode не запущен», навигация по сессиям/моделям, поле ввода и «Отправить» приглушены и отключены, кнопка запуска выделена акцентом и поднимает OpenCode Desktop. Клик по островку, когда ничего не играет и OpenCode не подключён, открывает меню (`Panel.Menu`), а не пустую карточку агента. После старта демона состояние возвращается само (опрос раз в 2 с, обнаружение — раз в 15 с).

### 4.5. Диагностика

`--selftest` (`OpenCodeSelfTest.cs`) в headless-режиме проверяет: доступность демона, непустые списки сессий и моделей, создание временной сессии, смену модели, отправку промпта и получение ответа. Сессия удаляется после теста. Результат пишется в `%LOCALAPPDATA%\OpenIsland\logs\app.log`.

---

## 5. Потоки и производительность

- Все сетевые вызовы — `async`/`await`; UI обновляется через `Dispatcher.BeginInvoke`.
- `OpenCodeService` использует отдельный `HttpClient` с бесконечным таймаутом для SSE и обычный клиент с таймаутом 10 с для REST.
- Опрос OpenCode — раз в 2 секунды; при отсутствии соединения интервал сохраняется, но discovery выполняется не чаще раза в 15 секунд.
- Тяжёлые визуальные эффекты включаются только для видимых состояний; фоновые таймеры останавливаются при скрытии островка.
- `PerfService` опрашивается **только пока открыта карточка мониторинга**: `UpdateView` вызывает `SetActive(target == View.Perf)`, и пока карточка закрыта, таймер спит (`Timeout.Infinite`). Замеры идут на фоновом потоке, готовые значения приходят в UI через `Dispatcher`.
- Захват буфера обмена — это событие `WM_CLIPBOARDUPDATE`, а не опрос: окно получает сообщение из системы (`Native.WatchClipboard`) и только тогда читает текст. Подписка снимается в `OnClosed`.
- Логирование (`App.LogDebug`) — в `%LOCALAPPDATA%\OpenIsland\logs\app.log` с ротацией при 1 МБ; в релизе — только значимые события и ошибки.

---

## 6. Безопасность

1. **Токен OpenCode** хранится только в зашифрованном виде (DPAPI, `CurrentUser`). Plaintext-файл старого формата мигрируется и удаляется.
2. **Пейринг** выполняется через официальный `opencode pair`; ссылка одноразовая и живёт 5 минут.
3. **Логи** не содержат токенов, cookies и приватного содержимого; ошибки API логируются в усечённом виде.
4. **Сеть** — только локальный демон `127.0.0.1`. Внешние интеграции (погода, тексты песен) из проекта удалены; единственный внешний запрос — проверка релизов на GitHub (`api.github.com`), анонимно, без передачи данных пользователя.
5. **Окно** не активируется и не появляется в панели задач, что снижает риск перехвата ввода.
6. **Буфер обмена** хранится только в памяти процесса (`ClipboardService`): фрагменты не пишутся на диск, не уходят в логи и не передаются по сети; выключатель `Settings.Clipboard` очищает историю.

---

## 7. Сборка

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\build.ps1
```

- Проект: `desktop/OpenIsland/OpenIsland.csproj`, имя сборки — `OpenIsland`, пространство имён — `OpenIsland`.
- Вывод: `bin/OpenIsland/OpenIsland.exe`.
- Если .NET 8 SDK не найден, `scripts/build.ps1` устанавливает его в `.dotnet/`.
- Аргументы запуска: `--view OpenCodeBig` (открыть карточку OpenCode), `--view Perf` / `--view Clipboard` (карточки мониторинга и буфера), `--selftest` (диагностика).
