using System.IO;

namespace DynamicIsland;

/// <summary>
/// Диагностический прогон интеграции с OpenCode без участия UI.
/// Запуск: OpenIsland.exe --selftest
/// Результат пишется в %LOCALAPPDATA%\OpenIsland\logs\app.log.
///
/// Проверяет REST- и SSE-контур приложения детерминированно: соединение,
/// списки сессий/моделей, создание сессии, выбор модели, отправку задачи,
/// факт получения SSE-события о старте выполнения и удаление сессии.
/// Ответ модели проверяется как опциональный (медленный/внешний ресурс):
/// включить строгий режим можно переменной OPENISLAND_SELFTEST_STRICT=1.
/// </summary>
static class OpenCodeSelfTest
{
    const int ConnectTimeoutMs = 10000;
    const int ListTimeoutMs = 5000;
    const int StartTimeoutMs = 20000;
    const int DefaultAnswerTimeoutMs = 60000;

    public static async Task<int> RunAsync()
    {
        App.LogDebug("SELFTEST: started.");
        OpenCodeService service = new();
        string? sessionId = null;
        string directory = Path.Combine(Path.GetTempPath(), "OpenIsland-selftest");

        try
        {
            if (!await WaitForAsync(() => service.IsConnected, ConnectTimeoutMs))
            {
                App.LogDebug("SELFTEST: FAIL - OpenCode service is not reachable.");
                return 2;
            }

            if (!await WaitForAsync(() => service.Sessions.Count > 0, ListTimeoutMs))
            {
                App.LogDebug("SELFTEST: FAIL - session list is empty.");
                return 3;
            }

            if (!await WaitForAsync(() => service.Models.Count > 0, ListTimeoutMs))
            {
                App.LogDebug("SELFTEST: FAIL - model list is empty.");
                return 4;
            }

            if (!await WaitForAsync(() => service.EventsReceived, ListTimeoutMs))
            {
                App.LogDebug("SELFTEST: FAIL - no SSE events received.");
                return 11;
            }

            App.LogDebug($"SELFTEST: connected, sessions={service.Sessions.Count}, models={service.Models.Count}.");

            Directory.CreateDirectory(directory);

            bool created = await service.CreateSessionAsync("OpenIsland self-test", directory);
            sessionId = service.SelectedSessionId;
            if (!created || string.IsNullOrEmpty(sessionId))
            {
                App.LogDebug($"SELFTEST: FAIL - session creation failed ({service.LastError}).");
                return 5;
            }

            App.LogDebug($"SELFTEST: session created {sessionId}.");

            string? model = Environment.GetEnvironmentVariable("OPENISLAND_SELFTEST_MODEL");
            if (!string.IsNullOrWhiteSpace(model))
            {
                bool picked = await service.SelectModelAsync(model);
                App.LogDebug(picked
                    ? $"SELFTEST: using model '{model}'."
                    : $"SELFTEST: model '{model}' not applied ({service.LastError}).");
            }

            long startedBefore = service.ExecutionStartedCount;
            bool sent = await service.SendPromptAsync("Ответь ровно одним словом: ok");
            if (!sent)
            {
                App.LogDebug($"SELFTEST: FAIL - prompt was not accepted ({service.LastError}).");
                return 6;
            }

            App.LogDebug("SELFTEST: prompt accepted, waiting for execution...");

            // Подтверждаем сквозной контур: демон начал выполнение, и событие дошло до нас через SSE.
            bool started = await WaitForAsync(
                () => service.ExecutionStartedCount > startedBefore, StartTimeoutMs);
            if (!started)
            {
                App.LogDebug($"SELFTEST: FAIL - execution did not start (busy={service.IsBusy}, status='{service.StatusText}').");
                return 7;
            }

            App.LogDebug("SELFTEST: execution started (SSE confirmed).");

            int answerTimeout = int.TryParse(
                Environment.GetEnvironmentVariable("OPENISLAND_SELFTEST_ANSWER_MS"), out int configured) && configured > 0
                ? configured
                : DefaultAnswerTimeoutMs;

            bool answered = await WaitForAsync(
                () => service.LastAssistantText.Contains("ok", StringComparison.OrdinalIgnoreCase),
                answerTimeout);

            bool strict = Environment.GetEnvironmentVariable("OPENISLAND_SELFTEST_STRICT") == "1";

            if (answered)
            {
                string answer = service.LastAssistantText.Replace("\r", " ").Replace("\n", " ").Trim();
                if (answer.Length > 240) answer = answer[..240];
                App.LogDebug($"SELFTEST: PASS - answer='{answer}'.");
                return 0;
            }

            if (strict)
            {
                App.LogDebug($"SELFTEST: FAIL - no answer within timeout (busy={service.IsBusy}, status='{service.StatusText}').");
                return 8;
            }

            App.LogDebug("SELFTEST: PASS - plumbing verified; model answer not observed "
                + "(set OPENISLAND_SELFTEST_STRICT=1 to require one).");
            return 0;
        }
        catch (Exception ex)
        {
            App.LogDebug($"SELFTEST: FAIL - {ex}");
            return 10;
        }
        finally
        {
            if (sessionId != null)
            {
                try
                {
                    if (!await service.DeleteSessionAsync(sessionId))
                    {
                        App.LogDebug($"SELFTEST: session cleanup returned failure for {sessionId}.");
                    }
                }
                catch (Exception ex)
                {
                    App.LogDebug($"SELFTEST: session cleanup failed: {ex.Message}");
                }
            }

            try
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
            catch
            {
                // Временный каталог уже мог быть удалён.
            }

            service.Dispose();
        }
    }

    static async Task<bool> WaitForAsync(Func<bool> condition, int timeoutMs)
    {
        DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return true;
            await Task.Delay(250);
        }
        return condition();
    }
}
