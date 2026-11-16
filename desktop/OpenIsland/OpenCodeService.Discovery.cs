using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OpenIsland;

public sealed partial class OpenCodeService
{
    // ---------------------------------------------------------------- settings

    static string? FindCliPath()
    {
        List<string> candidates = [];

        string cliRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ai.opencode.desktop", "cli");

        try
        {
            if (Directory.Exists(cliRoot))
            {
                candidates.AddRange(Directory.GetDirectories(cliRoot)
                    .OrderByDescending(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
                    .Select(path => Path.Combine(path, "opencode-cli.exe")));
            }
        }
        catch
        {
            // Игнорируем проблемы доступа к каталогу.
        }

        candidates.Add(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs", "@opencode-aidesktop", "resources", "opencode-cli.exe"));

        string? pathVariable = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(pathVariable))
        {
            foreach (string directory in pathVariable.Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(directory)) continue;
                candidates.Add(Path.Combine(directory.Trim(), "opencode-cli.exe"));
                candidates.Add(Path.Combine(directory.Trim(), "opencode.exe"));
            }
        }

        foreach (string candidate in candidates)
        {
            try
            {
                if (File.Exists(candidate)) return candidate;
            }
            catch
            {
                // Некорректный путь — просто пропускаем.
            }
        }

        return null;
    }

    static async Task<string> RunCliAsync(string cli, string arguments)
    {
        try
        {
            return await Task.Run(() =>
            {
                using Process? process = Process.Start(new ProcessStartInfo
                {
                    FileName = cli,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });

                if (process == null) return "";

                Task<string> output = process.StandardOutput.ReadToEndAsync();
                Task<string> error = process.StandardError.ReadToEndAsync();

                if (!process.WaitForExit(CliTimeoutMs))
                {
                    try { process.Kill(entireProcessTree: true); } catch { }
                }

                return $"{output.GetAwaiter().GetResult()}\n{error.GetAwaiter().GetResult()}";
            });
        }
        catch (Exception ex)
        {
            App.LogDebug($"OpenCode: CLI '{arguments}' failed: {ex.Message}");
            return "";
        }
    }

    async Task DetectServiceUrlAsync(bool force)
    {
        if (!force && DateTime.UtcNow.Ticks - _lastDiscoveryAt < TimeSpan.FromSeconds(15).Ticks)
        {
            return;
        }
        _lastDiscoveryAt = DateTime.UtcNow.Ticks;

        string? cli = await Task.Run(FindCliPath);
        string? discovered = null;

        if (cli != null)
        {
            string output = await RunCliAsync(cli, "service status");
            Match match = Regex.Match(output, @"https?://[^\s]+");
            if (match.Success)
            {
                string candidate = match.Value.TrimEnd('/');
                // Токен авторизации отправляется по этому адресу — принимаем только loopback.
                if (Uri.TryCreate(candidate, UriKind.Absolute, out Uri? uri) && uri.IsLoopback)
                {
                    discovered = candidate;
                }
                else
                {
                    App.LogDebug("OpenCode: ignoring non-loopback discovered URL.");
                }
            }
        }

        List<string> candidates = [];
        if (!string.IsNullOrEmpty(discovered)) candidates.Add(discovered);
        if (!candidates.Contains(_baseUrl)) candidates.Add(_baseUrl);
        if (!candidates.Contains(DefaultBaseUrl)) candidates.Add(DefaultBaseUrl);

        foreach (string candidate in candidates)
        {
            if (await ProbeAsync(candidate))
            {
                if (_baseUrl != candidate)
                {
                    App.LogDebug($"OpenCode: service discovered at {candidate}.");
                    _baseUrl = candidate;
                }
                return;
            }
        }

        App.LogDebug("OpenCode: service not found.");
    }

    async Task<bool> ProbeAsync(string baseUrl)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, baseUrl.TrimEnd('/') + "/api/info");
            using var response = await _http.SendAsync(request);
            int code = (int)response.StatusCode;
            return code is >= 200 and < 500;
        }
        catch
        {
            return false;
        }
    }

    async Task<bool> PairAsync()
    {
        string? cli = await Task.Run(FindCliPath);
        if (cli == null) return false;

        try
        {
            string output = await RunCliAsync(cli, "pair");
            Match match = Regex.Match(output, @"https?://[^\s/]+/auth/connect/[\w\-]+");
            if (!match.Success)
            {
                App.LogDebug("OpenCode: pair link was not found.");
                return false;
            }

            using var pairClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            string json = await pairClient.GetStringAsync(match.Value);
            using JsonDocument document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("token", out JsonElement tokenElement)) return false;

            string? token = tokenElement.GetString();
            if (string.IsNullOrWhiteSpace(token)) return false;

            _token = token;
            SaveToken(token);
            App.LogDebug("OpenCode: paired successfully.");
            return true;
        }
        catch (Exception ex)
        {
            App.LogDebug($"OpenCode: pairing failed: {ex.Message}");
            return false;
        }
    }

    // ---------------------------------------------------------------- token

    void LoadCachedToken()
    {
        try
        {
            if (File.Exists(TokenFile))
            {
                byte[] protectedBytes = File.ReadAllBytes(TokenFile);
                byte[] plainBytes = ProtectedData.Unprotect(protectedBytes, TokenEntropy, DataProtectionScope.CurrentUser);
                _token = Encoding.UTF8.GetString(plainBytes).Trim();
                return;
            }

            if (File.Exists(LegacyTokenFile))
            {
                string legacy = File.ReadAllText(LegacyTokenFile).Trim();
                if (legacy.Length > 0)
                {
                    _token = legacy;
                    SaveToken(legacy);
                    try { File.Delete(LegacyTokenFile); } catch { }
                    App.LogDebug("OpenCode: legacy token migrated to DPAPI storage.");
                }
            }
        }
        catch (Exception ex)
        {
            App.LogDebug($"OpenCode: token read failed: {ex.Message}");
        }
    }

    static void SaveToken(string token)
    {
        try
        {
            Directory.CreateDirectory(DataDirectory);
            byte[] protectedBytes = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(token), TokenEntropy, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(TokenFile, protectedBytes);
        }
        catch (Exception ex)
        {
            App.LogDebug($"OpenCode: token save failed: {ex.Message}");
        }
    }

}
