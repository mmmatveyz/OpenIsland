# Авторизация по подпискам (Claude Pro, ChatGPT Plus, Gemini Advanced)

В этом документе описывается механизм, позволяющий пользователю использовать свои существующие платные подписки потребительского уровня без необходимости оплачивать API за каждый токен.

---

## 1. Проблема и решение

| Подход | Плюсы | Минусы |
| :--- | :--- | :--- |
| **Официальный API (Pay-as-you-go)** | Стабильность, простота кода, чистый JSON. | Требует отдельной оплаты за токены. Платная подписка пользователя (ChatGPT Plus $20/мес, Claude Pro $20/мес) **не действует** на API! |
| **Session Auth (Браузерная сессия)** | Пользователь использует уже оплаченную подписку без доплат. | Требует прохождения капчи/Cloudflare при первом входе, сессия может периодически требовать обновления. |

В **OpenIsland** реализован **гибридный подход**:
Пользователь может в один клик выбрать удобный для себя вариант:
1. Войти через аккаунт с подпиской (Web Session).
2. Или вставить собственный API-ключ (OpenAI, Anthropic, Gemini, Ollama).

---

## 2. Механизм сессионной авторизации (Session Auth Flow)

Electron позволяет создавать изолированные Chromium-сессии с постоянным хранением данных (`persist:` partitions):

- `persist:ai_chatgpt` — хранилище для ChatGPT.
- `persist:ai_claude` — хранилище для Claude.
- `persist:ai_gemini` — хранилище для Gemini.

### 2.1. Пошаговый сценарий входа (Login Flow)

```
[ Пользователь нажимает "Подключить Claude Pro" ]
                     │
                     ▼
[ Main Process открывает AuthWindow ]
   - URL: https://claude.ai/login
   - Session: session.fromPartition('persist:ai_claude')
   - Пользователь видит стандартное окно авторизации (Google / Email)
   - Успешно проходит Cloudflare Turnstile и капчи вручную
                     │
                     ▼
[ Авторизация завершена (детект редиректа на claude.ai/chats) ]
   - AuthWindow закрывается или скрывается
   - Сессионные cookies и токен авторизации (sessionKey) 
     сохраняются в защищенном хранилище (safeStorage)
                     │
                     ▼
[ OpenIsland AI Service готов к работе ]
   - Запросы к ИИ отправляются от лица авторизованной сессии
   - Доступны все возможности подписки (Claude 3.5 Sonnet, Thinking, Projects)
```

---

## 3. Интеграция по провайдерам

### 3.1. Claude (Anthropic)
- **Точка входа**: `https://claude.ai/login`
- **Ключевой маркер сессии**: cookie `sessionKey` в домене `.claude.ai`.
- **Эндпоинты внутренней веб-сессии**: `https://api.claude.ai/api/organizations/.../chat_conversations`.
- **Дополнительный вариант**: авторизация через CLI-утилиту `claude` (Anthropic Claude Code OAuth), если установлена на машине.

### 3.2. ChatGPT (OpenAI)
- **Точка входа**: `https://chatgpt.com/auth/login`
- **Маркер сессии**: JWT-токен сессии из `https://chatgpt.com/api/auth/session` (`accessToken`) и cookies `__Secure-next-auth.session-token`.
- **Внутренний эндпоинт**: `https://chatgpt.com/backend-api/conversation`.
- **Поддержка подписки**: доступ к моделям GPT-4o, o1, Advanced Data Analysis в рамках Plus-лимитов.

### 3.3. Google Gemini
- **Точка входа**: `https://gemini.google.com/`
- **Маркер сессии**: cookies аккаунта Google (`SID`, `HSID`, `SSID`, `__Secure-1PSID`).
- **Поддержка подписки**: Gemini Advanced (Gemini 1.5 Pro / Ultra) с расширенным контекстным окном.

---

## 4. Безопасность и конфиденциальность

1. **Никакой отправки на сторонние сервера**: все запросы идут исключительно между локальной машиной пользователя и официальными серверами провайдеров (OpenAI, Anthropic, Google).
2. **Шифрование на уровне ОС**: токены доступа шифруются с помощью `electron.safeStorage`, использующего Windows DPAPI (Data Protection API).
3. **Раздельные песочницы**: каждый сервис работает в изолированной партиции и не имеет доступа к кукам других сервисов.
